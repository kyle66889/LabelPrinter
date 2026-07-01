using System.Globalization;

namespace LabelPrinter.Printing;

/// <summary>
/// Builds a small sample label for the "test" buttons. Sizes are "WxH" in inches;
/// dot counts assume 203 dpi (8 dots/mm) printers.
/// </summary>
public static class SampleLabelGenerator
{
    private const int DotsPerInch = 203;

    public static string Generate(LabelPrintType type, string size)
    {
        var (widthDots, heightDots) = ToDots(size);

        return type switch
        {
            LabelPrintType.Zpl =>
                $"^XA\n^PW{widthDots}\n^LL{heightDots}\n^FO50,50^A0N,40,40^FDTEST {size}^FS\n^XZ\n",
            LabelPrintType.Text =>
                $"TEST {size}\nControlCode Label Printer\n\f",
            _ => // Epl
                $"N\nq{widthDots}\nQ{heightDots},24\nA50,50,0,4,1,1,N,\"TEST {size}\"\nP1\n"
        };
    }

    private static (int width, int height) ToDots(string size)
    {
        var parts = size.ToLowerInvariant().Split('x');
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var w)
            || !double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var h))
        {
            // Fall back to 4x6 if the size string is malformed.
            w = 4;
            h = 6;
        }

        return ((int)Math.Round(w * DotsPerInch), (int)Math.Round(h * DotsPerInch));
    }
}
