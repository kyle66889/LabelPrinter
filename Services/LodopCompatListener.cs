using System.Net;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using LabelPrinter.Printing;

namespace LabelPrinter.Services;

/// <summary>
/// Stands in for a real C-Lodop install so an existing caller (MZL's `lodop_print.js`,
/// which loads `http://localhost:8000/CLodopfuncs.js` — with `18000` as fallback — or
/// on https pages `https://localhost.lodop.net:8443` / `8444`) can print PDFs through
/// LabelPrinter with zero changes on the caller's side.
/// </summary>
public sealed class LodopCompatListener : IDisposable
{
    private const int PrimaryPort = 8000;
    private const int FallbackPort = 18000;
    private const int HttpsPrimaryPort = 8443;
    private const int HttpsFallbackPort = 8444;

    private const long MaxPdfBytes = 10L * 1024 * 1024;
    private const long MaxRequestBodyBytes = 8 * 1024;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly LodopCompatConfig _config;
    private readonly PrintModel _printModel;
    private readonly Action<string> _log;
    private readonly List<Port> _ports = new();
    private readonly List<LodopLoopbackHttpsServer> _httpsServers = new();
    private readonly List<int> _httpsPorts = new();
    private X509Certificate2? _httpsCert;
    private LodopPrintQueue? _printQueue;

    /// <summary>HTTP + HTTPS ports this instance managed to bind.</summary>
    public IReadOnlyList<int> BoundPorts =>
        _ports.Select(p => p.Number).Concat(_httpsPorts).ToList();

    public LodopCompatListener(LodopCompatConfig config, PrintModel printModel, Action<string> log)
    {
        _config = config;
        _printModel = printModel;
        _log = log;
    }

    public void Start()
    {
        Stop();
        _printQueue = new LodopPrintQueue(
            printerName: () => _config.PrinterName,
            fetchPdf: FetchPdfBytes,
            printPdf: (bytes, printer) =>
                _printModel.PrintTo(Convert.ToBase64String(bytes), printer, LabelPrintType.Pdf),
            log: _log,
            storePath: LodopQueueStore.DefaultPath);

        StartOne(PrimaryPort);
        StartOne(FallbackPort);
        StartHttps(HttpsPrimaryPort);
        StartHttps(HttpsFallbackPort);
        if (_httpsCert is not null)
            LodopCompatCertificate.EnsureTrustedRootAsync(_httpsCert, _log);

        var pruned = LodopFailureReport.PruneOldAuditFiles(
            Path.Combine(AppContext.BaseDirectory, "logs"), keepDays: 30);
        if (pruned > 0)
            _log($"Lodop-compat: pruned {pruned} failure audit file(s) older than 30 days.");

        if (_ports.Count == 0 && _httpsPorts.Count == 0)
            _log("Lodop-compat: failed to bind HTTP (8000/18000) and HTTPS (8443/8444) — feature is unavailable.");
    }

    private const int MaxBindAttempts = 4;
    private static readonly TimeSpan BindRetryDelay = TimeSpan.FromMilliseconds(300);

    private void StartOne(int port)
    {
        HttpListener? listener = null;
        for (var attempt = 1; attempt <= MaxBindAttempts; attempt++)
        {
            listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            try
            {
                listener.Start();
                break;
            }
            catch (HttpListenerException ex) when (attempt < MaxBindAttempts)
            {
                _log($"Lodop-compat: {port} busy on attempt {attempt}/{MaxBindAttempts} ({ex.Message}), retrying...");
                listener = null;
                Thread.Sleep(BindRetryDelay);
            }
            catch (HttpListenerException ex)
            {
                _log($"Lodop-compat: failed to listen on {port}: {ex.Message}");
                return;
            }
        }

        if (listener is null)
            return;

        var cts = new CancellationTokenSource();
        var task = Task.Run(() => ListenAsync(listener, port, cts.Token));
        _ports.Add(new Port(port, listener, cts, task));
        _log($"Lodop-compat: listening on http://localhost:{port} -> {_config.PrinterName}");
    }

    private void StartHttps(int port)
    {
        try
        {
            _httpsCert ??= LodopCompatCertificate.GetOrCreate(_log);
            var server = new LodopLoopbackHttpsServer(
                port,
                _httpsCert,
                exchange => Dispatch(exchange, port, https: true),
                _log);
            server.Start();
            _httpsServers.Add(server);
            _httpsPorts.Add(port);
            _log($"Lodop-compat: listening on https://{LodopCompatCertificate.HostName}:{port} -> {_config.PrinterName}");
        }
        catch (Exception ex)
        {
            _log($"Lodop-compat: failed to listen on https {port}: {ex.Message}");
        }
    }

    public void Stop()
    {
        foreach (var p in _ports)
        {
            p.Cts.Cancel();
            if (p.Listener.IsListening)
                p.Listener.Stop();
            try { p.Task.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
            p.Listener.Close();
            p.Cts.Dispose();
        }

        _ports.Clear();

        foreach (var s in _httpsServers)
            s.Dispose();
        _httpsServers.Clear();
        _httpsPorts.Clear();
        // Do not Dispose _httpsCert — it lives in CurrentUser\My; disposing breaks the key handle.
        _httpsCert = null;

        _printQueue?.Dispose();
        _printQueue = null;
    }

    private async Task ListenAsync(HttpListener listener, int port, CancellationToken token)
    {
        while (!token.IsCancellationRequested && listener.IsListening)
        {
            try
            {
                var ctx = await listener.GetContextAsync().WaitAsync(token).ConfigureAwait(false);
                _ = Task.Run(() => HandleHttpListenerRequest(ctx, port), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log($"Lodop-compat [{port}] listener error: {ex.Message}");
            }
        }
    }

    private void HandleHttpListenerRequest(HttpListenerContext ctx, int port)
    {
        try
        {
            string? body = null;
            var bodyTooLarge = false;
            if (ctx.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadRequestBody(ctx, MaxRequestBodyBytes, out body))
                    bodyTooLarge = true;
            }

            var exchange = new LodopHttpExchange(
                ctx.Request.HttpMethod,
                ctx.Request.Url?.AbsolutePath ?? "/",
                ctx.Request.Headers["Origin"],
                body,
                bodyTooLarge);

            var result = Dispatch(exchange, port, https: false);
            WriteCors(ctx.Request, ctx.Response);
            ctx.Response.StatusCode = result.StatusCode;
            ctx.Response.ContentType = result.ContentType;
            ctx.Response.ContentLength64 = result.Body.Length;
            if (result.Body.Length > 0)
                ctx.Response.OutputStream.Write(result.Body, 0, result.Body.Length);
            ctx.Response.Close();
        }
        catch (Exception ex)
        {
            _log($"Lodop-compat [{port}] request failed: {ex.Message}");
            try
            {
                var err = Encoding.UTF8.GetBytes(ex.Message);
                WriteCors(ctx.Request, ctx.Response);
                ctx.Response.StatusCode = 500;
                ctx.Response.ContentType = "text/plain; charset=utf-8";
                ctx.Response.ContentLength64 = err.Length;
                ctx.Response.OutputStream.Write(err, 0, err.Length);
                ctx.Response.Close();
            }
            catch
            {
                // response already closed
            }
        }
    }

    private LodopHttpResult Dispatch(LodopHttpExchange exchange, int port, bool https)
    {
        try
        {
            if (exchange.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                return LodopHttpResult.Empty(204);

            if (exchange.BodyTooLarge)
                return LodopHttpResult.Text(413, "text/plain; charset=utf-8", "Request body too large.");

            var path = exchange.Path;

            if (exchange.Method.Equals("GET", StringComparison.OrdinalIgnoreCase)
                && (path == "/" || path.Equals("/CLodopfuncs.js", StringComparison.OrdinalIgnoreCase)))
            {
                return LodopHttpResult.Text(
                    200,
                    "application/javascript; charset=utf-8",
                    BuildClodopFuncsJs(port, https));
            }

            if (exchange.Method.Equals("GET", StringComparison.OrdinalIgnoreCase)
                && path.Equals("/_test_sample.pdf", StringComparison.OrdinalIgnoreCase))
            {
                return LodopHttpResult.Bytes(200, "application/pdf", GetSamplePdfBytes());
            }

            if (exchange.Method.Equals("POST", StringComparison.OrdinalIgnoreCase)
                && path.Equals("/lodop_print", StringComparison.OrdinalIgnoreCase))
            {
                return HandlePrint(exchange.Body ?? "", port);
            }

            return LodopHttpResult.Text(404, "text/plain; charset=utf-8", "Not Found");
        }
        catch (Exception ex)
        {
            _log($"Lodop-compat [{port}] request failed: {ex.Message}");
            return LodopHttpResult.Text(500, "text/plain; charset=utf-8", ex.Message);
        }
    }

    private LodopHttpResult HandlePrint(string body, int port)
    {
        if (string.IsNullOrWhiteSpace(_config.PrinterName))
        {
            _log($"Lodop-compat [{port}]: no printer configured.");
            return LodopHttpResult.Text(500, "text/plain; charset=utf-8", "No printer configured for Lodop compatibility.");
        }

        string? pdfUrl;
        try
        {
            using var doc = JsonDocument.Parse(body);
            pdfUrl = doc.RootElement.GetProperty("pdfUrl").GetString();
        }
        catch (Exception ex)
        {
            return LodopHttpResult.Text(400, "text/plain; charset=utf-8", $"Invalid request body: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(pdfUrl))
            return LodopHttpResult.Text(400, "text/plain; charset=utf-8", "pdfUrl is required.");

        var queue = _printQueue;
        if (queue is null)
            return LodopHttpResult.Text(503, "text/plain; charset=utf-8", "Print queue not started.");

        // Enqueue and return immediately so MZL's postback navigation does not abort a
        // long-running PDF fetch/print still tied to this HTTP request.
        if (!queue.TryEnqueue(pdfUrl, port, out var depth))
        {
            _log($"Lodop FAIL [queue_full] '{pdfUrl}' — port={port}; max={LodopPrintQueue.DefaultMaxQueued}");
            LodopFailureReport.Record("queue_full", pdfUrl, $"port={port}; max={LodopPrintQueue.DefaultMaxQueued}");
            return LodopHttpResult.Text(503, "text/plain; charset=utf-8", "Print queue full, retry shortly.");
        }

        _log($"Lodop queued [{port}]: '{pdfUrl}' (depth {depth}).");
        return LodopHttpResult.Text(200, "text/plain; charset=utf-8", "Queued");
    }

    private static byte[] FetchPdfBytes(string pdfUrl)
    {
        using var response = Http.GetAsync(pdfUrl, HttpCompletionOption.ResponseHeadersRead)
            .GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        using var stream = response.Content.ReadAsStream();
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        long total = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > MaxPdfBytes)
                throw new InvalidOperationException($"PDF exceeds {MaxPdfBytes / (1024 * 1024)} MB limit.");
            ms.Write(buffer, 0, read);
        }

        var bytes = ms.ToArray();
        LodopPdfFetch.EnsureLooksLikePdf(bytes);
        return bytes;
    }

    private static bool TryReadRequestBody(HttpListenerContext ctx, long maxBytes, out string body)
    {
        body = "";
        if (ctx.Request.ContentLength64 > maxBytes)
            return false;

        using var ms = new MemoryStream();
        var buffer = new byte[4096];
        long total = 0;
        int read;
        while ((read = ctx.Request.InputStream.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > maxBytes)
                return false;
            ms.Write(buffer, 0, read);
        }

        body = (ctx.Request.ContentEncoding ?? Encoding.UTF8).GetString(ms.ToArray());
        return true;
    }

    private static byte[] GetSamplePdfBytes()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LabelPrinter.sample-label.pdf")
            ?? throw new InvalidOperationException("Embedded sample-label.pdf resource is missing.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static void WriteCors(HttpListenerRequest request, HttpListenerResponse response)
    {
        var origin = request.Headers["Origin"];
        response.Headers.Set(
            "Access-Control-Allow-Origin",
            string.IsNullOrWhiteSpace(origin) ? "*" : origin);
        response.Headers.Set("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.Headers.Set("Access-Control-Allow-Headers", "Content-Type");
        response.Headers.Set("Access-Control-Allow-Private-Network", "true");
        response.Headers.Set("Vary", "Origin");
    }

    public static bool LooksLikeOurClodopFuncsJs(string js) =>
        js.Contains("lodop_print", StringComparison.Ordinal)
        && js.Contains("6.6.4.2", StringComparison.Ordinal);

    /// <summary>
    /// Minimal C-Lodop-compatible JS. On https pages MZL loads this from
    /// localhost.lodop.net:8443/8444, so PRINT must POST back to that same https origin
    /// (not http://localhost:8000 — mixed content would block it).
    /// </summary>
    public static string BuildClodopFuncsJs(int port, bool https = false)
    {
        var baseUrl = https
            ? $"https://{LodopCompatCertificate.HostName}:{port}"
            : $"http://localhost:{port}";

        return $$"""
            function getCLodop(){ return CLODOP; }
            var CLODOP = {
              VERSION: "6.6.4.2",
              CVERSION: "6.6.4.2",
              SET_LICENSES: function(){},
              ADD_PRINT_PDF: function(top,left,width,height,pdfUrl){ this._pdfUrl = pdfUrl; },
              SET_PRINTER_INDEX: function(index){ /* ignored: LabelPrinter's Lodop-compat row targets a single fixed printer */ },
              PRINT: function(){
                var self = this;
                var body = JSON.stringify({ pdfUrl: self._pdfUrl });
                var attempt = 0;
                function send(){
                  attempt++;
                  fetch('{{baseUrl}}/lodop_print', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: body,
                    keepalive: true
                  }).then(function(r){
                    if (r.status === 503 && attempt < 4) {
                      setTimeout(send, 250 * attempt);
                      return;
                    }
                    if (!r.ok) console.error('LabelPrinter lodop_print failed:', r.status);
                  }).catch(function(err){
                    if (attempt < 4) {
                      setTimeout(send, 250 * attempt);
                      return;
                    }
                    console.error('LabelPrinter lodop_print failed:', err);
                  });
                }
                send();
              }
            };
            """;
    }

    public void Dispose() => Stop();

    private sealed record Port(int Number, HttpListener Listener, CancellationTokenSource Cts, Task Task);
}
