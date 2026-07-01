# Multi-Format Printing & Settings UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Support three fixed label sizes (4×2 / 4×3 / 4×6), each bound to its own printer + REST port + print type, with per-size test buttons and host-IP display in the settings form.

**Architecture:** Config gains a fixed 3-entry `LabelFormats` list. Each enabled format runs its own `RestPrintListener` on its own port and forwards bytes verbatim to its bound printer (Windows printer name or `LPT*`). WebSocket routes by matching the message `alias` to a format's `Alias`; unmatched → log + skip. Print type only drives the built-in test sample and UI display. The settings form is rebuilt as a table (one row per size).

**Tech Stack:** C# / .NET 8 (WinForms, `net8.0-windows`), `Microsoft.Extensions.Configuration`, xUnit for tests. Build/test with the .NET 9 SDK already installed.

---

## File Structure

**New files:**
- `Printing/LabelFormat.cs` — `LabelFormat` class + `LabelPrintType` enum.
- `Printing/SampleLabelGenerator.cs` — pure function: (type, size) → sample label string.
- `Services/NetworkHelper.cs` — detect local IPv4.
- `LabelPrinter.Tests/LabelPrinter.Tests.csproj` — xUnit test project.
- `LabelPrinter.Tests/AppConfigTests.cs`
- `LabelPrinter.Tests/SampleLabelGeneratorTests.cs`
- `LabelPrinter.Tests/NetworkHelperTests.cs`

**Modified files:**
- `Config.cs` — `LabelFormats`, `AllowLanAccess`, defaults, legacy migration, `FindFormatByAlias`, new `Save()` schema.
- `Printing/PrintModel.cs` — `PrintTo(data, printerName)` with LPT-prefix detection; drop config dependency.
- `Services/RestPrintListener.cs` — per-format (one listener per size/port).
- `Services/WebSocketPrintListener.cs` — alias → format routing; skip on no match.
- `PrintHostService.cs` — spin up one REST listener per enabled format.
- `SettingsForm.Designer.cs` / `SettingsForm.cs` — table layout, IP display, per-row test, LAN toggle.
- `README.md`, `appsettings.json` — document new schema.

---

## Task 0: Add xUnit test project

**Files:**
- Create: `LabelPrinter.Tests/LabelPrinter.Tests.csproj`

- [ ] **Step 1: Create the test project**

Run:
```bash
cd "C:/Users/KyleHu/source/repos/LabelPrinter"
dotnet new xunit -n LabelPrinter.Tests -o LabelPrinter.Tests
```
Expected: creates `LabelPrinter.Tests/` with `UnitTest1.cs` and a `.csproj`.

- [ ] **Step 2: Retarget to net8.0-windows and reference the main project**

Overwrite `LabelPrinter.Tests/LabelPrinter.Tests.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\LabelPrinter.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Delete the scaffold test**

Run:
```bash
rm "LabelPrinter.Tests/UnitTest1.cs"
```

- [ ] **Step 3b: Exclude the test project from the main project's globs**

The test project lives *inside* the main project's directory, so `LabelPrinter.csproj`'s default `**/*.cs` glob would pull the test project's generated `obj` files (and, later, its test sources) into the main compile — causing duplicate-assembly-attribute errors and xunit-reference failures. Add a `DefaultItemExcludes` line to `LabelPrinter.csproj`'s `<PropertyGroup>` (right after the `<Description>` line):

```xml
    <DefaultItemExcludes>$(DefaultItemExcludes);LabelPrinter.Tests\**</DefaultItemExcludes>
```

Do **not** disable `GenerateAssemblyInfo` — that only masks half the problem.

- [ ] **Step 4: Verify it builds and runs (zero tests)**

Run:
```bash
dotnet test "LabelPrinter.Tests/LabelPrinter.Tests.csproj"
```
Expected: build succeeds; "Passed! - Failed: 0, Passed: 0" (no tests yet).

- [ ] **Step 5: Commit**

```bash
git add LabelPrinter.Tests/
git commit -m "test: add xUnit test project"
```

---

## Task 1: LabelFormat model + LabelPrintType enum

**Files:**
- Create: `Printing/LabelFormat.cs`

- [ ] **Step 1: Create the model**

`Printing/LabelFormat.cs`:
```csharp
namespace LabelPrinter.Printing;

public enum LabelPrintType
{
    Epl,
    Zpl,
    Text
}

/// <summary>
/// One label size and where/how it prints. Each format owns a REST port
/// and is bound to a printer (a Windows printer name or an "LPT*" port).
/// </summary>
public sealed class LabelFormat
{
    public string Size { get; set; } = "";            // "4x2" / "4x3" / "4x6" (fixed set)
    public string Alias { get; set; } = "";           // WebSocket routing key
    public string PrinterName { get; set; } = "";      // Windows printer OR "LPT1"/"LPT2"/"LPT3"
    public LabelPrintType PrintType { get; set; } = LabelPrintType.Epl;
    public int Port { get; set; }                      // dedicated REST port
    public bool Enabled { get; set; } = true;
    public bool IsDefault { get; set; }                // UI highlight only
}
```

- [ ] **Step 2: Build**

Run:
```bash
dotnet build "LabelPrinter.csproj"
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add Printing/LabelFormat.cs
git commit -m "feat: add LabelFormat model and LabelPrintType enum"
```

---

## Task 2: AppConfig — formats, defaults, migration, routing, save

**Files:**
- Modify: `Config.cs`
- Test: `LabelPrinter.Tests/AppConfigTests.cs`

- [ ] **Step 1: Write the failing tests**

`LabelPrinter.Tests/AppConfigTests.cs`:
```csharp
using LabelPrinter;
using LabelPrinter.Printing;
using Xunit;

namespace LabelPrinter.Tests;

public class AppConfigTests
{
    [Fact]
    public void CreateDefaultFormats_returns_three_sizes_with_expected_ports()
    {
        var formats = AppConfig.CreateDefaultFormats();

        Assert.Equal(3, formats.Count);
        Assert.Equal("4x2", formats[0].Size);
        Assert.Equal(48210, formats[0].Port);
        Assert.Equal("4x3", formats[1].Size);
        Assert.Equal(48211, formats[1].Port);
        Assert.Equal("4x6", formats[2].Size);
        Assert.Equal(48212, formats[2].Port);
        Assert.True(formats[2].IsDefault);
        Assert.All(formats, f => Assert.Equal(f.Size, f.Alias));
    }

    [Fact]
    public void MigrateLegacy_seeds_formats_and_folds_windows_printer_into_default()
    {
        var config = new AppConfig { PrinterName = "ZDesigner GK420t" };

        config.MigrateLegacy();

        Assert.Equal(3, config.LabelFormats.Count);
        var def = config.LabelFormats.Single(f => f.IsDefault);
        Assert.Equal("4x6", def.Size);
        Assert.Equal("ZDesigner GK420t", def.PrinterName);
    }

    [Fact]
    public void MigrateLegacy_folds_lpt_port_into_default_when_lpt_enabled()
    {
        var config = new AppConfig { UseLptPrinter = true, LptPort = "LPT2" };

        config.MigrateLegacy();

        var def = config.LabelFormats.Single(f => f.IsDefault);
        Assert.Equal("LPT2", def.PrinterName);
    }

    [Fact]
    public void MigrateLegacy_does_not_overwrite_existing_formats()
    {
        var config = new AppConfig
        {
            PrinterName = "Legacy",
            LabelFormats = new List<LabelFormat>
            {
                new() { Size = "4x6", PrinterName = "Existing", IsDefault = true }
            }
        };

        config.MigrateLegacy();

        Assert.Single(config.LabelFormats);
        Assert.Equal("Existing", config.LabelFormats[0].PrinterName);
    }

    [Fact]
    public void FindFormatByAlias_matches_enabled_format_case_insensitively()
    {
        var config = new AppConfig { LabelFormats = AppConfig.CreateDefaultFormats() };

        var match = config.FindFormatByAlias("4X6");

        Assert.NotNull(match);
        Assert.Equal("4x6", match!.Size);
    }

    [Fact]
    public void FindFormatByAlias_returns_null_for_missing_or_disabled()
    {
        var config = new AppConfig { LabelFormats = AppConfig.CreateDefaultFormats() };
        config.LabelFormats.ForEach(f => f.Enabled = false);

        Assert.Null(config.FindFormatByAlias(null));
        Assert.Null(config.FindFormatByAlias(""));
        Assert.Null(config.FindFormatByAlias("nope"));
        Assert.Null(config.FindFormatByAlias("4x6")); // disabled
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:
```bash
dotnet test "LabelPrinter.Tests/LabelPrinter.Tests.csproj"
```
Expected: FAIL — `AppConfig` has no `CreateDefaultFormats`, `MigrateLegacy`, `FindFormatByAlias`, or `LabelFormats` (compile errors).

- [ ] **Step 3: Rewrite `Config.cs`**

Replace the entire contents of `Config.cs` with:

```csharp
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

    // Legacy shim: kept ONLY so the pre-refactor PrintModel keeps compiling
    // between this task and Task 5. Task 5 deletes this method.
    public string ResolvePrinterName(string? aliasFromMessage) => PrinterName;

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
```

Note: `PrintType` is written as a string (`"Epl"`) via `ToString()`; `Microsoft.Extensions.Configuration` binds the enum back from that string on load automatically.

- [ ] **Step 4: Run tests to verify they pass**

Run:
```bash
dotnet test "LabelPrinter.Tests/LabelPrinter.Tests.csproj"
```
Expected: PASS (6 AppConfig tests). Because the legacy fields and the `ResolvePrinterName` shim are retained, the whole solution still compiles at this point, so the suite builds and runs green.

> **Ordering note:** The `PrintBarcode` → `PrintTo` rename in Task 5 breaks its three callers (`RestPrintListener`, `WebSocketPrintListener`, `SettingsForm`). The project will not compile between the start of Task 5 and the end of Task 8. Implement Tasks 5→8 as a batch, committing each file group, and run the full `dotnet test` at the end of Task 8. Tasks 2, 3, and 4 each keep the solution green and can be committed and tested independently.

- [ ] **Step 5: Commit**

```bash
git add Config.cs LabelPrinter.Tests/AppConfigTests.cs
git commit -m "feat: AppConfig multi-format support with legacy migration"
```

---

## Task 3: SampleLabelGenerator

**Files:**
- Create: `Printing/SampleLabelGenerator.cs`
- Test: `LabelPrinter.Tests/SampleLabelGeneratorTests.cs`

- [ ] **Step 1: Write the failing tests**

`LabelPrinter.Tests/SampleLabelGeneratorTests.cs`:
```csharp
using LabelPrinter.Printing;
using Xunit;

namespace LabelPrinter.Tests;

public class SampleLabelGeneratorTests
{
    [Fact]
    public void Epl_4x2_has_width_and_height_dots_at_203dpi()
    {
        var epl = SampleLabelGenerator.Generate(LabelPrintType.Epl, "4x2");

        Assert.Contains("q812", epl);        // 4in * 203
        Assert.Contains("Q406,24", epl);     // 2in * 203
        Assert.Contains("\"TEST 4x2\"", epl);
        Assert.Contains("P1", epl);
    }

    [Fact]
    public void Zpl_4x6_has_print_width_and_label_length()
    {
        var zpl = SampleLabelGenerator.Generate(LabelPrintType.Zpl, "4x6");

        Assert.Contains("^PW812", zpl);      // 4in
        Assert.Contains("^LL1218", zpl);     // 6in
        Assert.Contains("^FDTEST 4x6^FS", zpl);
        Assert.StartsWith("^XA", zpl);
        Assert.Contains("^XZ", zpl);
    }

    [Fact]
    public void Text_contains_size_and_form_feed()
    {
        var text = SampleLabelGenerator.Generate(LabelPrintType.Text, "4x3");

        Assert.Contains("TEST 4x3", text);
        Assert.EndsWith("\f", text);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:
```bash
dotnet test "LabelPrinter.Tests/LabelPrinter.Tests.csproj" --filter "FullyQualifiedName~SampleLabelGeneratorTests"
```
Expected: FAIL — `SampleLabelGenerator` does not exist.

- [ ] **Step 3: Implement the generator**

`Printing/SampleLabelGenerator.cs`:
```csharp
using System.Globalization;

namespace LabelPrinter.Printing;

/// <summary>
/// Builds a small sample label for the "test" buttons. Sizes are "WxH" in inches;
/// dot counts assume 203 dpi (8 dots/mm) printers.
/// </summary>
public static class SampleLabelGenerator
{
    private const int DotsPerInch = 203;

    public static string Generate(LabelPrintType type, string size)
    {
        var (widthDots, heightDots) = ToDots(size);

        return type switch
        {
            LabelPrintType.Zpl =>
                $"^XA\n^PW{widthDots}\n^LL{heightDots}\n^FO50,50^A0N,40,40^FDTEST {size}^FS\n^XZ\n",
            LabelPrintType.Text =>
                $"TEST {size}\nControlCode Label Printer\n\f",
            _ => // Epl
                $"N\nq{widthDots}\nQ{heightDots},24\nA50,50,0,4,1,1,N,\"TEST {size}\"\nP1\n"
        };
    }

    private static (int width, int height) ToDots(string size)
    {
        var parts = size.ToLowerInvariant().Split('x');
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var w)
            || !double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var h))
        {
            // Fall back to 4x6 if the size string is malformed.
            w = 4;
            h = 6;
        }

        return ((int)Math.Round(w * DotsPerInch), (int)Math.Round(h * DotsPerInch));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run:
```bash
dotnet test "LabelPrinter.Tests/LabelPrinter.Tests.csproj" --filter "FullyQualifiedName~SampleLabelGeneratorTests"
```
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add Printing/SampleLabelGenerator.cs LabelPrinter.Tests/SampleLabelGeneratorTests.cs
git commit -m "feat: add SampleLabelGenerator for test buttons"
```

---

## Task 4: NetworkHelper (local IPv4)

**Files:**
- Create: `Services/NetworkHelper.cs`
- Test: `LabelPrinter.Tests/NetworkHelperTests.cs`

- [ ] **Step 1: Write the failing test**

`LabelPrinter.Tests/NetworkHelperTests.cs`:
```csharp
using System.Net;
using System.Net.Sockets;
using LabelPrinter.Services;
using Xunit;

namespace LabelPrinter.Tests;

public class NetworkHelperTests
{
    [Fact]
    public void GetLocalIPv4_returns_a_parseable_ipv4()
    {
        var ip = NetworkHelper.GetLocalIPv4();

        Assert.True(IPAddress.TryParse(ip, out var parsed), $"'{ip}' is not a valid IP");
        Assert.Equal(AddressFamily.InterNetwork, parsed!.AddressFamily);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```bash
dotnet test "LabelPrinter.Tests/LabelPrinter.Tests.csproj" --filter "FullyQualifiedName~NetworkHelperTests"
```
Expected: FAIL — `NetworkHelper` does not exist.

- [ ] **Step 3: Implement**

`Services/NetworkHelper.cs`:
```csharp
using System.Net;
using System.Net.Sockets;

namespace LabelPrinter.Services;

public static class NetworkHelper
{
    /// <summary>
    /// Returns the primary local IPv4 address (the interface used to reach the
    /// network), falling back to any non-loopback IPv4, then to 127.0.0.1.
    /// The UDP "connect" sends no packets; it just selects the routing interface.
    /// </summary>
    public static string GetLocalIPv4()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint ep && !IPAddress.IsLoopback(ep.Address))
                return ep.Address.ToString();
        }
        catch
        {
            // no network / offline — fall through
        }

        try
        {
            foreach (var ip in Dns.GetHostAddresses(Dns.GetHostName()))
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    return ip.ToString();
        }
        catch
        {
            // ignore
        }

        return "127.0.0.1";
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:
```bash
dotnet test "LabelPrinter.Tests/LabelPrinter.Tests.csproj" --filter "FullyQualifiedName~NetworkHelperTests"
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Services/NetworkHelper.cs LabelPrinter.Tests/NetworkHelperTests.cs
git commit -m "feat: add NetworkHelper.GetLocalIPv4"
```

---

## Task 5: PrintModel — PrintTo(data, printerName)

**Files:**
- Modify: `Printing/PrintModel.cs`

- [ ] **Step 1: Rewrite PrintModel**

Replace the entire contents of `Printing/PrintModel.cs` with:

```csharp
namespace LabelPrinter.Printing;

public sealed class PrintModel
{
    /// <summary>
    /// Prints one or more label command blocks to a specific target. The target is
    /// either a Windows printer name or a parallel port ("LPT1".."LPT3"). The server
    /// may send multiple labels separated by blank lines; each is sent as one job.
    /// </summary>
    public void PrintTo(string data, string printerName)
    {
        if (string.IsNullOrWhiteSpace(data))
            throw new ArgumentException("Print data is empty.", nameof(data));
        if (string.IsNullOrWhiteSpace(printerName))
            throw new InvalidOperationException("No printer is configured for this label size.");

        var isLpt = printerName.TrimStart().StartsWith("LPT", StringComparison.OrdinalIgnoreCase);

        foreach (var block in SplitJobs(data))
        {
            if (isLpt)
                LptPrinter.Print(printerName, block);
            else
                RawPrinterHelper.SendStringToPrinter(printerName, block);
        }
    }

    private static IEnumerable<string> SplitJobs(string data)
    {
        var normalized = data.Replace("\r\n", "\n").Trim();
        if (string.IsNullOrEmpty(normalized))
            yield break;

        var parts = normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 1)
        {
            foreach (var part in parts)
                yield return part.TrimEnd() + "\n";
            yield break;
        }

        yield return normalized.EndsWith('\n') ? normalized : normalized + "\n";
    }
}
```

- [ ] **Step 2: Delete the legacy `ResolvePrinterName` shim from Config.cs**

In `Config.cs`, delete these three lines (the rewritten `PrintModel` no longer needs them):

```csharp
    // Legacy shim: kept ONLY so the pre-refactor PrintModel keeps compiling
    // between this task and Task 5. Task 5 deletes this method.
    public string ResolvePrinterName(string? aliasFromMessage) => PrinterName;
```

- [ ] **Step 3: Build the main project (expect callers to break)**

Run:
```bash
dotnet build "LabelPrinter.csproj"
```
Expected: FAIL — `RestPrintListener`, `WebSocketPrintListener`, `SettingsForm` still call the old `PrintBarcode` / `PrintModel(config)` API. These are fixed in Tasks 6–8. Do not commit yet if red; continue to Task 6.

- [ ] **Step 4: Commit (after Task 8 build is green)**

```bash
git add Printing/PrintModel.cs Config.cs
git commit -m "refactor: PrintModel.PrintTo targets an explicit printer (Windows or LPT)"
```

---

## Task 6: Per-format REST listeners + PrintHostService

**Files:**
- Modify: `Services/RestPrintListener.cs`
- Modify: `PrintHostService.cs`

- [ ] **Step 1: Rewrite RestPrintListener to serve one format**

Replace the entire contents of `Services/RestPrintListener.cs` with:

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using LabelPrinter.Printing;

namespace LabelPrinter.Services;

/// <summary>
/// Local HTTP endpoint for ONE label size: POST /LabelPrint with a raw label body
/// or JSON { "epl": "..." }. The bound port already selects the target printer, so
/// the request body's printer is implicit.
/// </summary>
public sealed class RestPrintListener : IDisposable
{
    private readonly LabelFormat _format;
    private readonly bool _allowLan;
    private readonly PrintModel _printModel;
    private readonly Action<string> _log;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _task;

    public RestPrintListener(LabelFormat format, bool allowLan, PrintModel printModel, Action<string> log)
    {
        _format = format;
        _allowLan = allowLan;
        _printModel = printModel;
        _log = log;
    }

    public void Start()
    {
        Stop();
        var host = _allowLan ? "+" : "localhost";
        var prefix = $"http://{host}:{_format.Port}/";

        _listener = new HttpListener();
        _listener.Prefixes.Add(prefix);
        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex)
        {
            _log($"REST [{_format.Size}] failed to listen on {prefix}: {ex.Message}. " +
                 "If 'Allow LAN access' is on, run as administrator or add a urlacl " +
                 $"(netsh http add urlacl url={prefix} user=Everyone).");
            _listener = null;
            return;
        }

        _cts = new CancellationTokenSource();
        _task = Task.Run(() => ListenAsync(_cts.Token));
        _log($"REST [{_format.Size}] listening on {prefix}LabelPrint -> {_format.PrinterName}");
    }

    public void Stop()
    {
        _cts?.Cancel();
        if (_listener?.IsListening == true)
            _listener.Stop();
        try
        {
            _task?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // ignore
        }

        _listener?.Close();
        _listener = null;
        _cts?.Dispose();
        _cts = null;
        _task = null;
    }

    private async Task ListenAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener is { IsListening: true })
        {
            try
            {
                var ctx = await _listener.GetContextAsync().WaitAsync(token).ConfigureAwait(false);
                _ = Task.Run(() => HandleRequest(ctx), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log($"REST [{_format.Size}] listener error: {ex.Message}");
            }
        }
    }

    private void HandleRequest(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath?.TrimEnd('/') ?? "";
            if (!ctx.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase)
                || !path.EndsWith("/LabelPrint", StringComparison.OrdinalIgnoreCase))
            {
                WriteResponse(ctx, 404, "Not Found");
                return;
            }

            using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
            var body = reader.ReadToEnd();
            string data = body;

            if (ctx.Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
            {
                var doc = JsonDocument.Parse(body);
                data = doc.RootElement.GetProperty("epl").GetString() ?? "";
            }

            if (string.IsNullOrWhiteSpace(data))
            {
                WriteResponse(ctx, 400, "Label body is required.");
                return;
            }

            _printModel.PrintTo(data, _format.PrinterName);
            _log($"REST [{_format.Size}] job completed.");
            WriteResponse(ctx, 200, "OK");
        }
        catch (Exception ex)
        {
            _log($"REST [{_format.Size}] print failed: {ex.Message}");
            WriteResponse(ctx, 500, ex.Message);
        }
    }

    private static void WriteResponse(HttpListenerContext ctx, int status, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.Close();
    }

    public void Dispose() => Stop();
}
```

- [ ] **Step 2: Rewrite PrintHostService to run one listener per enabled format**

Replace the entire contents of `PrintHostService.cs` with:

```csharp
using LabelPrinter.Printing;
using LabelPrinter.Services;

namespace LabelPrinter;

public sealed class PrintHostService : IDisposable
{
    private AppConfig _config = null!;
    private PrintModel? _printModel;
    private readonly List<RestPrintListener> _restListeners = new();
    private WebSocketPrintListener? _webSocketListener;

    public event Action<string>? LogMessage;

    public bool IsWebSocketConnected => _webSocketListener?.IsConnected == true;

    public void Start(AppConfig config)
    {
        Stop();
        _config = config;
        _printModel = new PrintModel();
        void Log(string msg) => LogMessage?.Invoke(msg);

        foreach (var format in _config.LabelFormats.Where(f => f.Enabled))
        {
            var listener = new RestPrintListener(format, _config.AllowLanAccess, _printModel, Log);
            listener.Start();
            _restListeners.Add(listener);
        }

        if (_config.EnableWebSocket)
        {
            _webSocketListener = new WebSocketPrintListener(_config, _printModel, Log);
            _webSocketListener.Start();
        }

        var ports = string.Join(", ", _config.LabelFormats.Where(f => f.Enabled).Select(f => $"{f.Size}:{f.Port}"));
        LogMessage?.Invoke($"Running. Ports: {ports}");
    }

    public void Restart(AppConfig config) => Start(config);

    public void Stop()
    {
        if (_webSocketListener != null)
            _webSocketListener.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _webSocketListener = null;

        foreach (var listener in _restListeners)
            listener.Dispose();
        _restListeners.Clear();

        _printModel = null;
    }

    public void Dispose() => Stop();
}
```

- [ ] **Step 3: Build (WebSocket + SettingsForm still broken)**

Run:
```bash
dotnet build "LabelPrinter.csproj"
```
Expected: FAIL — `WebSocketPrintListener` and `SettingsForm` still reference old members. Fixed in Tasks 7–8.

- [ ] **Step 4: Commit (after Task 8 build is green)**

```bash
git add Services/RestPrintListener.cs PrintHostService.cs
git commit -m "feat: run one REST listener per enabled label format"
```

---

## Task 7: WebSocket alias routing

**Files:**
- Modify: `Services/WebSocketPrintListener.cs:126-137`

- [ ] **Step 1: Replace the message-handling block**

In `Services/WebSocketPrintListener.cs`, replace this block:

```csharp
            var message = builder.ToString();
            if (LabelPrintMessageParser.TryParse(message, out var printMsg))
            {
                _log("Received LabelPrint job.");
                _printModel.PrintBarcode(printMsg.EplData, printMsg.PrinterAlias);
                _log("Print job sent to printer.");
            }
            else if (!string.IsNullOrWhiteSpace(message))
            {
                _log($"Ignored message: {Truncate(message, 80)}");
            }
```

with:

```csharp
            var message = builder.ToString();
            if (LabelPrintMessageParser.TryParse(message, out var printMsg))
            {
                var format = _config.FindFormatByAlias(printMsg.PrinterAlias);
                if (format == null)
                {
                    _log($"No enabled label format matches alias '{printMsg.PrinterAlias ?? "(none)"}'. Skipped.");
                }
                else
                {
                    _log($"Received LabelPrint job for {format.Size}.");
                    _printModel.PrintTo(printMsg.EplData, format.PrinterName);
                    _log($"Print job sent to {format.PrinterName}.");
                }
            }
            else if (!string.IsNullOrWhiteSpace(message))
            {
                _log($"Ignored message: {Truncate(message, 80)}");
            }
```

- [ ] **Step 2: Build (SettingsForm still broken)**

Run:
```bash
dotnet build "LabelPrinter.csproj"
```
Expected: FAIL only on `SettingsForm` members. Fixed in Task 8.

- [ ] **Step 3: Commit (after Task 8 build is green)**

```bash
git add Services/WebSocketPrintListener.cs
git commit -m "feat: WebSocket routes by alias to a label format, skips unmatched"
```

---

## Task 8: Settings form — table layout, IP, per-row test, LAN toggle

**Files:**
- Modify: `SettingsForm.Designer.cs` (full rewrite)
- Modify: `SettingsForm.cs` (full rewrite)

- [ ] **Step 1: Rewrite the Designer with static chrome + empty formats table**

Replace the entire contents of `SettingsForm.Designer.cs` with:

```csharp
#nullable disable
namespace LabelPrinter;

partial class SettingsForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        lblHost = new Label();
        lblWsUrl = new Label();
        txtWsUrl = new TextBox();
        chkEnableWebSocket = new CheckBox();
        tlpFormats = new TableLayoutPanel();
        chkRunAtStartup = new CheckBox();
        chkAllowLan = new CheckBox();
        btnSave = new Button();
        lblLog = new Label();
        txtLog = new TextBox();
        SuspendLayout();
        //
        // lblHost
        //
        lblHost.AutoSize = true;
        lblHost.Location = new Point(16, 16);
        lblHost.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        lblHost.Text = "本机地址: ...";
        //
        // lblWsUrl
        //
        lblWsUrl.AutoSize = true;
        lblWsUrl.Location = new Point(16, 46);
        lblWsUrl.Text = "WebSocket:";
        //
        // txtWsUrl
        //
        txtWsUrl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtWsUrl.Location = new Point(110, 42);
        txtWsUrl.Size = new Size(360, 23);
        //
        // chkEnableWebSocket
        //
        chkEnableWebSocket.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        chkEnableWebSocket.AutoSize = true;
        chkEnableWebSocket.Location = new Point(482, 44);
        chkEnableWebSocket.Text = "启用";
        chkEnableWebSocket.CheckedChanged += (_, _) => txtWsUrl.Enabled = chkEnableWebSocket.Checked;
        //
        // tlpFormats
        //
        tlpFormats.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        tlpFormats.Location = new Point(16, 76);
        tlpFormats.Size = new Size(556, 132);
        tlpFormats.ColumnCount = 7;
        tlpFormats.RowCount = 1;
        tlpFormats.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48F));   // default radio
        tlpFormats.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48F));   // size
        tlpFormats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));   // printer
        tlpFormats.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70F));   // type
        tlpFormats.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72F));   // port
        tlpFormats.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52F));   // enabled
        tlpFormats.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64F));   // test
        //
        // chkRunAtStartup
        //
        chkRunAtStartup.AutoSize = true;
        chkRunAtStartup.Location = new Point(16, 220);
        chkRunAtStartup.Text = "开机自启";
        //
        // chkAllowLan
        //
        chkAllowLan.AutoSize = true;
        chkAllowLan.Location = new Point(110, 220);
        chkAllowLan.Text = "允许局域网访问 (需管理员)";
        //
        // btnSave
        //
        btnSave.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnSave.Location = new Point(476, 214);
        btnSave.Size = new Size(96, 28);
        btnSave.Text = "保存并应用";
        btnSave.Click += BtnSave_Click;
        //
        // lblLog
        //
        lblLog.AutoSize = true;
        lblLog.Location = new Point(16, 252);
        lblLog.Text = "Log:";
        //
        // txtLog
        //
        txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        txtLog.Font = new Font("Consolas", 9F);
        txtLog.Location = new Point(16, 272);
        txtLog.Multiline = true;
        txtLog.ReadOnly = true;
        txtLog.ScrollBars = ScrollBars.Vertical;
        txtLog.Size = new Size(556, 150);
        //
        // SettingsForm
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(588, 440);
        Controls.Add(txtLog);
        Controls.Add(lblLog);
        Controls.Add(btnSave);
        Controls.Add(chkAllowLan);
        Controls.Add(chkRunAtStartup);
        Controls.Add(tlpFormats);
        Controls.Add(chkEnableWebSocket);
        Controls.Add(txtWsUrl);
        Controls.Add(lblWsUrl);
        Controls.Add(lblHost);
        MinimumSize = new Size(560, 420);
        Name = "SettingsForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "ControlCode Label Printer - 设置";
        FormClosing += SettingsForm_FormClosing;
        ResumeLayout(false);
        PerformLayout();
    }

    private Label lblHost;
    private Label lblWsUrl;
    private TextBox txtWsUrl;
    private CheckBox chkEnableWebSocket;
    private TableLayoutPanel tlpFormats;
    private CheckBox chkRunAtStartup;
    private CheckBox chkAllowLan;
    private Button btnSave;
    private Label lblLog;
    private TextBox txtLog;
}
```

- [ ] **Step 2: Rewrite the code-behind to build rows and handle test/save**

Replace the entire contents of `SettingsForm.cs` with:

```csharp
using System.Drawing.Printing;
using LabelPrinter.Printing;
using LabelPrinter.Services;

namespace LabelPrinter;

public partial class SettingsForm : Form
{
    private readonly AppConfig _config;
    private readonly PrintHostService _host;
    private readonly List<FormatRow> _rows = new();
    private readonly List<string> _printerChoices = new();

    public event Action<AppConfig>? ConfigSaved;

    public SettingsForm(AppConfig config, PrintHostService host)
    {
        _config = config;
        _host = host;
        InitializeComponent();
        LoadUi();
        _host.LogMessage += AppendLog;
    }

    private void LoadUi()
    {
        lblHost.Text = $"本机地址: {NetworkHelper.GetLocalIPv4()}";

        foreach (string name in PrinterSettings.InstalledPrinters)
            _printerChoices.Add(name);
        _printerChoices.Add("LPT1");
        _printerChoices.Add("LPT2");
        _printerChoices.Add("LPT3");

        txtWsUrl.Text = _config.LabelPrinterUrl;
        chkEnableWebSocket.Checked = _config.EnableWebSocket;
        txtWsUrl.Enabled = chkEnableWebSocket.Checked;
        chkRunAtStartup.Checked = _config.RunAtStartup;
        chkAllowLan.Checked = _config.AllowLanAccess;

        BuildHeaderRow();
        foreach (var format in _config.LabelFormats)
            AddFormatRow(format);
    }

    private void BuildHeaderRow()
    {
        string[] headers = { "默认", "尺寸", "打印机", "类型", "端口", "启用", "" };
        for (var col = 0; col < headers.Length; col++)
        {
            var lbl = new Label
            {
                Text = headers[col],
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(3, 3, 3, 3)
            };
            tlpFormats.Controls.Add(lbl, col, 0);
        }
    }

    private void AddFormatRow(LabelFormat format)
    {
        var rowIndex = tlpFormats.RowCount;
        tlpFormats.RowCount = rowIndex + 1;
        tlpFormats.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));

        var rdoDefault = new RadioButton { AutoSize = true, Checked = format.IsDefault, Anchor = AnchorStyles.Left };
        var lblSize = new Label { Text = format.Size, AutoSize = true, Anchor = AnchorStyles.Left };

        var cboPrinter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left | AnchorStyles.Right, Width = 200 };
        foreach (var choice in _printerChoices)
            cboPrinter.Items.Add(choice);
        var idx = cboPrinter.Items.IndexOf(format.PrinterName);
        if (idx < 0 && !string.IsNullOrEmpty(format.PrinterName))
            idx = cboPrinter.Items.Add(format.PrinterName); // keep an unknown/offline printer selectable
        cboPrinter.SelectedIndex = idx >= 0 ? idx : (cboPrinter.Items.Count > 0 ? 0 : -1);

        var cboType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left, Width = 64 };
        cboType.Items.AddRange(new object[] { "EPL", "ZPL", "文本" });
        cboType.SelectedIndex = (int)format.PrintType;

        var numPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = Math.Clamp(format.Port, 1, 65535), Anchor = AnchorStyles.Left, Width = 66 };

        var chkEnabled = new CheckBox { Checked = format.Enabled, AutoSize = true, Anchor = AnchorStyles.Left };

        var btnTest = new Button { Text = "测试", Anchor = AnchorStyles.Left, Width = 56 };

        var row = new FormatRow(format.Size, rdoDefault, lblSize, cboPrinter, cboType, numPort, chkEnabled, btnTest);
        btnTest.Click += (_, _) => TestRow(row);
        _rows.Add(row);

        tlpFormats.Controls.Add(rdoDefault, 0, rowIndex);
        tlpFormats.Controls.Add(lblSize, 1, rowIndex);
        tlpFormats.Controls.Add(cboPrinter, 2, rowIndex);
        tlpFormats.Controls.Add(cboType, 3, rowIndex);
        tlpFormats.Controls.Add(numPort, 4, rowIndex);
        tlpFormats.Controls.Add(chkEnabled, 5, rowIndex);
        tlpFormats.Controls.Add(btnTest, 6, rowIndex);
    }

    private void TestRow(FormatRow row)
    {
        ApplyUiToConfig();
        var printerName = (string?)row.Printer.SelectedItem ?? "";
        if (string.IsNullOrWhiteSpace(printerName))
        {
            MessageBox.Show(this, "请先为该尺寸选择打印机。", "Label Printer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var type = (LabelPrintType)row.Type.SelectedIndex;
        var sample = SampleLabelGenerator.Generate(type, row.Size);
        try
        {
            new PrintModel().PrintTo(sample, printerName);
            AppendLog($"Test [{row.Size}/{type}] sent to {printerName}.");
        }
        catch (Exception ex)
        {
            AppendLog($"Test [{row.Size}] failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Print Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        ApplyUiToConfig();
        ConfigSaved?.Invoke(_config);
        AppendLog("Settings saved.");
        MessageBox.Show(this, "已保存并重新连接。", "Label Printer", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ApplyUiToConfig()
    {
        _config.LabelPrinterUrl = txtWsUrl.Text.Trim();
        _config.EnableWebSocket = chkEnableWebSocket.Checked;
        _config.RunAtStartup = chkRunAtStartup.Checked;
        _config.AllowLanAccess = chkAllowLan.Checked;

        foreach (var row in _rows)
        {
            var format = _config.LabelFormats.First(f => f.Size == row.Size);
            format.PrinterName = (string?)row.Printer.SelectedItem ?? "";
            format.PrintType = (LabelPrintType)row.Type.SelectedIndex;
            format.Port = (int)row.Port.Value;
            format.Enabled = row.Enabled.Checked;
            format.IsDefault = row.Default.Checked;
        }
    }

    private void AppendLog(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(message));
            return;
        }

        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void SettingsForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _host.LogMessage -= AppendLog;
        base.OnFormClosed(e);
    }

    private sealed record FormatRow(
        string Size,
        RadioButton Default,
        Label SizeLabel,
        ComboBox Printer,
        ComboBox Type,
        NumericUpDown Port,
        CheckBox Enabled,
        Button Test);
}
```

Note: All `RadioButton`s are children of `tlpFormats`, so WinForms auto-groups them — checking one default unchecks the others.

- [ ] **Step 3: Build the whole solution — should be green now**

Run:
```bash
dotnet build "LabelPrinter.csproj"
```
Expected: Build succeeded (Tasks 5–8 callers all updated).

- [ ] **Step 4: Run the full test suite**

Run:
```bash
dotnet test "LabelPrinter.Tests/LabelPrinter.Tests.csproj"
```
Expected: PASS — 10 tests (6 AppConfig + 3 SampleLabelGenerator + 1 NetworkHelper).

- [ ] **Step 5: Commit everything from Tasks 5–8**

```bash
git add Printing/PrintModel.cs Services/RestPrintListener.cs PrintHostService.cs Services/WebSocketPrintListener.cs SettingsForm.cs SettingsForm.Designer.cs
git commit -m "feat: settings form table layout with per-size test, IP, LAN toggle"
```

---

## Task 9: Update docs and sample config

**Files:**
- Modify: `appsettings.json`
- Modify: `README.md`

- [ ] **Step 1: Rewrite appsettings.json to the new schema**

Replace the entire contents of `appsettings.json` with:

```json
{
  "LabelPrinter": {
    "LabelPrinterUrl": "ws://your-rma-host:2012/websocket",
    "EnableWebSocket": true,
    "AllowLanAccess": false,
    "ReconnectDelaySeconds": 5,
    "WebSocketConnectTimeoutSeconds": 10,
    "RunAtStartup": false,
    "LabelFormats": [
      { "Size": "4x2", "Alias": "4x2", "PrinterName": "", "PrintType": "Epl", "Port": 48210, "Enabled": true, "IsDefault": false },
      { "Size": "4x3", "Alias": "4x3", "PrinterName": "", "PrintType": "Epl", "Port": 48211, "Enabled": true, "IsDefault": false },
      { "Size": "4x6", "Alias": "4x6", "PrinterName": "", "PrintType": "Epl", "Port": 48212, "Enabled": true, "IsDefault": true }
    ]
  }
}
```

- [ ] **Step 2: Update README.md**

In `README.md`, update the **功能**, **配置**, and **消息格式** sections to describe:
- Three fixed sizes (4×2 / 4×3 / 4×6), each with its own printer, port, print type, enable flag; one marked default (UI highlight only).
- REST: `POST http://<host>:<port>/LabelPrint`, where the port selects the size/printer. List default ports 48210 / 48211 / 48212.
- `AllowLanAccess`: bind `http://+:<port>/` (needs admin / urlacl) vs localhost-only.
- WebSocket: `LabelPrint|<alias>|<data>`; alias must match a format's `Alias` or the job is skipped.
- Print type (EPL / ZPL / 文本) is per size and only affects the built-in test sample.
- Note the settings UI shows the machine's local IP.

Replace the old config table's rows (`PrinterName`, `PrinterAlias`, `UseLptPrinter`, `LptPort`, `RestListenPrefix`, `EnableRestEndpoint`) with `LabelFormats` (and its sub-fields) and `AllowLanAccess`. Keep `LabelPrinterUrl`, `EnableWebSocket`, `ReconnectDelaySeconds`, `WebSocketConnectTimeoutSeconds`, `RunAtStartup`.

- [ ] **Step 3: Build to confirm appsettings.json still copies**

Run:
```bash
dotnet build "LabelPrinter.csproj"
```
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add appsettings.json README.md
git commit -m "docs: update README and appsettings for multi-format schema"
```

---

## Task 10: Manual verification

**Files:** none (manual QA).

- [ ] **Step 1: Run the app**

Run:
```bash
dotnet build "LabelPrinter.csproj" -c Release
```
Then launch `bin/Release/net8.0-windows/LabelPrinter.exe`, open settings from the tray.

- [ ] **Step 2: Verify UI**

Confirm: host IP shows at top; three rows (4×2/4×3/4×6) with printer dropdown (Windows printers + LPT1–3), type dropdown, port (defaults 48210/48211/48212), enable, and a 测试 button; exactly one default radio selectable.

- [ ] **Step 3: Verify per-size test**

Pick a real printer for one row, click 测试 — a sample label of that size/type prints; log shows success.

- [ ] **Step 4: Verify per-port REST routing**

With that row's port (e.g. 48212), run:
```bash
curl -X POST "http://localhost:48212/LabelPrint" -H "Content-Type: text/plain" --data-binary $'N\nA20,20,0,4,1,1,N,"REST"\nP1\n'
```
Expected: 200 OK; label prints on that row's printer.

- [ ] **Step 5: Verify WebSocket skip-on-unmatched**

Send a `LabelPrint|nosuchalias|...` message from the RMA server (or a test WS server); confirm the log shows "No enabled label format matches alias 'nosuchalias'. Skipped." and nothing prints. Then send `LabelPrint|4x6|...` and confirm it prints to the 4×6 printer.

- [ ] **Step 6: Verify legacy migration**

Temporarily put an old-style `appsettings.json` (with `PrinterName` and no `LabelFormats`) next to the exe, launch, open settings — confirm three formats appear and the old printer landed on the 4×6 row. (Restore the new config afterward.)

- [ ] **Step 7: Commit any doc fixes discovered during QA**

```bash
git add -A
git commit -m "docs: QA fixes"   # only if changes were needed
```

---

## Notes for the implementer

- **Build order matters:** Tasks 2 and 5–8 together remove and replace the old single-printer API. The project will not compile between the start of Task 5 and the end of Task 8. Do those tasks as one batch; run the full `dotnet test` at the end of Task 8. Tasks 3 and 4 add isolated new files and their tests can be reasoned about independently, but the whole-solution test run only goes green after Task 8.
- **No solution file:** build/test with explicit project paths as shown. If someone later adds a `.sln`, run `dotnet sln add LabelPrinter.csproj LabelPrinter.Tests/LabelPrinter.Tests.csproj`.
- **Verbatim forwarding preserved:** real REST/WebSocket jobs are still sent byte-for-byte; only the test buttons generate content.
