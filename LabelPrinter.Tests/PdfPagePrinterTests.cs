using System.Drawing;
using LabelPrinter.Printing;
using Xunit;

namespace LabelPrinter.Tests;

public class PdfPagePrinterTests
{
    [Fact]
    public void RenderPages_mzl_waybill_is_not_blank()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "mzl-waybill-blank-bug.pdf");
        Assert.True(File.Exists(path), $"missing fixture: {path}");
        var pdfBytes = File.ReadAllBytes(path);

        var (images, pageSize) = PdfPagePrinter.RenderPages(pdfBytes);
        try
        {
            Assert.Single(images);
            Assert.InRange(pageSize.Width, 287f, 289f);
            Assert.InRange(pageSize.Height, 431f, 433f);
            Assert.False(PdfPagePrinter.IsMostlyBlank(images[0]));
        }
        finally
        {
            foreach (var img in images)
                img.Dispose();
        }
    }
}
