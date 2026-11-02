using System.Text.Json;

namespace LabelPrinter.Services;

public sealed record SitePermission(string Profile, string Category, string Origin, int Setting);

/// <summary>
/// Reads the per-site "Local network" / "Apps on device" choices Chrome and Edge keep in each
/// profile's Preferences file. The storage format is Chromium-internal and shifts between
/// versions, so results are advisory.
/// </summary>
public static class BrowserSitePermissions
{
    public const int Allow = 1;
    public const int Block = 2;
    public const int Ask = 3;

    private static readonly string[] Categories = { "local_network_access", "local_network", "loopback_network" };

    public static IReadOnlyList<SitePermission> ParsePreferences(string profile, string json)
    {
        var found = new List<SitePermission>();
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("profile", out var p)
            || !p.TryGetProperty("content_settings", out var cs)
            || !cs.TryGetProperty("exceptions", out var exceptions))
            return found;

        foreach (var category in Categories)
        {
            if (!exceptions.TryGetProperty(category, out var entries) || entries.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var entry in entries.EnumerateObject())
            {
                var origin = entry.Name.Split(',')[0];
                if (!origin.Contains("shipswithus.com", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (entry.Value.ValueKind != JsonValueKind.Object
                    || !entry.Value.TryGetProperty("setting", out var s)
                    || s.ValueKind != JsonValueKind.Number)
                    continue;

                found.Add(new SitePermission(profile, category, origin, s.GetInt32()));
            }
        }

        return found;
    }

    /// <summary>Reads every profile under a browser's "User Data" folder; unreadable ones are skipped.</summary>
    public static IReadOnlyList<SitePermission> ReadAllProfiles(string userDataDir)
    {
        var all = new List<SitePermission>();
        if (!Directory.Exists(userDataDir))
            return all;

        foreach (var dir in Directory.EnumerateDirectories(userDataDir))
        {
            var name = Path.GetFileName(dir);
            if (name != "Default" && !name.StartsWith("Profile ", StringComparison.Ordinal))
                continue;

            var prefs = Path.Combine(dir, "Preferences");
            try
            {
                using var fs = new FileStream(prefs, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs);
                all.AddRange(ParsePreferences(name, reader.ReadToEnd()));
            }
            catch
            {
                // no Preferences yet, or locked/corrupt: skip this profile
            }
        }

        return all;
    }

    public static string SettingText(int setting) => setting switch
    {
        Allow => L.T("sc.site.allow"),
        Block => L.T("sc.site.block"),
        Ask => L.T("sc.site.ask"),
        _ => setting.ToString()
    };

    public static CheckResult Evaluate(string browserName, IReadOnlyList<SitePermission> permissions)
    {
        var title = L.F("sc.site.title", browserName);
        if (permissions.Count == 0)
            return new CheckResult(CheckStatus.Info, title, L.T("sc.site.none"), L.T("sc.site.none.fix"));

        var summary = string.Join("; ", permissions.Select(p =>
            $"{p.Profile}: {p.Origin} {CategoryText(p.Category)}={SettingText(p.Setting)}"));

        // The check can't tell which browser profile the user prints from, so it judges each one:
        // a block only counts as THE cause when every profile that has an opinion blocks.
        var states = permissions
            .GroupBy(p => p.Profile)
            .Select(g => (Profile: g.Key, Setting: EffectiveSetting(g)))
            .Where(s => s.Setting is not null)
            .ToList();

        if (states.Count == 0)
            return new CheckResult(CheckStatus.Info, title, L.F("sc.site.pending", summary), L.T("sc.site.none.fix"));

        var blocked = states.Where(s => s.Setting == Block).Select(s => s.Profile).ToList();
        if (blocked.Count == states.Count)
            return new CheckResult(CheckStatus.Fail, title, L.F("sc.site.blocked", summary), L.T("sc.site.blocked.fix"));

        if (blocked.Count > 0)
            return new CheckResult(
                CheckStatus.Warn, title,
                L.F("sc.site.mixed", string.Join(", ", blocked), summary), L.T("sc.site.blocked.fix"));

        return states.All(s => s.Setting == Allow)
            ? new CheckResult(CheckStatus.Ok, title, L.F("sc.site.ok", summary))
            : new CheckResult(CheckStatus.Info, title, L.F("sc.site.pending", summary), L.T("sc.site.none.fix"));
    }

    /// <summary>
    /// What one profile decides for reaching this PC. MZL talks to localhost, so the loopback
    /// ("Apps on device") entry is decisive when present, then the local-network-access one. The
    /// plain LAN "local_network" category is ignored here: blocking the LAN does not stop localhost.
    /// </summary>
    private static int? EffectiveSetting(IEnumerable<SitePermission> profilePermissions)
    {
        var list = profilePermissions.ToList();
        var relevant = list.Where(p => p.Category == "loopback_network").ToList();
        if (relevant.Count == 0)
            relevant = list.Where(p => p.Category == "local_network_access").ToList();
        if (relevant.Count == 0)
            return null;

        if (relevant.Any(p => p.Setting == Block))
            return Block;
        return relevant.All(p => p.Setting == Allow) ? Allow : Ask;
    }

    private static string CategoryText(string category) =>
        category == "loopback_network" ? L.T("sc.site.cat.loopback") : L.T("sc.site.cat.local");
}
