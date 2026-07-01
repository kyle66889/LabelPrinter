using System.Text.Json;
using System.Text.Json.Serialization;
using LabelPrinter.Printing;
using Microsoft.Extensions.Configuration;

namespace LabelPrinter;

public sealed class AppConfig
{
    // --- Global settings (persisted) ---
    public string LabelPrinterUrl { get; set; } = "ws://localhost:2012/websocket";
    public bool EnableWebSocket { get; set; } = true;
    public bool AllowLanAccess { get; set; }
    public int ReconnectDelaySeconds { get; set; } = 5;
    public int WebSocketConnectTimeoutSeconds { get; set; } = 10;
    public bool RunAtStartup { get; set; }

    public List<LabelFormat> LabelFormats { get; set; } = new();

    // --- Legacy fields: only read for migration, never written by Save() ---
    public string PrinterName { get; set; } = "";
    public string PrinterAlias { get; set; } = "";
    public bool UseLptPrinter { get; set; }
    public string LptPort { get; set; } = "LPT1";
    public string RestListenPrefix { get; set; } = "";
    public bool EnableRestEndpoint { get; set; } = true;

    public static List<LabelFormat> CreateDefaultFormats() => new()
    {
        new LabelFormat { Size = "4x2", Alias = "4x2", Port = 48210, PrintType = LabelPrintType.Epl, Enabled = true },
        new LabelFormat { Size = "4x3", Alias = "4x3", Port = 48211, PrintType = LabelPrintType.Epl, Enabled = true },
        new LabelFormat { Size = "4x6", Alias = "4x6", Port = 48212, PrintType = LabelPrintType.Epl, Enabled = true, IsDefault = true }
    };

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
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

        var root = builder.Build();
        var config = new AppConfig();
        root.GetSection("LabelPrinter").Bind(config);
        config.MigrateLegacy();
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
                ["LabelFormats"] = LabelFormats.Select(f => new Dictionary<string, object?>
                {
                    ["Size"] = f.Size,
                    ["Alias"] = f.Alias,
                    ["PrinterName"] = f.PrinterName,
                    ["PrintType"] = f.PrintType.ToString(),
                    ["Port"] = f.Port,
                    ["Enabled"] = f.Enabled,
                    ["IsDefault"] = f.IsDefault
                }).ToList()
            }
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        File.WriteAllText(path, JsonSerializer.Serialize(root, options));
    }
}
