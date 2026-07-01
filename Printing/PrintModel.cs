namespace LabelPrinter.Printing;

public sealed class PrintModel
{
    /// <summary>
    /// Prints label data to a specific target. The target is either a Windows printer
    /// name or a parallel port ("LPT1".."LPT3").
    ///
    /// EPL/ZPL are sent RAW (bytes forwarded verbatim; only a real label printer can
    /// interpret them), and multiple labels separated by blank lines become separate
    /// jobs. Text is sent with the Windows "TEXT" data type as a single job, so it is
    /// rendered into pages by the printer driver — this lets it print on ordinary
    /// GDI printers (e.g. Microsoft Print to PDF), not just label printers.
    /// </summary>
    public void PrintTo(string data, string printerName, LabelPrintType printType)
    {
        if (string.IsNullOrWhiteSpace(data))
            throw new ArgumentException("Print data is empty.", nameof(data));
        if (string.IsNullOrWhiteSpace(printerName))
            throw new InvalidOperationException("No printer is configured for this label size.");

        var isLpt = printerName.TrimStart().StartsWith("LPT", StringComparison.OrdinalIgnoreCase);

        if (printType == LabelPrintType.Text)
        {
            // Render as text: one job, CRLF line endings, driver-rendered on GDI printers.
            var text = data.Replace("\r\n", "\n").Replace("\n", "\r\n");
            if (isLpt)
                LptPrinter.Print(printerName, text);
            else
                RawPrinterHelper.SendStringToPrinter(printerName, text, "TEXT");
            return;
        }

        foreach (var block in SplitJobs(data))
        {
            if (isLpt)
                LptPrinter.Print(printerName, block);
            else
                RawPrinterHelper.SendStringToPrinter(printerName, block);
        }
    }

    private static IEnumerable<string> SplitJobs(string data)
    {
        var normalized = data.Replace("\r\n", "\n").Trim();
        if (string.IsNullOrEmpty(normalized))
            yield break;

        var parts = normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 1)
        {
            foreach (var part in parts)
                yield return part.TrimEnd() + "\n";
            yield break;
        }

        yield return normalized.EndsWith('\n') ? normalized : normalized + "\n";
    }
}
