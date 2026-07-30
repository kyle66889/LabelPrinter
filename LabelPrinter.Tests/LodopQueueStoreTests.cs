using LabelPrinter.Services;
using Xunit;

namespace LabelPrinter.Tests;

public class LodopQueueStoreTests
{
    [Fact]
    public void Add_Remove_roundtrip_persists_fifo_order()
    {
        var path = Path.Combine(Path.GetTempPath(), "LabelPrinterTests", Guid.NewGuid().ToString("N"), "queue.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var store = new LodopQueueStore(path);
            store.Add(new LodopQueuedJob("1", "http://a.pdf", 8000));
            store.Add(new LodopQueuedJob("2", "http://b.pdf", 8443));

            var reloaded = new LodopQueueStore(path).Load();
            Assert.Equal(2, reloaded.Count);
            Assert.Equal("http://a.pdf", reloaded[0].PdfUrl);
            Assert.Equal("http://b.pdf", reloaded[1].PdfUrl);

            store.Remove("1");
            reloaded = new LodopQueueStore(path).Load();
            Assert.Single(reloaded);
            Assert.Equal("2", reloaded[0].Id);
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Queue_loads_existing_store_on_start_and_prints()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LabelPrinterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var storePath = Path.Combine(dir, "queue.json");
        var printed = new List<string>();

        try
        {
            new LodopQueueStore(storePath).Add(
                new LodopQueuedJob(Guid.NewGuid().ToString("N"), "http://restored.pdf", 8000));

            using var queue = new LodopPrintQueue(
                printerName: () => "Fake",
                fetchPdf: url => System.Text.Encoding.UTF8.GetBytes("%PDF-1.4\n" + url),
                printPdf: (bytes, _) =>
                {
                    lock (printed)
                        printed.Add(System.Text.Encoding.UTF8.GetString(bytes));
                },
                log: _ => { },
                beginJob: _ => true,
                endJob: () => { },
                storePath: storePath);

            Assert.True(SpinWait.SpinUntil(() =>
            {
                lock (printed) return printed.Contains("%PDF-1.4\nhttp://restored.pdf");
            }, TimeSpan.FromSeconds(5)));

            // Completed job removed from disk
            Assert.Empty(new LodopQueueStore(storePath).Load());
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}
