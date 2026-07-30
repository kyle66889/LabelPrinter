using Xunit;
using LabelPrinter.Services;

namespace LabelPrinter.Tests;

public class LodopFailureAuditPruneTests
{
    [Fact]
    public void PruneOldAuditFiles_deletes_txt_older_than_keepDays_only()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LabelPrinterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var oldDay = DateTime.Today.AddDays(-45).ToString("yyyy-MM-dd");
            var recentDay = DateTime.Today.AddDays(-3).ToString("yyyy-MM-dd");
            var oldFailures = Path.Combine(dir, $"lodop-failures-{oldDay}.txt");
            var oldNames = Path.Combine(dir, $"lodop-failed-files-{oldDay}.txt");
            var recentFailures = Path.Combine(dir, $"lodop-failures-{recentDay}.txt");
            var json = Path.Combine(dir, "lodop-print-failures.json");
            File.WriteAllText(oldFailures, "old");
            File.WriteAllText(oldNames, "old.pdf");
            File.WriteAllText(recentFailures, "recent");
            File.WriteAllText(json, "[]");

            var removed = LodopFailureReport.PruneOldAuditFiles(dir, keepDays: 30);
            Assert.Equal(2, removed);
            Assert.False(File.Exists(oldFailures));
            Assert.False(File.Exists(oldNames));
            Assert.True(File.Exists(recentFailures));
            Assert.True(File.Exists(json)); // pending pool never pruned here
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void IsTimestampOnLocalDay_filters_correctly()
    {
        var today = DateTime.Today;
        var stamp = today.ToString("yyyy-MM-dd") + " 09:36:57";
        Assert.True(LodopFailedJobExtensions.IsOnLocalDay(stamp, today));
        Assert.False(LodopFailedJobExtensions.IsOnLocalDay(stamp, today.AddDays(-1)));
        Assert.False(LodopFailedJobExtensions.IsOnLocalDay("not-a-date", today));
    }
}
