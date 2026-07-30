using System.Text.Json;

namespace LabelPrinter.Services;

public sealed record LodopQueuedJob(string Id, string PdfUrl, int Port);

/// <summary>
/// Durable FIFO list of Lodop print jobs under logs/lodop-print-queue.json so a
/// restart/crash can resume work that already returned 200 Queued to the browser.
/// </summary>
public sealed class LodopQueueStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly object _gate = new();

    public LodopQueueStore(string path) => _path = path;

    public static string DefaultPath =>
        Path.Combine(AppContext.BaseDirectory, "logs", "lodop-print-queue.json");

    public IReadOnlyList<LodopQueuedJob> Load()
    {
        lock (_gate)
            return ReadUnlocked();
    }

    public void Add(LodopQueuedJob job)
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

    private List<LodopQueuedJob> ReadUnlocked()
    {
        try
        {
            if (!File.Exists(_path))
                return new List<LodopQueuedJob>();

            var json = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(json))
                return new List<LodopQueuedJob>();

            return JsonSerializer.Deserialize<List<LodopQueuedJob>>(json, JsonOptions)
                   ?? new List<LodopQueuedJob>();
        }
        catch
        {
            return new List<LodopQueuedJob>();
        }
    }

    private void WriteUnlocked(List<LodopQueuedJob> jobs)
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
            // Persistence is best-effort; queue still works in memory.
        }
    }
}
