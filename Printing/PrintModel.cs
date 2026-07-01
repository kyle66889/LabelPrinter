namespace LabelPrinter.Printing;

public sealed class PrintModel
{
    /// <summary>
    /// Prints one or more label command blocks to a specific target. The target is
    /// either a Windows printer name or a parallel port ("LPT1".."LPT3"). The server
    /// may send multiple labels separated by blank lines; each is sent as one job.
    /// </summary>
    public void PrintTo(string data, string printerName)
    {
        if (string.IsNullOrWhiteSpace(data))
            throw new ArgumentException("Print data is empty.", nameof(data));
        if (string.IsNullOrWhiteSpace(printerName))
            throw new InvalidOperationException("No printer is configured for this label size.");

        var isLpt = printerName.TrimStart().StartsWith("LPT", StringComparison.OrdinalIgnoreCase);

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
