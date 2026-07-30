using LabelPrinter.Services;
using Xunit;

namespace LabelPrinter.Tests;

public class LodopPrintQueueTests
{
    [Fact]
    public void TryEnqueue_accepts_job_and_worker_prints_fifo()
    {
        var printed = new List<string>();
        using var queue = new LodopPrintQueue(
            printerName: () => "FakePrinter",
            fetchPdf: url => System.Text.Encoding.UTF8.GetBytes("%PDF-1.4\n" + url),
            printPdf: (bytes, _) =>
            {
                lock (printed)
                    printed.Add(System.Text.Encoding.UTF8.GetString(bytes));
            },
            log: _ => { },
            beginJob: _ => true,
            endJob: () => { });

        Assert.True(queue.TryEnqueue("http://a.pdf", port: 8000, out var depth1));
        Assert.Equal(1, depth1);
        Assert.True(queue.TryEnqueue("http://b.pdf", port: 8000, out var depth2));
        Assert.Equal(2, depth2);

        Assert.True(SpinWait.SpinUntil(() =>
        {
            lock (printed) return printed.Count >= 2;
        }, TimeSpan.FromSeconds(5)));

        lock (printed)
            Assert.Equal(new[] { "%PDF-1.4\nhttp://a.pdf", "%PDF-1.4\nhttp://b.pdf" }, printed);
    }

    [Fact]
    public void TryEnqueue_rejects_when_queue_is_full()
    {
        using var block = new ManualResetEventSlim(false);
        using var queue = new LodopPrintQueue(
            printerName: () => "FakePrinter",
            fetchPdf: url => System.Text.Encoding.UTF8.GetBytes("%PDF-1.4\n" + url),
            printPdf: (_, _) => block.Wait(TimeSpan.FromSeconds(10)),
            log: _ => { },
            beginJob: _ => true,
            endJob: () => { },
            maxQueued: 2);

        Assert.True(queue.TryEnqueue("http://1.pdf", 8000, out _));
        Assert.True(queue.TryEnqueue("http://2.pdf", 8000, out _));
        // Give worker a moment to pick up job 1 (still counts as queued/in-flight).
        Thread.Sleep(50);
        Assert.False(queue.TryEnqueue("http://3.pdf", 8000, out var depth));
        Assert.Equal(2, depth);

        block.Set();
        Assert.True(SpinWait.SpinUntil(() => queue.PendingCount == 0, TimeSpan.FromSeconds(5)));
    }
}

public class LodopCompatListenerQueueJsTests
{
    [Fact]
    public void BuildClodopFuncsJs_uses_keepalive_and_retries_503()
    {
        var js = LodopCompatListener.BuildClodopFuncsJs(8443, https: true);

        Assert.Contains("keepalive: true", js);
        Assert.Contains("503", js);
        Assert.Contains("setTimeout", js);
    }
}
