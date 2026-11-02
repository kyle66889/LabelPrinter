namespace LabelPrinter.Services;

public sealed record LodopRequestSnapshot(
    int ScriptLoads,
    int Preflights,
    int PrintPosts,
    DateTime? LastLocalTime,
    string? LastSource);

/// <summary>
/// Counts requests that came from a real browser page (not our own Settings probes), so the
/// self-check can tell "the browser never reached us" apart from "it reached us but didn't print".
/// </summary>
public sealed class LodopRequestStats
{
    /// <summary>
    /// True for a request made by a web page (script tag, fetch, preflight). Typing the URL into the
    /// address bar sends Sec-Fetch-Site: none and no Origin/Referer, so it does not count: someone
    /// testing the port by hand must not look like MZL reaching us.
    /// </summary>
    public static bool IsBrowserPageRequest(string? origin, string? referer, string? secFetchSite) =>
        !string.IsNullOrEmpty(origin)
        || !string.IsNullOrEmpty(referer)
        || (!string.IsNullOrEmpty(secFetchSite)
            && !secFetchSite.Equals("none", StringComparison.OrdinalIgnoreCase));

    private readonly object _gate = new();
    private int _scriptLoads;
    private int _preflights;
    private int _printPosts;
    private DateTime? _last;
    private string? _lastSource;

    public void Record(string method, string path, string? source, DateTime? now = null)
    {
        lock (_gate)
        {
            if (method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                _preflights++;
            else if (method.Equals("POST", StringComparison.OrdinalIgnoreCase)
                     && path.Equals("/lodop_print", StringComparison.OrdinalIgnoreCase))
                _printPosts++;
            else if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
                     && (path == "/" || path.Equals("/CLodopfuncs.js", StringComparison.OrdinalIgnoreCase)))
                _scriptLoads++;
            else
                return;

            _last = now ?? DateTime.Now;
            if (!string.IsNullOrWhiteSpace(source))
                _lastSource = source;
        }
    }

    public LodopRequestSnapshot Snapshot()
    {
        lock (_gate)
            return new LodopRequestSnapshot(_scriptLoads, _preflights, _printPosts, _last, _lastSource);
    }
}
