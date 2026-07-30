namespace LabelPrinter.Services;

/// <summary>
/// Append-only failure reports for Lodop-compat jobs that already returned 200 Queued
/// to the browser, so operators can see which labels never printed without digging logs.
/// Writes two files under the given directory (normally AppBase/logs):
///   lodop-failures-yyyy-MM-dd.txt      — full detail lines
///   lodop-failed-files-yyyy-MM-dd.txt  — one PDF file name per line (easy to scan/diff)
/// </summary>
public static class LodopFailureReport
{
    private static readonly object Gate = new();

    public static string FileNameFromUrl(string pdfUrl)
    {
        if (string.IsNullOrWhiteSpace(pdfUrl))
            return "(empty)";

        try
        {
            if (Uri.TryCreate(pdfUrl, UriKind.Absolute, out var uri))
            {
                var name = Path.GetFileName(uri.LocalPath);
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
        }
        catch
        {
            // fall through
        }

        var trimmed = pdfUrl.Trim();
        var slash = trimmed.LastIndexOfAny(['/', '\\']);
        if (slash >= 0 && slash < trimmed.Length - 1)
        {
            var tail = trimmed[(slash + 1)..];
            var q = tail.IndexOfAny(['?', '#']);
            return q >= 0 ? tail[..q] : tail;
        }

        return trimmed;
    }

    public static void Record(string reason, string pdfUrl, string? detail = null) =>
        Record(Path.Combine(AppContext.BaseDirectory, "logs"), reason, pdfUrl, detail);

    public static void Record(string logDir, string reason, string pdfUrl, string? detail = null)
    {
        try
        {
            Directory.CreateDirectory(logDir);
            var day = DateTime.Now.ToString("yyyy-MM-dd");
            var fileName = FileNameFromUrl(pdfUrl);
            var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var detailLine =
                $"[{stamp}] {reason} | {fileName} | {pdfUrl}" +
                (string.IsNullOrWhiteSpace(detail) ? "" : $" | {detail}") +
                Environment.NewLine;

            var detailPath = Path.Combine(logDir, $"lodop-failures-{day}.txt");
            var namesPath = Path.Combine(logDir, $"lodop-failed-files-{day}.txt");

            lock (Gate)
            {
                File.AppendAllText(detailPath, detailLine);
                File.AppendAllText(namesPath, fileName + Environment.NewLine);
            }

            // Structured, removable copy for the settings UI's failure tab — the .txt
            // files above stay append-only as the permanent audit trail.
            LodopFailureStore.For(Path.Combine(logDir, "lodop-print-failures.json"))
                .Add(new LodopFailedJob(Guid.NewGuid().ToString("N"), reason, pdfUrl, detail, stamp));
        }
        catch
        {
            // Reporting must never take down the print worker.
        }
    }

    /// <summary>
    /// Deletes append-only daily audit txt files older than <paramref name="keepDays"/>.
    /// Does NOT touch lodop-print-failures.json (the unresolved work queue) or the print queue.
    /// </summary>
    public static int PruneOldAuditFiles(string logDir, int keepDays = 30)
    {
        var removed = 0;
        try
        {
            if (!Directory.Exists(logDir) || keepDays < 1)
                return 0;

            var cutoff = DateTime.Today.AddDays(-keepDays);
            foreach (var path in Directory.EnumerateFiles(logDir, "lodop-failures-*.txt")
                         .Concat(Directory.EnumerateFiles(logDir, "lodop-failed-files-*.txt")))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                // ...-yyyy-MM-dd at the end of the file name
                if (name.Length < 10)
                    continue;
                var datePart = name[^10..];
                if (!DateTime.TryParse(datePart, out var fileDay))
                    continue;
                if (fileDay.Date >= cutoff)
                    continue;

                try
                {
                    File.Delete(path);
                    removed++;
                }
                catch
                {
                    // skip locked files
                }
            }
        }
        catch
        {
            // best-effort
        }

        return removed;
    }
}
