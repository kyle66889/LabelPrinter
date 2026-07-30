using System.Collections.Concurrent;

namespace LabelPrinter.Services;

/// <summary>
/// FIFO print queue for Lodop-compat: HTTP handlers only enqueue and return quickly so
/// MZL's full-page postback can navigate away without aborting a long PDF fetch/print.
/// A single background worker fetches + prints serially. Pending jobs are mirrored to
/// disk so restart/crash can resume them.
/// </summary>
public sealed class LodopPrintQueue : IDisposable
{
    // Pack lines can burst hundreds of PrintPdf posts while the printer is still
    // catching up; URLs are tiny so a large cap is cheap. Bound only to avoid
    // unbounded growth if something loops forever.
    public const int DefaultMaxQueued = 2000;

    private readonly Func<string> _printerName;
    private readonly Func<string, byte[]> _fetchPdf;
    private readonly Action<byte[], string> _printPdf;
    private readonly Action<string> _log;
    private readonly Action<string, string, string?> _reportFailure;
    private readonly Func<int, bool> _beginJob;
    private readonly Action _endJob;
    private readonly int _maxQueued;
    private readonly LodopQueueStore? _store;

    private readonly ConcurrentQueue<LodopQueuedJob> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;
    private int _pending;

    public int PendingCount => Volatile.Read(ref _pending);

    public LodopPrintQueue(
        Func<string> printerName,
        Func<string, byte[]> fetchPdf,
        Action<byte[], string> printPdf,
        Action<string> log,
        Action<string, string, string?>? reportFailure = null,
        Func<int, bool>? beginJob = null,
        Action? endJob = null,
        int maxQueued = DefaultMaxQueued,
        string? storePath = null)
    {
        _printerName = printerName;
        _fetchPdf = fetchPdf;
        _printPdf = printPdf;
        _log = log;
        _reportFailure = reportFailure ?? ((reason, url, detail) => LodopFailureReport.Record(reason, url, detail));
        _beginJob = beginJob ?? (timeoutMs => Printing.PrintModel.TryBeginJob(timeoutMs));
        _endJob = endJob ?? Printing.PrintModel.EndJob;
        _maxQueued = maxQueued;
        // null storePath = memory-only (unit tests). Production passes DefaultPath.
        _store = storePath is null ? null : new LodopQueueStore(storePath);

        if (_store is not null)
        {
            foreach (var job in _store.Load())
                EnqueueInMemory(job, persist: false);

            if (_pending > 0)
                _log($"Lodop-compat: restored {_pending} persisted print job(s) from disk.");
        }

        _worker = Task.Run(WorkerLoopAsync);
    }

    /// <summary>
    /// Enqueues a PDF URL. Returns false if the queue is full (caller should 503).
    /// <paramref name="queueDepth"/> is the pending count after this attempt (including
    /// rejected attempts that did not add).
    /// </summary>
    public bool TryEnqueue(string pdfUrl, int port, out int queueDepth)
    {
        var next = Interlocked.Increment(ref _pending);
        if (next > _maxQueued)
        {
            queueDepth = Interlocked.Decrement(ref _pending);
            return false;
        }

        var job = new LodopQueuedJob(Guid.NewGuid().ToString("N"), pdfUrl, port);
        _store?.Add(job);
        _queue.Enqueue(job);
        _signal.Release();
        queueDepth = next;
        return true;
    }

    private void EnqueueInMemory(LodopQueuedJob job, bool persist)
    {
        Interlocked.Increment(ref _pending);
        if (persist)
            _store?.Add(job);
        _queue.Enqueue(job);
        _signal.Release();
    }

    private async Task WorkerLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (!_queue.TryDequeue(out var job))
                continue;

            try
            {
                ProcessJob(job);
            }
            finally
            {
                _store?.Remove(job.Id);
                Interlocked.Decrement(ref _pending);
            }
        }
    }

    private void Fail(string reason, string pdfUrl, string? detail = null)
    {
        // Always land in the main run log (FileLog + Settings 运行日志), not only the
        // failure-tab JSON. The failure store is an extra unresolved-work queue.
        _log($"Lodop FAIL [{reason}] '{pdfUrl}'" + (detail is null ? "" : $" — {detail}"));
        _reportFailure(reason, pdfUrl, detail);
    }

    private void ProcessJob(LodopQueuedJob job)
    {
        var pdfUrl = job.PdfUrl;
        var port = job.Port;

        var printer = _printerName();
        if (string.IsNullOrWhiteSpace(printer))
        {
            Fail("no_printer", pdfUrl, $"port={port}");
            return;
        }

        // Download once; on failure record to the failure log (no auto-retry). Operators
        // can re-queue from the Settings failure tab. Fetch before the printer lock so a
        // slow download does not block REST/WS jobs sharing PrintModel.
        byte[] pdfBytes;
        try
        {
            pdfBytes = _fetchPdf(pdfUrl);
            LodopPdfFetch.EnsureLooksLikePdf(pdfBytes);
        }
        catch (Exception ex)
        {
            Fail("fetch_failed", pdfUrl, ex.Message);
            return;
        }

        // Wait up to 60s for a print slot (same idea as WebSocket jobs) rather than
        // dropping — the HTTP response already returned 200/queued.
        if (!_beginJob(60_000))
        {
            Fail("busy_timeout", pdfUrl, $"port={port}; printer slot busy 60s");
            return;
        }

        try
        {
            try
            {
                _printPdf(pdfBytes, printer);
                _log($"Lodop OK [{port}]: printed '{pdfUrl}' to {printer}.");
            }
            catch (Exception ex)
            {
                Fail("print_failed", pdfUrl, ex.Message);
            }
        }
        finally
        {
            _endJob();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _worker.Wait(TimeSpan.FromSeconds(3)); } catch { /* ignore */ }

        // Leave remaining jobs on disk for the next Start(); do not mark them failed.
        var left = _queue.Count;
        if (left > 0 || Volatile.Read(ref _pending) > 0)
            _log($"Lodop-compat: stopped with {Math.Max(left, Volatile.Read(ref _pending))} job(s) still persisted on disk.");

        _cts.Dispose();
        _signal.Dispose();
    }
}
