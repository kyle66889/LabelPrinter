using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using PDFtoImage;
using SkiaSharp;

namespace LabelPrinter.Printing;

/// <summary>
/// Renders PDF pages to bitmaps via PDFium (PDFtoImage), then prints through the
/// printer's GDI driver. Label printers do not understand raw PDF bytes; dumping
/// them with WritePrinter is a silent no-op on most models.
///
/// Windows.Data.Pdf was tried first but renders many MZL waybill PDFs as blank
/// white pages (browser/Acrobat show content). PDFium matches what operators see.
/// </summary>
public static class PdfPagePrinter
{
    private const int RenderDpi = 144;

    public static void Print(string printerName, byte[] pdfBytes)
    {
        if (pdfBytes is null || pdfBytes.Length == 0)
            throw new ArgumentException("PDF data is empty.", nameof(pdfBytes));

        var (images, pageSizePoints) = RenderPages(pdfBytes);
        try
        {
            if (images.Count == 0)
                throw new InvalidOperationException("PDF has no pages.");

            using var doc = new PrintDocument();
            doc.PrinterSettings.PrinterName = printerName;
            if (!doc.PrinterSettings.IsValid)
                throw new InvalidOperationException($"Printer '{printerName}' is not available.");
            doc.DocumentName = "Label Printer Service";

            // Without this, PageBounds comes from whatever page size the driver currently
            // has configured (e.g. a leftover default), not the PDF's actual size — the
            // image then gets scaled/centered against the WRONG page and part of the label
            // ends up beyond where the physical stock is cut. PaperSize is in hundredths
            // of an inch; PDF points are 1/72 inch.
            doc.DefaultPageSettings.PaperSize = new PaperSize(
                "Label",
                (int)Math.Round(pageSizePoints.Width / 72 * 100),
                (int)Math.Round(pageSizePoints.Height / 72 * 100));
            doc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);

            var pageIndex = 0;
            doc.PrintPage += (_, e) =>
            {
                var img = images[pageIndex];

                // Scale against PrintableArea (the driver-reported region that accounts for
                // the printer's actual hardware margins) so the image isn't oversized versus
                // what the printer can actually render — but anchor at (0,0), not centered
                // inside it. C-Lodop's own ADD_PRINT_PDF(0, 0, "100%", "100%", ...) call is
                // top/left-anchored at the page origin with no margin; centering here added
                // a visible gap at the top and left that C-Lodop's output doesn't have.
                //
                // NOTE: this has been observed to print ~5 degrees rotated on at least one
                // printer/paper-size combination. Root cause not yet found — kept as-is
                // per explicit instruction to prioritize matching C-Lodop's fit-to-page
                // look over avoiding the rotation for now.
                var area = ResolveDrawArea(e);
                var scale = Math.Min(area.Width / img.Width, area.Height / img.Height);
                if (scale <= 0 || float.IsNaN(scale) || float.IsInfinity(scale))
                    throw new InvalidOperationException(
                        $"Printer draw area is unusable ({area.Width}x{area.Height}).");

                var w = img.Width * scale;
                var h = img.Height * scale;
                e.Graphics!.DrawImage(img, 0, 0, w, h);
                pageIndex++;
                e.HasMorePages = pageIndex < images.Count;
            };

            doc.Print();
        }
        finally
        {
            foreach (var img in images)
                img.Dispose();
        }
    }

    /// <summary>
    /// Renders each PDF page to a GDI bitmap. Used by Print and by unit tests that
    /// guard against the Windows.Data.Pdf "blank waybill" regression.
    /// </summary>
    public static (List<Image> images, SizeF firstPageSizePoints) RenderPages(byte[] pdfBytes)
    {
        var pageCount = Conversion.GetPageCount(pdfBytes);
        if (pageCount <= 0)
            throw new InvalidOperationException("PDF has no pages.");

        var firstSize = Conversion.GetPageSize(pdfBytes, 0);
        var list = new List<Image>(pageCount);
        var options = new RenderOptions(Dpi: RenderDpi);

        for (var i = 0; i < pageCount; i++)
        {
            using var sk = Conversion.ToImage(pdfBytes, i, null, options);
            var bmp = SkiaToBitmap(sk);
            if (IsMostlyBlank(bmp))
            {
                bmp.Dispose();
                foreach (var img in list)
                    img.Dispose();
                throw new InvalidOperationException(
                    $"PDF page {i + 1} rendered blank (PDFium produced no visible content).");
            }

            list.Add(bmp);
        }

        return (list, firstSize);
    }

    /// <summary>
    /// True when sampled pixels are nearly all white — the failure mode Windows.Data.Pdf
    /// hit on MZL waybills that still open fine in a browser.
    /// </summary>
    public static bool IsMostlyBlank(Image image, double darkRatioThreshold = 0.005)
    {
        Bitmap bmp;
        var owns = false;
        if (image is Bitmap existing)
        {
            bmp = existing;
        }
        else
        {
            bmp = new Bitmap(image);
            owns = true;
        }

        try
        {
            long dark = 0, total = 0;
            const int step = 4;
            for (var y = 0; y < bmp.Height; y += step)
            {
                for (var x = 0; x < bmp.Width; x += step)
                {
                    var c = bmp.GetPixel(x, y);
                    total++;
                    if (c.R < 250 || c.G < 250 || c.B < 250)
                        dark++;
                }
            }

            return total == 0 || dark / (double)total < darkRatioThreshold;
        }
        finally
        {
            if (owns)
                bmp.Dispose();
        }
    }

    private static RectangleF ResolveDrawArea(PrintPageEventArgs e)
    {
        var printable = e.PageSettings!.PrintableArea;
        if (printable.Width >= 1 && printable.Height >= 1)
            return printable;

        var margins = e.MarginBounds;
        if (margins.Width >= 1 && margins.Height >= 1)
            return margins;

        return e.PageBounds;
    }

    private static Bitmap SkiaToBitmap(SKBitmap sk)
    {
        using var encoded = sk.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("Failed to encode rendered PDF page.");
        // Decode into an independent GDI bitmap. Image.FromStream keeps a live reference to
        // the source stream; cloning after the MemoryStream is disposed yields "Parameter is
        // not valid" on later Width/Height/GetPixel calls.
        var pngBytes = encoded.ToArray();
        using var ms = new MemoryStream(pngBytes);
        using var loaded = new Bitmap(ms);
        return new Bitmap(loaded);
    }
}
