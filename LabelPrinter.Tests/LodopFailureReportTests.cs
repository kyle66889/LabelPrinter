using LabelPrinter.Services;
using Xunit;

namespace LabelPrinter.Tests;

public class LodopFailureReportTests
{
    [Theory]
    [InlineData("http://fbd.shipswithus.com//temp/a3ee59d1-db38-43f8-ab61-c277a0c1f0ef.pdf", "a3ee59d1-db38-43f8-ab61-c277a0c1f0ef.pdf")]
    [InlineData("https://host/temp/foo.PDF?x=1", "foo.PDF")]
    [InlineData("not-a-url", "not-a-url")]
    public void FileNameFromUrl_extracts_last_path_segment(string url, string expected)
    {
        Assert.Equal(expected, LodopFailureReport.FileNameFromUrl(url));
    }

    [Fact]
    public void Record_appends_one_detail_line_and_filename_index_line()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LabelPrinterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            LodopFailureReport.Record(
                dir,
                reason: "fetch_failed",
                pdfUrl: "http://fbd.example/temp/label-001.pdf",
                detail: "404 Not Found");

            var day = DateTime.Now.ToString("yyyy-MM-dd");
            var detailPath = Path.Combine(dir, $"lodop-failures-{day}.txt");
            var namesPath = Path.Combine(dir, $"lodop-failed-files-{day}.txt");

            Assert.True(File.Exists(detailPath));
            Assert.True(File.Exists(namesPath));

            var detail = File.ReadAllText(detailPath);
            Assert.Contains("fetch_failed", detail);
            Assert.Contains("label-001.pdf", detail);
            Assert.Contains("http://fbd.example/temp/label-001.pdf", detail);
            Assert.Contains("404 Not Found", detail);

            var names = File.ReadAllLines(namesPath);
            Assert.Contains("label-001.pdf", names);

            var failures = LodopFailureStore.For(Path.Combine(dir, "lodop-print-failures.json")).Load();
            Assert.Single(failures);
            Assert.Equal("fetch_failed", failures[0].Reason);
            Assert.Equal("http://fbd.example/temp/label-001.pdf", failures[0].PdfUrl);
            Assert.Equal("404 Not Found", failures[0].Detail);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}
