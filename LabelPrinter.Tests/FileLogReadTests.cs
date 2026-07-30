using Xunit;

namespace LabelPrinter.Tests;

public class FileLogReadTests
{
    [Fact]
    public void TryReadToday_returns_null_when_missing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lp-flog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(FileLog.TryReadToday(dir));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void TryReadToday_returns_tail_when_over_maxChars()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lp-flog-" + Guid.NewGuid().ToString("N"));
        var logs = Path.Combine(dir, "logs");
        Directory.CreateDirectory(logs);
        try
        {
            var path = FileLog.TodayLogPath(dir);
            var body = string.Join(
                Environment.NewLine,
                Enumerable.Range(1, 50).Select(i => $"line-{i}-xxxxxxxxxxxxxxxx"));
            File.WriteAllText(path, body + Environment.NewLine);

            var text = FileLog.TryReadToday(dir, maxChars: 80);
            Assert.NotNull(text);
            Assert.True(text!.Length <= 80 + 40); // cut may keep up to one extra line
            Assert.DoesNotContain("line-1-", text);
            Assert.Contains("line-50-", text);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }
}
