using System.Collections.Concurrent;
using System.Text.Json;

namespace LabelPrinter.Services;

public sealed record LodopFailedJob(string Id, string Reason, string PdfUrl, string? Detail, string Timestamp);

public static class LodopFailedJobExtensions
{
    /// <summary>True when <paramref name="timestamp"/> (yyyy-MM-dd HH:mm:ss) falls on <paramref name="day"/> local calendar date.</summary>
    public static bool IsOnLocalDay(string timestamp, DateTime day)
    {
        if (DateTime.TryParse(timestamp, out var dt))
            return dt.Date == day.Date;
        return false;
    }
}

/// <summary>
/// Durable, removable list of unresolved Lodop-compat print failures under
/// logs/lodop-print-failures.json, so the settings UI can show "N failed" and let an
/// operator manually retry or dismiss each one. Complements LodopFailureReport's
/// append-only .txt audit trail, which is never edited or removed.
/// </summary>
public sealed class LodopFailureStore
{
    // Keyed by full path so every caller targeting the same file shares one lock —
    // Record() is called from background print-worker threads and must not race.
    private static readonly ConcurrentDictionary<string, LodopFailureStore> Instances = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly object _gate = new();

    private LodopFailureStore(string path) => _path = path;

    public static string DefaultPath =>
        Path.Combine(AppContext.BaseDirectory, "logs", "lodop-print-failures.json");

    public static LodopFailureStore For(string path) =>
        Instances.GetOrAdd(Path.GetFullPath(path), p => new LodopFailureStore(p));

    public IReadOnlyList<LodopFailedJob> Load()
    {
        lock (_gate)
            return ReadUnlocked();
    }

    public void Add(LodopFailedJob job)
    {
        lock (_gate)
        {
            var list = ReadUnlocked();
            list.Add(job);
            WriteUnlocked(list);
        }
    }

    public void Remove(string id)
    {
        lock (_gate)
        {
            var list = ReadUnlocked();
            list.RemoveAll(j => j.Id == id);
            WriteUnlocked(list);
        }
    }

    private List<LodopFailedJob> ReadUnlocked()
    {
        try
        {
            if (!File.Exists(_path))
                return new List<LodopFailedJob>();

            var json = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(json))
                return new List<LodopFailedJob>();

            return JsonSerializer.Deserialize<List<LodopFailedJob>>(json, JsonOptions)
                   ?? new List<LodopFailedJob>();
        }
        catch
        {
            return new List<LodopFailedJob>();
        }
    }

    private void WriteUnlocked(List<LodopFailedJob> jobs)
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(jobs, JsonOptions));
            File.Move(tmp, _path, overwrite: true);
        }
        catch
        {
            // Persistence is best-effort; the append-only .txt log still has the record.
        }
    }
}
