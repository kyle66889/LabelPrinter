using System.Diagnostics;

namespace LabelPrinter;

public enum InstanceIdentity { Same, Different, Unknown }

public interface IInstanceHandle : IDisposable
{
    int Id { get; }
    string Description { get; }
    bool TryStop(TimeSpan wait, out string? error);
}

public sealed record StopResult(bool AllStopped, IReadOnlyList<string> Errors);

public enum TakeoverOutcome { NothingFound, Declined, Taken, StopFailed, LockStillHeld }

public sealed record TakeoverResult(TakeoverOutcome Outcome, string Detail = "");

/// <summary>
/// Finds and stops an already-running copy of this app so a newer build can take over, instead of
/// just telling the user "already running". Only processes that really are this product are offered.
/// </summary>
public static class PreviousInstance
{
    private const string DefaultProcessName = "LabelPrinter";

    /// <summary>Decides from the exe's product name whether another process is this application.</summary>
    public static InstanceIdentity Classify(string? ownProduct, string? otherProduct)
    {
        if (string.IsNullOrWhiteSpace(ownProduct) || string.IsNullOrWhiteSpace(otherProduct))
            return InstanceIdentity.Unknown;

        return string.Equals(ownProduct.Trim(), otherProduct.Trim(), StringComparison.OrdinalIgnoreCase)
            ? InstanceIdentity.Same
            : InstanceIdentity.Different;
    }

    /// <summary>Other running copies of this app. The caller must dispose every returned handle.</summary>
    public static IReadOnlyList<IInstanceHandle> FindOthers()
    {
        var ownProduct = ReadProduct(Environment.ProcessPath);
        var names = new[] { Process.GetCurrentProcess().ProcessName, DefaultProcessName }
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var found = new List<IInstanceHandle>();
        foreach (var name in names)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                if (p.Id == Environment.ProcessId)
                {
                    p.Dispose();
                    continue;
                }

                var info = Inspect(p);
                if (Classify(ownProduct, info.Product) == InstanceIdentity.Different)
                {
                    p.Dispose();
                    continue;
                }

                found.Add(new ProcessHandle(p, info));
            }
        }

        return found;
    }

    public static string Describe(IEnumerable<IInstanceHandle> instances) =>
        string.Join(Environment.NewLine, instances.Select(i => i.Description));

    public static StopResult StopAll(IReadOnlyList<IInstanceHandle> instances, TimeSpan wait)
    {
        var errors = new List<string>();
        foreach (var instance in instances)
        {
            if (!instance.TryStop(wait, out var error))
                errors.Add($"PID {instance.Id}: {error}");
        }

        return new StopResult(errors.Count == 0, errors);
    }

    /// <summary>
    /// The whole take-over sequence with its side effects injected: ask, stop, then wait for the
    /// single-instance lock to come free (the dying process releases it a moment after it exits).
    /// </summary>
    public static TakeoverResult TakeOver(
        IReadOnlyList<IInstanceHandle> others,
        Func<string, bool> confirm,
        Func<bool> tryAcquireLock,
        TimeSpan stopWait,
        int lockAttempts,
        TimeSpan lockDelay,
        Action<TimeSpan>? sleep = null)
    {
        if (others.Count == 0)
            return new TakeoverResult(TakeoverOutcome.NothingFound);

        if (!confirm(Describe(others)))
            return new TakeoverResult(TakeoverOutcome.Declined);

        var stopped = StopAll(others, stopWait);
        if (!stopped.AllStopped)
            return new TakeoverResult(TakeoverOutcome.StopFailed, string.Join("; ", stopped.Errors));

        sleep ??= Thread.Sleep;
        for (var attempt = 0; attempt < lockAttempts; attempt++)
        {
            if (tryAcquireLock())
                return new TakeoverResult(TakeoverOutcome.Taken);
            if (attempt < lockAttempts - 1)
                sleep(lockDelay);
        }

        return new TakeoverResult(TakeoverOutcome.LockStillHeld);
    }

    private sealed record ProcessInfo(string? Path, string? Version, string? Product);

    // MainModule throws for processes we may not inspect (other user / elevated); then nothing is known.
    private static ProcessInfo Inspect(Process process)
    {
        try
        {
            var path = process.MainModule?.FileName;
            if (path is null)
                return new ProcessInfo(null, null, null);

            var info = FileVersionInfo.GetVersionInfo(path);
            return new ProcessInfo(path, info.ProductVersion?.Split('+')[0], info.ProductName);
        }
        catch
        {
            return new ProcessInfo(null, null, null);
        }
    }

    private static string? ReadProduct(string? path)
    {
        try
        {
            return path is null ? null : FileVersionInfo.GetVersionInfo(path).ProductName;
        }
        catch
        {
            return null;
        }
    }

    private sealed class ProcessHandle : IInstanceHandle
    {
        private readonly Process _process;

        public ProcessHandle(Process process, ProcessInfo info)
        {
            _process = process;
            Id = process.Id;
            Description = Describe(process.Id, info);
        }

        public int Id { get; }

        public string Description { get; }

        public bool TryStop(TimeSpan wait, out string? error)
        {
            error = null;
            try
            {
                if (_process.HasExited)
                    return true;

                _process.Kill();
                if (_process.WaitForExit((int)wait.TotalMilliseconds))
                    return true;

                error = "timeout";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public void Dispose() => _process.Dispose();

        private static string Describe(int id, ProcessInfo info)
        {
            if (info.Path is null)
                return $"PID {id} ({L.T("app.replace.unknown")})";

            return info.Version is null
                ? $"{info.Path} (PID {id})"
                : $"{info.Path} (v{info.Version}, PID {id})";
        }
    }
}
