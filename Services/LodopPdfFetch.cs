namespace LabelPrinter.Services;

/// <summary>
/// Shared PDF download checks for Lodop-compat jobs. MZL sometimes returns HTML / empty
/// bodies while the temp file is still being written — those must not be printed as blank labels.
/// </summary>
public static class LodopPdfFetch
{
    public static bool LooksLikePdf(byte[]? bytes)
    {
        if (bytes is null || bytes.Length < 5)
            return false;
        // PDF files start with "%PDF-" (ISO 32000). Allow a small BOM/leading whitespace margin.
        var i = 0;
        while (i < bytes.Length && i < 16 && (bytes[i] == (byte)' ' || bytes[i] == (byte)'\t' || bytes[i] == (byte)'\r' || bytes[i] == (byte)'\n'))
            i++;
        return i + 4 < bytes.Length
               && bytes[i] == (byte)'%'
               && bytes[i + 1] == (byte)'P'
               && bytes[i + 2] == (byte)'D'
               && bytes[i + 3] == (byte)'F'
               && bytes[i + 4] == (byte)'-';
    }

    public static void EnsureLooksLikePdf(byte[] bytes)
    {
        if (!LooksLikePdf(bytes))
            throw new InvalidOperationException(
                $"Downloaded content is not a PDF ({bytes?.Length ?? 0} bytes).");
    }
}
