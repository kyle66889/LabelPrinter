using LabelPrinter.Services;
using Xunit;

namespace LabelPrinter.Tests;

public class LodopPdfFetchTests
{
    [Fact]
    public void LooksLikePdf_requires_percent_PDF_header()
    {
        Assert.False(LodopPdfFetch.LooksLikePdf(Array.Empty<byte>()));
        Assert.False(LodopPdfFetch.LooksLikePdf("<html>"u8.ToArray()));
        Assert.True(LodopPdfFetch.LooksLikePdf("%PDF-1.4\n..."u8.ToArray()));
    }
}

public class LodopPrintQueueFetchFailureTests
{
    [Fact]
    public void ProcessJob_fetch_failure_is_reported_and_not_retried()
    {
        var attempts = 0;
        var failures = new List<(string Reason, string Url)>();
        var printed = false;

        using var queue = new LodopPrintQueue(
            printerName: () => "FakePrinter",
            fetchPdf: _ =>
            {
                Interlocked.Increment(ref attempts);
                throw new HttpRequestException("404");
            },
            printPdf: (_, _) => printed = true,
            log: _ => { },
            reportFailure: (reason, url, _) =>
            {
                lock (failures)
                    failures.Add((reason, url));
            },
            beginJob: _ => true,
            endJob: () => { });

        Assert.True(queue.TryEnqueue("http://missing.pdf", 8000, out _));
        Assert.True(SpinWait.SpinUntil(() =>
        {
            lock (failures) return failures.Count >= 1;
        }, TimeSpan.FromSeconds(5)));

        Assert.Equal(1, Volatile.Read(ref attempts));
        Assert.False(printed);
        lock (failures)
        {
            Assert.Single(failures);
            Assert.Equal("fetch_failed", failures[0].Reason);
        }
    }

    [Fact]
    public void ProcessJob_non_pdf_body_is_reported_as_fetch_failed()
    {
        var failures = new List<string>();

        using var queue = new LodopPrintQueue(
            printerName: () => "FakePrinter",
            fetchPdf: _ => "<html>error</html>"u8.ToArray(),
            printPdf: (_, _) => { },
            log: _ => { },
            reportFailure: (reason, _, _) =>
            {
                lock (failures)
                    failures.Add(reason);
            },
            beginJob: _ => true,
            endJob: () => { });

        Assert.True(queue.TryEnqueue("http://a.pdf", 8000, out _));
        Assert.True(SpinWait.SpinUntil(() =>
        {
            lock (failures) return failures.Count >= 1;
        }, TimeSpan.FromSeconds(5)));

        lock (failures)
            Assert.Equal("fetch_failed", Assert.Single(failures));
    }
}
