using System.Net;
using LabelPrinter.Services;
using Xunit;

namespace LabelPrinter.Tests;

public class MzlSelfCheckRulesTests
{
    [Theory]
    [InlineData("156.0.8078.73", 156)]
    [InlineData("142", 142)]
    [InlineData(" 99.1 ", 99)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("abc", null)]
    public void ParseMajorVersion_reads_the_leading_number(string? input, int? expected) =>
        Assert.Equal(expected, MzlSelfCheckRules.ParseMajorVersion(input));

    [Fact]
    public void AllowListCoversMzl_matches_any_shipswithus_entry()
    {
        Assert.True(MzlSelfCheckRules.AllowListCoversMzl(new[] { "https://netorg.sharepoint.com", "https://[*.]ShipsWithUs.com" }));
        Assert.False(MzlSelfCheckRules.AllowListCoversMzl(new[] { "https://netorg.sharepoint.com" }));
        Assert.False(MzlSelfCheckRules.AllowListCoversMzl(Array.Empty<string>()));
    }

    [Fact]
    public void EvaluateService_fails_when_nothing_is_bound()
    {
        var r = MzlSelfCheckRules.EvaluateService(Array.Empty<int>());
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.False(string.IsNullOrWhiteSpace(r.Solution));
    }

    [Fact]
    public void EvaluateService_warns_and_names_the_missing_ports()
    {
        var r = MzlSelfCheckRules.EvaluateService(new[] { 8000, 18000 });
        Assert.Equal(CheckStatus.Warn, r.Status);
        Assert.Contains("8443", r.Detail);
        Assert.Contains("8444", r.Detail);
    }

    [Fact]
    public void EvaluateService_is_ok_when_all_four_ports_are_bound()
    {
        var r = MzlSelfCheckRules.EvaluateService(new[] { 8000, 18000, 8443, 8444 });
        Assert.Equal(CheckStatus.Ok, r.Status);
        Assert.Null(r.Solution);
    }

    [Fact]
    public void EvaluatePrinter_covers_empty_missing_and_installed()
    {
        var installed = new[] { "BIXOLON XD3-40d - BPL-Z" };

        Assert.Equal(CheckStatus.Fail, MzlSelfCheckRules.EvaluatePrinter("", installed).Status);
        Assert.Equal(CheckStatus.Fail, MzlSelfCheckRules.EvaluatePrinter(null, installed).Status);
        Assert.Equal(CheckStatus.Fail, MzlSelfCheckRules.EvaluatePrinter("Other", installed).Status);
        Assert.Equal(CheckStatus.Ok, MzlSelfCheckRules.EvaluatePrinter("bixolon xd3-40d - bpl-z", installed).Status);
    }

    [Fact]
    public void EvaluateDns_distinguishes_loopback_wrong_address_and_failure()
    {
        Assert.Equal(CheckStatus.Ok, MzlSelfCheckRules.EvaluateDns(new[] { IPAddress.Loopback }, null).Status);

        var wrong = MzlSelfCheckRules.EvaluateDns(new[] { IPAddress.Parse("10.1.2.3") }, null);
        Assert.Equal(CheckStatus.Fail, wrong.Status);
        Assert.Contains("10.1.2.3", wrong.Detail);

        var failed = MzlSelfCheckRules.EvaluateDns(null, "No such host");
        Assert.Equal(CheckStatus.Fail, failed.Status);
        Assert.Contains("hosts", failed.Solution);
    }

    [Fact]
    public void EvaluateBrowser_ok_when_policy_covers_mzl()
    {
        var r = MzlSelfCheckRules.EvaluateBrowser("Chrome", "156.0.8078.73", true, () => "cmd");
        Assert.Equal(CheckStatus.Ok, r.Status);
    }

    [Fact]
    public void EvaluateBrowser_warns_on_new_chrome_without_policy_and_includes_the_script_command()
    {
        var r = MzlSelfCheckRules.EvaluateBrowser("Chrome", "156.0.8078.73", false, () => "powershell -File x.ps1");
        Assert.Equal(CheckStatus.Warn, r.Status);
        Assert.Contains("powershell -File x.ps1", r.Solution);
    }

    [Fact]
    public void EvaluateBrowser_warning_offers_the_one_click_fix()
    {
        var warn = MzlSelfCheckRules.EvaluateBrowser("Chrome", "156.0.8078.73", false, () => "cmd");
        Assert.Equal(CheckAction.AllowBrowser, warn.Action);

        Assert.Equal(CheckAction.None, MzlSelfCheckRules.EvaluateBrowser("Chrome", "156.0.8078.73", true, () => "cmd").Action);
        Assert.Equal(CheckAction.None, MzlSelfCheckRules.EvaluateBrowser("Chrome", "120.0.1.1", false, () => "cmd").Action);
    }

    [Fact]
    public void EvaluateProxy_warns_only_when_a_proxy_would_take_the_request()
    {
        Assert.Equal(CheckStatus.Ok, MzlSelfCheckRules.EvaluateProxy(null, null).Status);

        var warn = MzlSelfCheckRules.EvaluateProxy("http://proxy.corp:8080/", null);
        Assert.Equal(CheckStatus.Warn, warn.Status);
        Assert.Contains("proxy.corp:8080", warn.Detail);
        Assert.Contains("localhost.lodop.net", warn.Solution);

        Assert.Equal(CheckStatus.Info, MzlSelfCheckRules.EvaluateProxy(null, "boom").Status);
    }

    [Fact]
    public void EvaluateOldClodop_fails_when_it_is_running_and_names_the_process()
    {
        var r = MzlSelfCheckRules.EvaluateOldClodop(new[] { "CLodop_Server.exe" }, Array.Empty<string>(), Array.Empty<string>());

        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Contains("CLodop_Server.exe", r.Detail);
        Assert.False(string.IsNullOrWhiteSpace(r.Solution));
    }

    [Fact]
    public void EvaluateOldClodop_warns_when_only_installed_or_set_to_autostart()
    {
        var installed = MzlSelfCheckRules.EvaluateOldClodop(Array.Empty<string>(), new[] { "C-Lodop Web打印服务" }, Array.Empty<string>());
        Assert.Equal(CheckStatus.Warn, installed.Status);
        Assert.Contains("C-Lodop", installed.Detail);

        var autostart = MzlSelfCheckRules.EvaluateOldClodop(Array.Empty<string>(), Array.Empty<string>(), new[] { "CLodop" });
        Assert.Equal(CheckStatus.Warn, autostart.Status);
        Assert.Contains("CLodop", autostart.Detail);
    }

    [Fact]
    public void EvaluateOldClodop_is_ok_when_nothing_is_found()
    {
        var r = MzlSelfCheckRules.EvaluateOldClodop(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        Assert.Equal(CheckStatus.Ok, r.Status);
        Assert.Null(r.Solution);
    }

    [Theory]
    [InlineData("C-Lodop Web打印服务", true)]
    [InlineData("CLodop", true)]
    [InlineData("LabelPrinter", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsClodopName_matches_lodop_products_only(string? text, bool expected) =>
        Assert.Equal(expected, MzlSelfCheckRules.IsClodopName(text));

    [Fact]
    public void AllowBrowserFix_maps_each_outcome_to_a_clear_result()
    {
        Assert.Equal(CheckStatus.Info, AllowBrowserFix.ToResult(new FixOutcome(true, null, null, false)).Status);

        var start = AllowBrowserFix.ToResult(new FixOutcome(false, "blocked by AppLocker", null, false));
        Assert.Equal(CheckStatus.Fail, start.Status);
        Assert.Contains("blocked by AppLocker", start.Detail);

        Assert.Equal(CheckStatus.Ok, AllowBrowserFix.ToResult(new FixOutcome(false, null, 0, true)).Status);

        var notApplied = AllowBrowserFix.ToResult(new FixOutcome(false, null, 1, false));
        Assert.Equal(CheckStatus.Fail, notApplied.Status);
        Assert.Contains("shipswithus.com", notApplied.Solution);

        // exit code 0 but the policy isn't readable afterwards: don't claim success
        Assert.Equal(CheckStatus.Fail, AllowBrowserFix.ToResult(new FixOutcome(false, null, 0, false)).Status);
        // script didn't finish in time
        Assert.Contains("timeout", AllowBrowserFix.ToResult(new FixOutcome(false, null, null, false)).Detail);
    }

    [Fact]
    public void FixScript_EncodedCommand_carries_the_whole_script_and_stays_short_enough_for_ShellExecute()
    {
        var encoded = FixScript.EncodedCommand();
        var decoded = System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(encoded));

        // the elevated run must not depend on any file on disk
        Assert.DoesNotContain(".ps1", decoded);
        Assert.Contains("LocalNetworkAccessAllowedForUrls", decoded);
        Assert.Contains("shipswithus.com", decoded);
        Assert.Contains("exit 1", decoded);
        Assert.DoesNotContain("\n\n", decoded);
        Assert.DoesNotContain("\n ", decoded); // indentation stripped

        // 4282 characters of arguments were verified to launch; keep clear headroom below that
        Assert.True(encoded.Length < 3600, $"encoded script is {encoded.Length} chars");
    }

    [Fact]
    public void FixScript_uses_the_same_origins_and_browsers_as_the_apps_own_policy_and_no_unknown_policy()
    {
        foreach (var origin in BrowserLocalAccessPolicy.AllowedOrigins)
            Assert.Contains($"\"{origin}\"", FixScript.Content);
        foreach (var root in BrowserLocalAccessPolicy.BrowserPolicyRoots)
            Assert.Contains($"\"HKLM:\\{root}\"", FixScript.Content);

        // Chrome 156 reports LoopbackNetworkAccessAllowedForUrls as "Unknown policy"
        Assert.DoesNotContain("Loopback", FixScript.Content);
    }

    [Fact]
    public void FixScript_reports_failure_through_its_exit_code_and_never_overwrites_existing_entries()
    {
        Assert.Contains("exit 1", FixScript.Content);
        Assert.Contains("$ErrorActionPreference = 'Stop'", FixScript.Content);
        Assert.Contains("$existing -notcontains $u", FixScript.Content);
        Assert.DoesNotContain("Loopback", FixScript.Content);
    }

    [Fact]
    public void EvaluateBrowser_builds_the_script_command_only_for_the_warning()
    {
        var calls = 0;
        Func<string> command = () => { calls++; return "cmd"; };

        MzlSelfCheckRules.EvaluateBrowser("Chrome", "156.0.8078.73", true, command);   // allowed
        MzlSelfCheckRules.EvaluateBrowser("Chrome", "120.0.1.1", false, command);      // old Chrome
        Assert.Equal(0, calls);

        MzlSelfCheckRules.EvaluateBrowser("Chrome", "156.0.8078.73", false, command);  // warning
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(@"""C:\Program Files\CLodop\CLodopPrint64.exe"" /auto", "CLodopPrint64.exe")]
    [InlineData(@"C:\Tools\CLodop_Server.exe -x", "CLodop_Server.exe")]
    [InlineData(@"D:\Pda\LodopCompat\LabelPrinter.exe", "LabelPrinter.exe")]       // folder name must not matter
    [InlineData(@"""D:\Lodop Stuff\LabelPrinter.exe""", "LabelPrinter.exe")]
    [InlineData(@"D:\Lodop Stuff\LabelPrinter.exe", "LabelPrinter.exe")]           // unquoted path with a space
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ExecutableFileName_extracts_the_program_not_the_folders(string? command, string? expected) =>
        Assert.Equal(expected, MzlSelfCheckRules.ExecutableFileName(command));

    [Fact]
    public void An_autostart_path_containing_lodop_is_not_mistaken_for_clodop()
    {
        var exe = MzlSelfCheckRules.ExecutableFileName(@"""D:\Pda\LodopCompat\LabelPrinter.exe""");
        Assert.False(MzlSelfCheckRules.IsClodopName(exe));
        Assert.True(MzlSelfCheckRules.IsClodopName(MzlSelfCheckRules.ExecutableFileName(@"""C:\CLodop\CLodopPrint64.exe""")));
    }

    [Fact]
    public void EvaluateBrowser_is_only_info_on_pre_local_network_access_chrome()
    {
        var r = MzlSelfCheckRules.EvaluateBrowser("Chrome", "120.0.1.1", false, () => "cmd");
        Assert.Equal(CheckStatus.Info, r.Status);
    }

    [Fact]
    public void EvaluateRequestStats_warns_when_the_browser_never_reached_us()
    {
        Assert.Equal(CheckStatus.Warn, MzlSelfCheckRules.EvaluateRequestStats(null).Status);
        var zero = new LodopRequestSnapshot(0, 0, 0, null, null);
        Assert.Equal(CheckStatus.Warn, MzlSelfCheckRules.EvaluateRequestStats(zero).Status);
    }

    [Fact]
    public void EvaluateRequestStats_warns_when_script_loaded_but_no_print_arrived()
    {
        var r = MzlSelfCheckRules.EvaluateRequestStats(new LodopRequestSnapshot(2, 1, 0, DateTime.Now, "https://fbd.shipswithus.com"));
        Assert.Equal(CheckStatus.Warn, r.Status);
    }

    [Fact]
    public void EvaluateRequestStats_is_ok_once_a_print_post_arrived()
    {
        var r = MzlSelfCheckRules.EvaluateRequestStats(new LodopRequestSnapshot(1, 1, 3, new DateTime(2026, 11, 2, 9, 30, 0), "https://fbd.shipswithus.com"));
        Assert.Equal(CheckStatus.Ok, r.Status);
        Assert.Contains("3", r.Detail);
        Assert.Contains("fbd.shipswithus.com", r.Detail);
    }

    [Fact]
    public void ToLogText_indents_every_line_of_a_multi_line_solution()
    {
        var text = new CheckResult(CheckStatus.Fail, "T", "D", "line1\nline2").ToLogText();

        Assert.StartsWith("[FAIL] T: D", text);
        Assert.Contains("line1", text);
        Assert.Contains("\r\n          line2", text);
    }

    [Fact]
    public void ToLogText_has_no_solution_line_for_passing_checks()
    {
        var text = new CheckResult(CheckStatus.Ok, "T", "D").ToLogText();
        Assert.Equal("[ OK ] T: D", text);
    }
}

public class LodopRequestFlowTests
{
    private static LodopCompatListener NewListener(LodopRequestStats? stats = null) =>
        new(new LodopCompatConfig { PrinterName = "test" }, new LabelPrinter.Printing.PrintModel(), _ => { }, stats);

    [Fact]
    public void Dispatch_counts_page_requests_but_not_tool_requests()
    {
        var listener = NewListener();

        listener.Dispatch(new LodopHttpExchange("GET", "/CLodopfuncs.js", "https://fbd.shipswithus.com", null, false, FromBrowser: true), 8000, false);
        listener.Dispatch(new LodopHttpExchange("OPTIONS", "/lodop_print", "https://fbd.shipswithus.com", null, false, FromBrowser: true), 8000, false);
        listener.Dispatch(new LodopHttpExchange("GET", "/CLodopfuncs.js", null, null, false, FromBrowser: false), 8000, false); // curl / address bar

        var s = listener.Stats.Snapshot();
        Assert.Equal(1, s.ScriptLoads);
        Assert.Equal(1, s.Preflights);
        Assert.Equal(0, s.PrintPosts);
    }

    [Fact]
    public void A_supplied_stats_object_is_used_so_counts_can_outlive_a_listener()
    {
        var stats = new LodopRequestStats();
        var first = NewListener(stats);
        first.Dispatch(new LodopHttpExchange("GET", "/", "https://fbd.shipswithus.com", null, false, FromBrowser: true), 8000, false);
        first.Dispose();

        var second = NewListener(stats);
        second.Dispatch(new LodopHttpExchange("GET", "/", "https://fbd.shipswithus.com", null, false, FromBrowser: true), 8000, false);

        Assert.Same(stats, second.Stats);
        Assert.Equal(2, stats.Snapshot().ScriptLoads);
    }

    private static async Task<LodopHttpExchange?> Parse(string rawRequest) =>
        await LodopLoopbackHttpsServer.ReadRequestAsync(
            new MemoryStream(System.Text.Encoding.ASCII.GetBytes(rawRequest)), CancellationToken.None);

    [Fact]
    public async Task Https_parser_flags_script_loads_from_a_page_but_not_address_bar_visits()
    {
        var fromPage = await Parse("GET /CLodopfuncs.js HTTP/1.1\r\nHost: localhost.lodop.net:8443\r\nSec-Fetch-Site: cross-site\r\nReferer: https://fbd.shipswithus.com/manage\r\n\r\n");
        var addressBar = await Parse("GET /CLodopfuncs.js HTTP/1.1\r\nHost: localhost.lodop.net:8443\r\nSec-Fetch-Site: none\r\n\r\n");
        var curl = await Parse("GET /CLodopfuncs.js HTTP/1.1\r\nHost: localhost.lodop.net:8443\r\n\r\n");

        Assert.True(fromPage!.FromBrowser);
        Assert.False(addressBar!.FromBrowser);
        Assert.False(curl!.FromBrowser);
    }

    [Fact]
    public async Task Https_parser_flags_a_cors_preflight_by_its_Origin_header()
    {
        var preflight = await Parse("OPTIONS /lodop_print HTTP/1.1\r\nHost: localhost.lodop.net:8443\r\nOrigin: https://fbd.shipswithus.com\r\n\r\n");

        Assert.True(preflight!.FromBrowser);
        Assert.Equal("https://fbd.shipswithus.com", preflight.Origin);
    }
}

public class LodopRequestStatsTests
{
    [Theory]
    [InlineData("https://fbd.shipswithus.com", null, null, true)]   // fetch / preflight
    [InlineData(null, "https://fbd.shipswithus.com/manage", "cross-site", true)] // script tag
    [InlineData(null, null, "cross-site", true)]
    [InlineData(null, null, "same-site", true)]
    [InlineData(null, null, "none", false)]   // typed into the address bar
    [InlineData(null, null, "NONE", false)]
    [InlineData(null, null, null, false)]     // our own probes and curl
    [InlineData("", "", "", false)]
    public void IsBrowserPageRequest_ignores_address_bar_visits_and_tools(
        string? origin, string? referer, string? secFetchSite, bool expected) =>
        Assert.Equal(expected, LodopRequestStats.IsBrowserPageRequest(origin, referer, secFetchSite));

    [Fact]
    public void Record_counts_script_loads_preflights_and_prints_separately()
    {
        var stats = new LodopRequestStats();
        var t = new DateTime(2026, 11, 2, 8, 0, 0);

        stats.Record("GET", "/CLodopfuncs.js", "https://fbd.shipswithus.com", t);
        stats.Record("GET", "/", null, t);
        stats.Record("OPTIONS", "/lodop_print", "https://fbd.shipswithus.com", t);
        stats.Record("POST", "/lodop_print", "https://fbd.shipswithus.com", t.AddMinutes(1));

        var s = stats.Snapshot();
        Assert.Equal(2, s.ScriptLoads);
        Assert.Equal(1, s.Preflights);
        Assert.Equal(1, s.PrintPosts);
        Assert.Equal(t.AddMinutes(1), s.LastLocalTime);
        Assert.Equal("https://fbd.shipswithus.com", s.LastSource);
    }

    [Fact]
    public void Record_ignores_unrelated_requests()
    {
        var stats = new LodopRequestStats();
        stats.Record("GET", "/_test_sample.pdf", null);
        stats.Record("GET", "/favicon.ico", null);

        var s = stats.Snapshot();
        Assert.Equal(0, s.ScriptLoads + s.Preflights + s.PrintPosts);
        Assert.Null(s.LastLocalTime);
    }
}
