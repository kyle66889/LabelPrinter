using System.Drawing.Printing;
using System.Text.Json;
using System.Text.Json.Serialization;
using LabelPrinter.Printing;
using Microsoft.Extensions.Configuration;

namespace LabelPrinter;

public sealed class AppConfig
{
    // --- Global settings (persisted) ---
    public string LabelPrinterUrl { get; set; } = "ws://localhost:2012/websocket";
    public bool EnableWebSocket { get; set; } = false;
    public bool AllowLanAccess { get; set; }
    public int ReconnectDelaySeconds { get; set; } = 5;
    public int WebSocketConnectTimeoutSeconds { get; set; } = 10;
    // First-run default: on. Existing appsettings.json values are preserved on upgrade.
    public bool RunAtStartup { get; set; } = true;
    public string Language { get; set; } = "zh"; // "zh" or "en"

    public List<LabelFormat> LabelFormats { get; set; } = new();
    public LodopCompatConfig LodopCompat { get; set; } = new();

    // --- Legacy fields: only read for migration, never written by Save() ---
    public string PrinterName { get; set; } = "";
    public string PrinterAlias { get; set; } = "";
    public bool UseLptPrinter { get; set; }
    public string LptPort { get; set; } = "LPT1";
    public string RestListenPrefix { get; set; } = "";
    public bool EnableRestEndpoint { get; set; } = true;

    /// <summary>
    /// Size rows for a brand-new install: present in the UI but disabled. MZL compat is
    /// the path that starts enabled (see <see cref="CreateFirstRunDefaults"/>).
    /// </summary>
    public static List<LabelFormat> CreateDefaultFormats() => new()
    {
        new LabelFormat { Size = "4x2", Alias = "4x2", Port = 48210, PrintType = LabelPrintType.Epl, Enabled = false },
        new LabelFormat { Size = "4x3", Alias = "4x3", Port = 48211, PrintType = LabelPrintType.Epl, Enabled = false },
        new LabelFormat { Size = "4x6", Alias = "4x6", Port = 48212, PrintType = LabelPrintType.Epl, Enabled = false, IsDefault = true }
    };

    /// <summary>
    /// Defaults used when there is no usable appsettings.json yet (corrupt/missing).
    /// Prefer a BIXOLON printer for MZL when one is installed.
    /// </summary>
    public static AppConfig CreateFirstRunDefaults(IEnumerable<string>? installedPrinters = null) => new()
    {
        RunAtStartup = true,
        LabelFormats = CreateDefaultFormats(),
        LodopCompat = new LodopCompatConfig
        {
            Enabled = true,
            PrinterName = PickDefaultLodopPrinterName(installedPrinters)
        }
    };

    /// <summary>
    /// Prefers any installed printer whose name contains "bixolon" (case-insensitive);
    /// otherwise the first installed printer; otherwise empty (user sets later).
    /// </summary>
    public static string PickDefaultLodopPrinterName(IEnumerable<string>? installedPrinters = null)
    {
        string? bixolon = null;
        string? first = null;
        foreach (var name in installedPrinters ?? EnumerateInstalledPrinters())
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;
            first ??= name;
            if (name.Contains("bixolon", StringComparison.OrdinalIgnoreCase))
            {
                bixolon = name;
                break;
            }
        }

        return bixolon ?? first ?? "";
    }

    private static IEnumerable<string> EnumerateInstalledPrinters()
    {
        foreach (string name in PrinterSettings.InstalledPrinters)
            yield return name;
    }

    /// <summary>
    /// If MZL has no printer yet, fill one from the installed list. Returns true when
    /// the name changed (caller may persist). Does not change an already-set name.
    /// </summary>
    public bool EnsureLodopPrinterIfEmpty(IEnumerable<string>? installedPrinters = null)
    {
        if (!string.IsNullOrWhiteSpace(LodopCompat.PrinterName))
            return false;

        var picked = PickDefaultLodopPrinterName(installedPrinters);
        if (string.IsNullOrEmpty(picked))
            return false;

        LodopCompat.PrinterName = picked;
        return true;
    }

    /// <summary>
    /// Ensures LabelFormats is populated. If empty (e.g. loading an old config file),
    /// seeds the three defaults and folds the legacy single-printer settings into the
    /// default (4x6) format.
    /// </summary>
    public void MigrateLegacy()
    {
        if (LabelFormats.Count > 0)
            return;

        LabelFormats = CreateDefaultFormats();
        var def = LabelFormats.Single(f => f.IsDefault);

        if (UseLptPrinter && !string.IsNullOrWhiteSpace(LptPort))
            def.PrinterName = LptPort.Trim();
        else if (!string.IsNullOrWhiteSpace(PrinterName))
            def.PrinterName = PrinterName.Trim();
    }

    public LabelFormat? FindFormatByAlias(string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
            return null;

        return LabelFormats.FirstOrDefault(f =>
            f.Enabled && string.Equals(f.Alias, alias, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns human-readable problems that should block a save, currently: two or more
    /// enabled formats sharing the same REST port (each needs its own port to listen).
    /// </summary>
    public List<string> ValidateFormats()
    {
        var errors = new List<string>();

        var dupePorts = LabelFormats
            .Where(f => f.Enabled)
            .GroupBy(f => f.Port)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var port in dupePorts)
        {
            var sizes = string.Join(", ", LabelFormats.Where(f => f.Enabled && f.Port == port).Select(f => f.Size));
            errors.Add($"端口 {port} 被多个启用的尺寸占用（{sizes}）。每个启用的尺寸需要不同的端口。");
        }

        return errors;
    }

    public static AppConfig Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        AppConfig config;
        try
        {
            if (!File.Exists(path))
            {
                // True first launch with no packaged config: MZL-only + startup + BIXOLON pick.
                config = CreateFirstRunDefaults();
                try { config.Save(); } catch { /* best-effort */ }
            }
            else
            {
                config = new AppConfig();
                var builder = new ConfigurationBuilder()
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

                var root = builder.Build();
                root.GetSection("LabelPrinter").Bind(config);
                config.MigrateLegacy();
                // Packaged template ships PrinterName=""; fill once so Settings isn't empty.
                if (config.EnsureLodopPrinterIfEmpty())
                {
                    try { config.Save(); } catch { /* best-effort */ }
                }
            }
        }
        catch (Exception ex)
        {
            // A corrupt/half-written appsettings.json must not stop the app from starting.
            // Log it and fall back to first-run defaults; the user can re-save from Settings.
            FileLog.Write($"Config load failed, falling back to defaults: {ex.Message}");
            config = CreateFirstRunDefaults();
        }

        L.SetLanguage(L.Parse(config.Language));
        return config;
    }

    public void Save()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var root = new Dictionary<string, object>
        {
            ["LabelPrinter"] = new Dictionary<string, object?>
            {
                ["LabelPrinterUrl"] = LabelPrinterUrl,
                ["EnableWebSocket"] = EnableWebSocket,
                ["AllowLanAccess"] = AllowLanAccess,
                ["ReconnectDelaySeconds"] = ReconnectDelaySeconds,
                ["WebSocketConnectTimeoutSeconds"] = WebSocketConnectTimeoutSeconds,
                ["RunAtStartup"] = RunAtStartup,
                ["Language"] = Language,
                ["LabelFormats"] = LabelFormats.Select(f => new Dictionary<string, object?>
                {
                    ["Size"] = f.Size,
                    ["Alias"] = f.Alias,
                    ["PrinterName"] = f.PrinterName,
                    ["PrintType"] = f.PrintType.ToString(),
                    ["Port"] = f.Port,
                    ["Enabled"] = f.Enabled,
                    ["IsDefault"] = f.IsDefault
                }).ToList(),
                ["LodopCompat"] = new Dictionary<string, object?>
                {
                    ["Enabled"] = LodopCompat.Enabled,
                    ["PrinterName"] = LodopCompat.PrinterName
                }
            }
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        var json = JsonSerializer.Serialize(root, options);

        // Atomic write: serialize to a temp file first, then swap it into place. A crash
        // mid-write leaves the old (valid) file intact instead of a truncated one that
        // would fail to parse on next launch.
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>
/// Settings for the C-Lodop compatibility shim: no Size/Alias/PrintType/Port, since it
/// isn't one of the fixed label sizes — it's a single always-8000/18000 endpoint that
/// stands in for a real C-Lodop install so an existing caller (e.g. MZL) can print PDFs
/// through LabelPrinter without any change on its side. See Services/LodopCompatListener.
/// </summary>
public sealed class LodopCompatConfig
{
    // First-run / property default: on. Persisted appsettings values always win after Save.
    public bool Enabled { get; set; } = true;
    public string PrinterName { get; set; } = "";
}
