namespace LabelPrinter;

/// <summary>
/// Best-effort append-only logger to logs/labelprinter-yyyy-MM-dd.log — one file per
/// calendar day, so a long-running tray process doesn't grow a single ever-larger log
/// file. Never throws — logging must not be able to take the process down, so all I/O
/// errors are swallowed. Used both by the tray's live log and by the process-level crash
/// handlers in Program. Old day-files are left in place (not auto-deleted); prune
/// manually if disk space becomes a concern.
/// </summary>
public static class FileLog
{
    private static readonly object Gate = new();

    public static string TodayLogPath(string? baseDir = null) =>
        Path.Combine(baseDir ?? AppContext.BaseDirectory, "logs", $"labelprinter-{DateTime.Now:yyyy-MM-dd}.log");

    public static void Write(string message)
    {
        try
        {
            var path = TodayLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            lock (Gate)
            {
                File.AppendAllText(
                    path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging is best-effort; never let a logging failure escape.
        }
    }

    /// <summary>
    /// Reads today's on-disk log for the Settings "运行日志" seed. Caps at
    /// <paramref name="maxChars"/> (tail) so a huge day file doesn't freeze the UI.
    /// Returns null if missing/unreadable. Uses ReadWrite share so live writers keep going.
    /// </summary>
    public static string? TryReadToday(string? baseDir = null, int maxChars = 512_000)
    {
        try
        {
            var path = TodayLogPath(baseDir);
            if (!File.Exists(path))
                return null;

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            var text = reader.ReadToEnd();
            if (text.Length == 0)
                return null;
            if (text.Length <= maxChars)
                return text;

            // Prefer cutting on a line boundary so the first visible line isn't half a message.
            var start = text.Length - maxChars;
            var nl = text.IndexOf('\n', start);
            return nl >= 0 && nl < text.Length - 1 ? text[(nl + 1)..] : text[start..];
        }
        catch
        {
            return null;
        }
    }
}
