using System.Drawing.Printing;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using Microsoft.Win32;

namespace LabelPrinter.Services;

public enum CheckStatus { Ok, Info, Warn, Fail }

/// <summary>A one-click fix the UI can offer for a result.</summary>
public enum CheckAction { None, AllowBrowser }

public sealed record CheckResult(
    CheckStatus Status, string Title, string Detail, string? Solution = null, CheckAction Action = CheckAction.None)
{
    public string ToLogText()
    {
        var tag = Status switch
        {
            CheckStatus.Ok => "[ OK ]",
            CheckStatus.Info => "[INFO]",
            CheckStatus.Warn => "[WARN]",
            _ => "[FAIL]"
        };
        var sb = new StringBuilder();
        sb.Append(tag).Append(' ').Append(Title).Append(": ").Append(Detail);
        if (!string.IsNullOrWhiteSpace(Solution))
        {
            var lines = Solution.Split('\n');
            sb.Append("\r\n       -> ").Append(L.T("sc.solution")).Append(": ").Append(lines[0].TrimEnd('\r'));
            for (var i = 1; i < lines.Length; i++)
                sb.Append("\r\n          ").Append(lines[i].TrimEnd('\r'));
        }

        return sb.ToString();
    }
}

/// <summary>Pure decision rules behind the self-check, kept free of I/O so they are unit-testable.</summary>
public static class MzlSelfCheckRules
{
    public const string ClodopHost = "localhost.lodop.net";
    public static readonly int[] ExpectedPorts = { 8000, 18000, 8443, 8444 };

    // Chrome 142 introduced the Local Network Access permission; older versions don't block this.
    public const int LocalNetworkAccessMajor = 142;

    public static bool AllowListCoversMzl(IEnumerable<string> urls) =>
        urls.Any(u => u.Contains("shipswithus.com", StringComparison.OrdinalIgnoreCase));

    public static int? ParseMajorVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;
        var head = version.Trim().Split('.')[0];
        return int.TryParse(head, out var major) ? major : null;
    }

    public static bool ResolvesToLoopback(IEnumerable<IPAddress> addresses) =>
        addresses.Any(a => IPAddress.IsLoopback(a));

    public static CheckResult EvaluateService(IReadOnlyCollection<int> boundPorts)
    {
        var title = L.T("sc.svc.title");
        if (boundPorts.Count == 0)
            return new CheckResult(CheckStatus.Fail, title, L.T("sc.svc.none"), L.T("sc.svc.none.fix"));

        var missing = ExpectedPorts.Where(p => !boundPorts.Contains(p)).ToList();
        if (missing.Count == 0)
            return new CheckResult(CheckStatus.Ok, title, L.T("sc.svc.ok"));

        return new CheckResult(
            CheckStatus.Warn, title,
            L.F("sc.svc.partial", string.Join("/", missing)),
            L.T("sc.svc.partial.fix"));
    }

    public static CheckResult EvaluatePrinter(string? printerName, IEnumerable<string> installed)
    {
        var title = L.T("sc.prn.title");
        if (string.IsNullOrWhiteSpace(printerName))
            return new CheckResult(CheckStatus.Fail, title, L.T("sc.prn.empty"), L.T("sc.prn.empty.fix"));

        var found = installed.Any(p => string.Equals(p, printerName, StringComparison.OrdinalIgnoreCase));
        return found
            ? new CheckResult(CheckStatus.Ok, title, L.F("sc.prn.ok", printerName))
            : new CheckResult(CheckStatus.Fail, title, L.F("sc.prn.missing", printerName), L.T("sc.prn.missing.fix"));
    }

    public static CheckResult EvaluateDns(IReadOnlyCollection<IPAddress>? addresses, string? error)
    {
        var title = L.T("sc.dns.title");
        if (addresses is null || addresses.Count == 0)
            return new CheckResult(CheckStatus.Fail, title, L.F("sc.dns.fail", error ?? "?"), L.T("sc.dns.fix"));

        var text = string.Join(", ", addresses.Select(a => a.ToString()));
        return ResolvesToLoopback(addresses)
            ? new CheckResult(CheckStatus.Ok, title, L.F("sc.dns.ok", text))
            : new CheckResult(CheckStatus.Fail, title, L.F("sc.dns.wrong", text), L.T("sc.dns.fix"));
    }

    public static CheckResult EvaluateProxy(string? proxyAddress, string? error)
    {
        var title = L.T("sc.proxy.title");
        if (error is not null)
            return new CheckResult(CheckStatus.Info, title, L.F("sc.proxy.unknown", error));
        if (proxyAddress is null)
            return new CheckResult(CheckStatus.Ok, title, L.T("sc.proxy.ok"));

        return new CheckResult(CheckStatus.Warn, title, L.F("sc.proxy.warn", proxyAddress), L.T("sc.proxy.fix"));
    }

    /// <param name="scriptCommand">Only called for the warning, because building it writes a script file.</param>
    public static CheckResult EvaluateBrowser(
        string name, string? version, bool policyCoversMzl, Func<string> scriptCommand)
    {
        var title = L.F("sc.br.title", name, version ?? "?");
        if (policyCoversMzl)
            return new CheckResult(CheckStatus.Ok, title, L.T("sc.br.ok"));

        var major = ParseMajorVersion(version);
        if (major is int m && m < LocalNetworkAccessMajor)
            return new CheckResult(CheckStatus.Info, title, L.T("sc.br.old"));

        return new CheckResult(
            CheckStatus.Warn, title, L.T("sc.br.warn"), L.F("sc.br.warn.fix", scriptCommand()),
            CheckAction.AllowBrowser);
    }

    /// <summary>
    /// A real C-Lodop on the same PC grabs 8000/18000/8443/8444 (often at login, before this app),
    /// so MZL ends up talking to it instead of LabelPrinter.
    /// </summary>
    public static CheckResult EvaluateOldClodop(
        IReadOnlyCollection<string> runningProcesses,
        IReadOnlyCollection<string> installedNames,
        IReadOnlyCollection<string> autoStartEntries)
    {
        var title = L.T("sc.old.title");
        var names = string.Join(", ", installedNames.Concat(runningProcesses).Distinct(StringComparer.OrdinalIgnoreCase));

        if (runningProcesses.Count > 0)
            return new CheckResult(CheckStatus.Fail, title, L.F("sc.old.running", names), L.T("sc.old.fix"));

        if (installedNames.Count > 0 || autoStartEntries.Count > 0)
            return new CheckResult(
                CheckStatus.Warn, title,
                L.F("sc.old.installed", string.IsNullOrEmpty(names) ? string.Join(", ", autoStartEntries) : names),
                L.T("sc.old.fix"));

        return new CheckResult(CheckStatus.Ok, title, L.T("sc.old.none"));
    }

    /// <summary>The program's file name from a Run-key command line (quoted or not, with or without arguments).</summary>
    public static string? ExecutableFileName(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        var text = command.Trim();
        if (text[0] == '"')
        {
            var end = text.IndexOf('"', 1);
            text = end > 0 ? text[1..end] : text[1..];
        }
        else
        {
            // Unquoted paths may contain spaces, so cut at the first ".exe" rather than the first space.
            var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe >= 0)
                text = text[..(exe + 4)];
        }

        return Path.GetFileName(text);
    }

    public static bool IsClodopName(string? text) =>
        !string.IsNullOrEmpty(text) && text.Contains("lodop", StringComparison.OrdinalIgnoreCase);

    public static CheckResult EvaluateRequestStats(LodopRequestSnapshot? stats)
    {
        var title = L.T("sc.req.title");
        if (stats is null || (stats.ScriptLoads == 0 && stats.Preflights == 0 && stats.PrintPosts == 0))
            return new CheckResult(CheckStatus.Warn, title, L.T("sc.req.none"), L.T("sc.req.none.fix"));

        if (stats.PrintPosts == 0)
            return new CheckResult(
                CheckStatus.Warn, title,
                L.F("sc.req.scriptOnly", stats.ScriptLoads, stats.Preflights),
                L.T("sc.req.scriptOnly.fix"));

        return new CheckResult(
            CheckStatus.Ok, title,
            L.F("sc.req.ok", stats.PrintPosts,
                stats.LastLocalTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-",
                stats.LastSource ?? "-"));
    }
}

/// <summary>
/// Walks the failure points that stop MZL (browser page) from printing through the Lodop-compat
/// shim, one at a time, reporting each as it finishes. Read-only: it never prints a page.
/// </summary>
public sealed class MzlSelfCheck
{
    private readonly PrintHostService _host;
    private readonly Func<string?> _printerName;

    public MzlSelfCheck(PrintHostService host, Func<string?> printerName)
    {
        _host = host;
        _printerName = printerName;
    }

    /// <summary>
    /// Everything runs on a pool thread: the process list, the Uninstall-registry walk and the
    /// installed-printer list can each take seconds. The printer name is a UI control value, so it
    /// is read here, before leaving the UI thread. <paramref name="report"/> may be called from any thread.
    /// </summary>
    public Task<IReadOnlyList<CheckResult>> RunAsync(Action<CheckResult> report)
    {
        var printerName = _printerName();
        return Task.Run(() => RunChecksAsync(printerName, report));
    }

    private async Task<IReadOnlyList<CheckResult>> RunChecksAsync(string? printerName, Action<CheckResult> report)
    {
        var results = new List<CheckResult>();

        void Add(CheckResult r)
        {
            results.Add(r);
            report(r);
        }

        Add(MzlSelfCheckRules.EvaluateService(_host.LodopBoundPorts));
        Add(MzlSelfCheckRules.EvaluateOldClodop(
            FindClodopProcesses(), FindInstalledClodop(), FindClodopAutoStart()));
        Add(MzlSelfCheckRules.EvaluatePrinter(printerName, InstalledPrinters()));
        Add(await CheckHttpScriptAsync().ConfigureAwait(false));
        Add(await CheckDnsAsync().ConfigureAwait(false));
        Add(CheckProxy());
        Add(await CheckTlsAsync().ConfigureAwait(false));

        foreach (var r in CheckBrowsers())
            Add(r);

        Add(CheckPolicyWritable());
        Add(MzlSelfCheckRules.EvaluateRequestStats(_host.LodopRequestStats));
        return results;
    }

    private static List<string> FindClodopProcesses()
    {
        var found = new List<string>();
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.ProcessName.StartsWith("CLodop", StringComparison.OrdinalIgnoreCase))
                        found.Add(p.ProcessName + ".exe");
                }
                catch
                {
                    // process exited while enumerating
                }
            }
        }

        return found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> FindInstalledClodop()
    {
        var found = new List<string>();
        var roots = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var root in roots)
            {
                try
                {
                    using var key = hive.OpenSubKey(root);
                    if (key is null)
                        continue;
                    foreach (var sub in key.GetSubKeyNames())
                    {
                        using var app = key.OpenSubKey(sub);
                        if (app?.GetValue("DisplayName") is string name && MzlSelfCheckRules.IsClodopName(name))
                            found.Add(name);
                    }
                }
                catch
                {
                    // unreadable uninstall list: treat as nothing found
                }
            }
        }

        return found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> FindClodopAutoStart()
    {
        var found = new List<string>();
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            try
            {
                using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                if (key is null)
                    continue;
                foreach (var name in key.GetValueNames())
                {
                    // Match the entry name or the program's file name, never the whole command: a folder
                    // called "...Lodop..." must not make an unrelated autostart entry look like C-Lodop.
                    if (MzlSelfCheckRules.IsClodopName(name)
                        || MzlSelfCheckRules.IsClodopName(MzlSelfCheckRules.ExecutableFileName(key.GetValue(name) as string)))
                        found.Add(name);
                }
            }
            catch
            {
                // unreadable Run key: treat as nothing found
            }
        }

        return found;
    }

    /// <summary>True when a Chrome or Edge allow-list under HKLM/HKCU already covers MZL.</summary>
    public static bool BrowserPolicyCoversMzl() =>
        MzlSelfCheckRules.AllowListCoversMzl(ReadAllowList(@"SOFTWARE\Policies\Google\Chrome"))
        || MzlSelfCheckRules.AllowListCoversMzl(ReadAllowList(@"SOFTWARE\Policies\Microsoft\Edge"));

    private static IEnumerable<string> InstalledPrinters() =>
        PrinterSettings.InstalledPrinters.Cast<string>();

    private static async Task<CheckResult> CheckHttpScriptAsync()
    {
        var title = L.T("sc.http.title");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var ok = new List<int>();
        var foreign = new List<int>();
        var down = new List<int>();

        foreach (var port in new[] { 8000, 18000 })
        {
            try
            {
                var js = await http.GetStringAsync($"http://localhost:{port}/CLodopfuncs.js").ConfigureAwait(false);
                (LodopCompatListener.LooksLikeOurClodopFuncsJs(js) ? ok : foreign).Add(port);
            }
            catch
            {
                down.Add(port);
            }
        }

        if (foreign.Count > 0)
            return new CheckResult(
                CheckStatus.Fail, title,
                L.F("sc.http.foreign", string.Join("/", foreign)), L.T("sc.http.foreign.fix"));
        if (ok.Count == 0)
            return new CheckResult(
                CheckStatus.Fail, title,
                L.F("sc.http.down", string.Join("/", down)), L.T("sc.http.down.fix"));

        return new CheckResult(CheckStatus.Ok, title, L.F("sc.http.ok", string.Join("/", ok)));
    }

    private static async Task<CheckResult> CheckDnsAsync()
    {
        try
        {
            var task = Dns.GetHostAddressesAsync(MzlSelfCheckRules.ClodopHost);
            if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(6))).ConfigureAwait(false) != task)
                return MzlSelfCheckRules.EvaluateDns(null, "timeout");
            return MzlSelfCheckRules.EvaluateDns(await task.ConfigureAwait(false), null);
        }
        catch (Exception ex)
        {
            return MzlSelfCheckRules.EvaluateDns(null, ex.Message);
        }
    }

    /// <summary>
    /// Browsers only skip the proxy for localhost / 127.0.0.1 themselves, not for a name that merely
    /// resolves to 127.0.0.1, so a system proxy or PAC rule can swallow MZL's https requests.
    /// </summary>
    private static CheckResult CheckProxy()
    {
        try
        {
            var target = new Uri($"https://{MzlSelfCheckRules.ClodopHost}:8443/");
            var proxy = HttpClient.DefaultProxy;
            var via = proxy.IsBypassed(target) ? null : proxy.GetProxy(target);
            var proxied = via is not null && via != target;
            return MzlSelfCheckRules.EvaluateProxy(proxied ? via!.ToString() : null, null);
        }
        catch (Exception ex)
        {
            return MzlSelfCheckRules.EvaluateProxy(null, ex.Message);
        }
    }

    /// <summary>
    /// Connects straight to 127.0.0.1 but keeps localhost.lodop.net as the TLS host name, so this
    /// tests certificate trust on its own, independent of whether DNS resolves.
    /// </summary>
    private static async Task<CheckResult> CheckTlsAsync()
    {
        var title = L.T("sc.tls.title");
        var handler = new SocketsHttpHandler
        {
            // localhost.lodop.net is not a loopback name, so a system proxy would otherwise be used and
            // ConnectCallback would be asked to dial the proxy's port instead of ours.
            UseProxy = false,
            ConnectCallback = async (ctx, ct) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, ctx.DnsEndPoint.Port), ct)
                        .ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };

        Exception? firstError = null;
        foreach (var port in new[] { 8443, 8444 })
        {
            try
            {
                var js = await http.GetStringAsync($"https://{MzlSelfCheckRules.ClodopHost}:{port}/CLodopfuncs.js")
                    .ConfigureAwait(false);
                if (LodopCompatListener.LooksLikeOurClodopFuncsJs(js))
                    return new CheckResult(CheckStatus.Ok, title, L.F("sc.tls.ok", port));
            }
            catch (Exception ex)
            {
                firstError ??= ex;
            }
        }

        if (firstError is not null && HasInner<AuthenticationException>(firstError))
            return new CheckResult(
                CheckStatus.Fail, title,
                L.F("sc.tls.untrusted", InnermostMessage(firstError)),
                L.F("sc.tls.untrusted.fix", Environment.UserDomainName + "\\" + Environment.UserName));

        return new CheckResult(
            CheckStatus.Fail, title,
            L.F("sc.tls.down", firstError is null ? "?" : InnermostMessage(firstError)),
            L.T("sc.tls.down.fix"));
    }

    private IEnumerable<CheckResult> CheckBrowsers()
    {
        Func<string> scriptCommand = () => FixScript.TryWrite() ?? "allow-mzl-browser.ps1";
        var found = false;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var (name, policyPath, beaconPaths, userData) in new[]
                 {
                     ("Chrome", @"SOFTWARE\Policies\Google\Chrome", new[] { @"Software\Google\Chrome\BLBeacon", @"SOFTWARE\Google\Chrome\BLBeacon" },
                         Path.Combine(localAppData, "Google", "Chrome", "User Data")),
                     ("Edge", @"SOFTWARE\Policies\Microsoft\Edge", new[] { @"Software\Microsoft\Edge\BLBeacon", @"SOFTWARE\Microsoft\Edge\BLBeacon" },
                         Path.Combine(localAppData, "Microsoft", "Edge", "User Data"))
                 })
        {
            var version = ReadBeaconVersion(beaconPaths);
            if (version is null)
                continue;
            found = true;

            var policyCovers = MzlSelfCheckRules.AllowListCoversMzl(ReadAllowList(policyPath));
            yield return MzlSelfCheckRules.EvaluateBrowser(name, version, policyCovers, scriptCommand);

            // A policy overrides the per-site choice, so the site setting only matters without one.
            var major = MzlSelfCheckRules.ParseMajorVersion(version);
            if (!policyCovers && (major is null || major >= MzlSelfCheckRules.LocalNetworkAccessMajor))
                yield return BrowserSitePermissions.Evaluate(name, BrowserSitePermissions.ReadAllProfiles(userData));
        }

        if (!found)
            yield return new CheckResult(CheckStatus.Info, L.T("sc.br.none.title"), L.T("sc.br.none"));
    }

    private static string? ReadBeaconVersion(string[] paths)
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var path in paths)
            {
                try
                {
                    using var key = hive.OpenSubKey(path);
                    if (key?.GetValue("version") is string v && !string.IsNullOrWhiteSpace(v))
                        return v;
                }
                catch
                {
                    // unreadable hive/key: treat as not installed
                }
            }
        }

        return null;
    }

    private static List<string> ReadAllowList(string policyRoot)
    {
        var urls = new List<string>();
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            try
            {
                using var key = hive.OpenSubKey($@"{policyRoot}\LocalNetworkAccessAllowedForUrls");
                if (key is null)
                    continue;
                foreach (var name in key.GetValueNames())
                {
                    if (key.GetValue(name) is string s)
                        urls.Add(s);
                }
            }
            catch
            {
                // policy key unreadable: treat as not configured
            }
        }

        return urls;
    }

    private static CheckResult CheckPolicyWritable()
    {
        var title = L.T("sc.pol.title");
        var canWrite = CanWriteUnderHkcu(@"Software\Policies\Google\Chrome")
            && CanWriteUnderHkcu(@"Software\Policies\Microsoft\Edge");

        return canWrite
            ? new CheckResult(CheckStatus.Ok, title, L.T("sc.pol.ok"))
            : new CheckResult(CheckStatus.Info, title, L.T("sc.pol.denied"), L.T("sc.pol.denied.fix"));
    }

    /// <summary>
    /// Opens the key (or, if it doesn't exist yet, its nearest existing parent) for writing to learn
    /// whether the app could create it, without creating anything: a diagnostic must not change the registry.
    /// </summary>
    private static bool CanWriteUnderHkcu(string path)
    {
        var current = path;
        while (true)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(current, writable: true);
                if (key is not null)
                    return true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                return false;
            }

            var cut = current.LastIndexOf('\\');
            if (cut < 0)
                return true;
            current = current[..cut];
        }
    }

    private static bool HasInner<T>(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is T)
                return true;
        }

        return false;
    }

    private static string InnermostMessage(Exception ex)
    {
        var e = ex;
        while (e.InnerException is not null)
            e = e.InnerException;
        return e.Message;
    }
}

/// <summary>Writes the admin PowerShell script that adds MZL's domains to the browsers' allow list.</summary>
public static class FixScript
{
    /// <summary>Built from the same origin and browser lists the app's own startup policy uses.</summary>
    public static string Content => BuildContent(BrowserLocalAccessPolicy.AllowedOrigins, BrowserLocalAccessPolicy.BrowserPolicyRoots);

    private static string BuildContent(IEnumerable<string> origins, IEnumerable<string> policyRoots)
    {
        var urls = string.Join(",", origins.Select(o => $"\"{o}\""));
        var roots = string.Join(",", policyRoots.Select(r => $"\"HKLM:\\{r}\""));
        return $$"""
        $ErrorActionPreference = 'Stop'
        try {
          $urls = {{urls}}
          foreach ($root in {{roots}}) {
            $key = "$root\LocalNetworkAccessAllowedForUrls"
            if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
            $names = @((Get-ItemProperty -Path $key).PSObject.Properties | Where-Object { $_.Name -notlike 'PS*' })
            $existing = @($names | ForEach-Object { $_.Value })
            $next = 1 + (($names | ForEach-Object { $_.Name -as [int] } | Measure-Object -Maximum).Maximum)
            foreach ($u in $urls) {
              if ($existing -notcontains $u) {
                New-ItemProperty -Path $key -Name $next -Value $u -PropertyType String -Force | Out-Null
                $next++
              }
            }
          }
          Write-Host "Done. Fully close and reopen Chrome/Edge, then check chrome://policy."
          Start-Sleep -Seconds 4
        } catch {
          Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
          Start-Sleep -Seconds 10
          exit 1
        }
        """;
    }

    /// <summary>Writes the script next to the logs and returns its path, or null if that fails.</summary>
    public static string? TryWritePath()
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "allow-mzl-browser.ps1");
            File.WriteAllText(path, Content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return path;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Returns the command to run (script path included), or null if it can't be written.</summary>
    public static string? TryWrite() =>
        TryWritePath() is { } path ? $"powershell -ExecutionPolicy Bypass -File \"{path}\"" : null;

    /// <summary>
    /// The script as a PowerShell -EncodedCommand value. The one-click fix runs elevated, so it must
    /// not read the script from a user-writable file (another process could swap it before it runs);
    /// the content travels on the command line instead. Blank lines and indentation are dropped to
    /// keep the argument well under what ShellExecute accepts (about 4 KB was verified to work).
    /// </summary>
    public static string EncodedCommand()
    {
        var compact = string.Join("\n", Content.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
        return Convert.ToBase64String(Encoding.Unicode.GetBytes(compact));
    }
}

public sealed record FixOutcome(bool Cancelled, string? StartError, int? ExitCode, bool PolicyNowPresent);

/// <summary>Runs the allow-list script elevated (UAC prompt) so the app itself never needs admin rights.</summary>
public static class AllowBrowserFix
{
    private const int ErrorCancelled = 1223; // user declined the UAC prompt

    /// <summary>
    /// Process.Start with the 'runas' verb blocks until the user answers the UAC prompt, so the whole
    /// thing runs on a pool thread instead of freezing the settings window.
    /// </summary>
    public static Task<FixOutcome> RunAsync() => Task.Run(RunElevated);

    private static FixOutcome RunElevated()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(
                "powershell.exe", $"-NoProfile -EncodedCommand {FixScript.EncodedCommand()}")
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null)
                return new FixOutcome(false, "PowerShell did not start", null, false);

            var exited = process.WaitForExit(60_000);
            return new FixOutcome(false, null, exited ? process.ExitCode : null, MzlSelfCheck.BrowserPolicyCoversMzl());
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return new FixOutcome(true, null, null, false);
        }
        catch (Exception ex)
        {
            return new FixOutcome(false, ex.Message, null, false);
        }
    }

    public static CheckResult ToResult(FixOutcome outcome)
    {
        var title = L.T("sc.fix.title");
        if (outcome.Cancelled)
            return new CheckResult(CheckStatus.Info, title, L.T("sc.fix.cancelled"));
        if (outcome.StartError is not null)
            return new CheckResult(CheckStatus.Fail, title, L.F("sc.fix.startFailed", outcome.StartError), L.T("sc.fix.itPolicy"));
        if (outcome.ExitCode == 0 && outcome.PolicyNowPresent)
            return new CheckResult(CheckStatus.Ok, title, L.T("sc.fix.done"));

        return new CheckResult(
            CheckStatus.Fail, title,
            L.F("sc.fix.notApplied", outcome.ExitCode?.ToString() ?? "timeout"),
            L.T("sc.fix.itPolicy"));
    }
}
