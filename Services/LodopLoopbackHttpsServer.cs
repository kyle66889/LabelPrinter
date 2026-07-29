using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace LabelPrinter.Services;

/// <summary>
/// Minimal HTTPS listener on loopback (no http.sys / netsh / admin). Used for C-Lodop's
/// https ports 8443/8444 so MZL pages on https://fbd... can load CLodopfuncs.js.
/// </summary>
internal sealed class LodopLoopbackHttpsServer : IDisposable
{
    private readonly int _port;
    private readonly X509Certificate2 _cert;
    private readonly Func<LodopHttpExchange, LodopHttpResult> _handler;
    private readonly Action<string> _log;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptTask;

    public LodopLoopbackHttpsServer(
        int port,
        X509Certificate2 cert,
        Func<LodopHttpExchange, LodopHttpResult> handler,
        Action<string> log)
    {
        _port = port;
        _cert = cert;
        _handler = handler;
        _log = log;
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    public void Start()
    {
        _listener.Start();
        _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { /* ignore */ }
        try { _acceptTask?.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        _cts.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                var c = client;
                _ = Task.Run(() => HandleClientAsync(c, token), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log($"Lodop-compat [{_port}] https accept error: {ex.Message}");
                client?.Dispose();
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            try
            {
                await using var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
                await ssl.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions
                    {
                        ServerCertificate = _cert,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                    },
                    token).ConfigureAwait(false);

                var exchange = await ReadRequestAsync(ssl, token).ConfigureAwait(false);
                if (exchange is null)
                    return;

                var result = _handler(exchange);
                await WriteResponseAsync(ssl, exchange.Origin, result, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log($"Lodop-compat [{_port}] https request error: {ex.Message}");
            }
        }
    }

    private static async Task<LodopHttpExchange?> ReadRequestAsync(Stream stream, CancellationToken token)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[4096];
        // Read headers (and possibly some body) until \r\n\r\n
        while (ms.Length < 64 * 1024)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
            if (read <= 0)
                return null;
            ms.Write(buffer, 0, read);
            var headerEnd = IndexOfHeaderEnd(ms.GetBuffer(), (int)ms.Length);
            if (headerEnd < 0)
                continue;

            var headerText = Encoding.ASCII.GetString(ms.GetBuffer(), 0, headerEnd);
            var lines = headerText.Split("\r\n");
            if (lines.Length == 0)
                return null;

            var parts = lines[0].Split(' ');
            if (parts.Length < 2)
                return null;

            var method = parts[0];
            var path = parts[1];
            var q = path.IndexOf('?', StringComparison.Ordinal);
            if (q >= 0)
                path = path[..q];

            string? origin = null;
            var contentLength = 0;
            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                var colon = line.IndexOf(':');
                if (colon <= 0)
                    continue;
                var name = line[..colon].Trim();
                var value = line[(colon + 1)..].Trim();
                if (name.Equals("Origin", StringComparison.OrdinalIgnoreCase))
                    origin = value;
                else if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                         && int.TryParse(value, out var len))
                    contentLength = len;
            }

            var bodyStart = headerEnd + 4;
            var body = Array.Empty<byte>();
            if (contentLength > 0)
            {
                if (contentLength > 8 * 1024)
                    return new LodopHttpExchange(method, path, origin, null, BodyTooLarge: true);

                body = new byte[contentLength];
                var already = (int)ms.Length - bodyStart;
                if (already > 0)
                    Buffer.BlockCopy(ms.GetBuffer(), bodyStart, body, 0, Math.Min(already, contentLength));

                var got = Math.Min(already, contentLength);
                while (got < contentLength)
                {
                    var n = await stream.ReadAsync(body.AsMemory(got, contentLength - got), token).ConfigureAwait(false);
                    if (n <= 0)
                        break;
                    got += n;
                }
            }

            var bodyText = body.Length == 0 ? "" : Encoding.UTF8.GetString(body);
            return new LodopHttpExchange(method, path, origin, bodyText, BodyTooLarge: false);
        }

        return null;
    }

    private static int IndexOfHeaderEnd(byte[] data, int length)
    {
        for (var i = 0; i + 3 < length; i++)
        {
            if (data[i] == (byte)'\r' && data[i + 1] == (byte)'\n' && data[i + 2] == (byte)'\r' && data[i + 3] == (byte)'\n')
                return i;
        }

        return -1;
    }

    private static async Task WriteResponseAsync(
        Stream stream,
        string? origin,
        LodopHttpResult result,
        CancellationToken token)
    {
        var acao = string.IsNullOrWhiteSpace(origin) ? "*" : origin;
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(result.StatusCode).Append(' ').Append(Reason(result.StatusCode)).Append("\r\n");
        sb.Append("Content-Type: ").Append(result.ContentType).Append("\r\n");
        sb.Append("Content-Length: ").Append(result.Body.Length).Append("\r\n");
        sb.Append("Access-Control-Allow-Origin: ").Append(acao).Append("\r\n");
        sb.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n");
        sb.Append("Access-Control-Allow-Headers: Content-Type\r\n");
        sb.Append("Access-Control-Allow-Private-Network: true\r\n");
        sb.Append("Vary: Origin\r\n");
        sb.Append("Connection: close\r\n");
        sb.Append("\r\n");

        var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
        await stream.WriteAsync(headerBytes, token).ConfigureAwait(false);
        if (result.Body.Length > 0)
            await stream.WriteAsync(result.Body, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    private static string Reason(int status) => status switch
    {
        200 => "OK",
        204 => "No Content",
        400 => "Bad Request",
        404 => "Not Found",
        413 => "Payload Too Large",
        500 => "Internal Server Error",
        502 => "Bad Gateway",
        503 => "Service Unavailable",
        _ => "OK"
    };
}

internal sealed record LodopHttpExchange(
    string Method,
    string Path,
    string? Origin,
    string? Body,
    bool BodyTooLarge);

internal sealed record LodopHttpResult(int StatusCode, string ContentType, byte[] Body)
{
    public static LodopHttpResult Text(int status, string contentType, string text) =>
        new(status, contentType, Encoding.UTF8.GetBytes(text));

    public static LodopHttpResult Bytes(int status, string contentType, byte[] body) =>
        new(status, contentType, body);

    public static LodopHttpResult Empty(int status) =>
        new(status, "text/plain; charset=utf-8", Array.Empty<byte>());
}
