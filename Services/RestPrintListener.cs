using System.Net;
using System.Text;
using System.Text.Json;
using LabelPrinter.Printing;

namespace LabelPrinter.Services;

/// <summary>
/// Local HTTP endpoint for ONE label size: POST /LabelPrint with a raw label body
/// or JSON { "epl": "..." }. The bound port already selects the target printer, so
/// the request body's printer is implicit.
/// </summary>
public sealed class RestPrintListener : IDisposable
{
    private readonly LabelFormat _format;
    private readonly bool _allowLan;
    private readonly PrintModel _printModel;
    private readonly Action<string> _log;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _task;

    public RestPrintListener(LabelFormat format, bool allowLan, PrintModel printModel, Action<string> log)
    {
        _format = format;
        _allowLan = allowLan;
        _printModel = printModel;
        _log = log;
    }

    public void Start()
    {
        Stop();
        var host = _allowLan ? "+" : "localhost";
        var prefix = $"http://{host}:{_format.Port}/";

        _listener = new HttpListener();
        _listener.Prefixes.Add(prefix);
        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex)
        {
            _log($"REST [{_format.Size}] failed to listen on {prefix}: {ex.Message}. " +
                 "If 'Allow LAN access' is on, run as administrator or add a urlacl " +
                 $"(netsh http add urlacl url={prefix} user=Everyone).");
            _listener = null;
            return;
        }

        _cts = new CancellationTokenSource();
        _task = Task.Run(() => ListenAsync(_cts.Token));
        _log($"REST [{_format.Size}] listening on {prefix}LabelPrint -> {_format.PrinterName}");
    }

    public void Stop()
    {
        _cts?.Cancel();
        if (_listener?.IsListening == true)
            _listener.Stop();
        try
        {
            _task?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // ignore
        }

        _listener?.Close();
        _listener = null;
        _cts?.Dispose();
        _cts = null;
        _task = null;
    }

    private async Task ListenAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener is { IsListening: true })
        {
            try
            {
                var ctx = await _listener.GetContextAsync().WaitAsync(token).ConfigureAwait(false);
                _ = Task.Run(() => HandleRequest(ctx), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log($"REST [{_format.Size}] listener error: {ex.Message}");
            }
        }
    }

    private void HandleRequest(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath?.TrimEnd('/') ?? "";
            if (!ctx.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase)
                || !path.EndsWith("/LabelPrint", StringComparison.OrdinalIgnoreCase))
            {
                WriteResponse(ctx, 404, "Not Found");
                return;
            }

            using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
            var body = reader.ReadToEnd();
            string data = body;

            if (ctx.Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
            {
                var doc = JsonDocument.Parse(body);
                data = doc.RootElement.GetProperty("epl").GetString() ?? "";
            }

            if (string.IsNullOrWhiteSpace(data))
            {
                WriteResponse(ctx, 400, "Label body is required.");
                return;
            }

            _printModel.PrintTo(data, _format.PrinterName);
            _log($"REST [{_format.Size}] job completed.");
            WriteResponse(ctx, 200, "OK");
        }
        catch (Exception ex)
        {
            _log($"REST [{_format.Size}] print failed: {ex.Message}");
            WriteResponse(ctx, 500, ex.Message);
        }
    }

    private static void WriteResponse(HttpListenerContext ctx, int status, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.Close();
    }

    public void Dispose() => Stop();
}
