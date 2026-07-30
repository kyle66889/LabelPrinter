using LabelPrinter.Services;
using Xunit;

namespace LabelPrinter.Tests;

public class LodopFailureStoreTests
{
    [Fact]
    public void Add_Remove_roundtrip_persists_list()
    {
        var path = Path.Combine(Path.GetTempPath(), "LabelPrinterTests", Guid.NewGuid().ToString("N"), "failures.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var store = LodopFailureStore.For(path);
            store.Add(new LodopFailedJob("1", "fetch_failed", "http://a.pdf", "404", "2026-07-30 09:00:00"));
            store.Add(new LodopFailedJob("2", "print_failed", "http://b.pdf", null, "2026-07-30 09:00:05"));

            var loaded = LodopFailureStore.For(path).Load();
            Assert.Equal(2, loaded.Count);

            store.Remove("1");
            loaded = LodopFailureStore.For(path).Load();
            Assert.Single(loaded);
            Assert.Equal("2", loaded[0].Id);
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void For_returns_shared_instance_per_path()
    {
        var path = Path.Combine(Path.GetTempPath(), "LabelPrinterTests", Guid.NewGuid().ToString("N"), "failures.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            Assert.Same(LodopFailureStore.For(path), LodopFailureStore.For(path));
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); } catch { /* ignore */ }
        }
    }
}
