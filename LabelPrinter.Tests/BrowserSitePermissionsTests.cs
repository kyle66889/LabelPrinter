using LabelPrinter.Services;
using Xunit;

namespace LabelPrinter.Tests;

public class BrowserSitePermissionsTests
{
    private const string Prefs = """
        {
          "profile": {
            "content_settings": {
              "exceptions": {
                "loopback_network": {
                  "https://passport.qidian.com:443,*": { "setting": 1 },
                  "https://fbd.shipswithus.com:443,*": { "setting": 2 }
                },
                "local_network_access": {
                  "https://fbd.shipswithus.com:443,*": { "setting": 1 }
                },
                "local_fonts": {
                  "https://fbd.shipswithus.com:443,*": { "setting": 2 }
                }
              }
            }
          }
        }
        """;

    [Fact]
    public void ParsePreferences_keeps_only_mzl_entries_in_the_network_categories()
    {
        var found = BrowserSitePermissions.ParsePreferences("Default", Prefs);

        Assert.Equal(2, found.Count);
        Assert.All(found, p => Assert.Equal("https://fbd.shipswithus.com:443", p.Origin));
        Assert.Contains(found, p => p.Category == "loopback_network" && p.Setting == BrowserSitePermissions.Block);
        Assert.Contains(found, p => p.Category == "local_network_access" && p.Setting == BrowserSitePermissions.Allow);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"profile":{}}""")]
    [InlineData("""{"profile":{"content_settings":{"exceptions":{}}}}""")]
    public void ParsePreferences_returns_empty_when_the_sections_are_missing(string json) =>
        Assert.Empty(BrowserSitePermissions.ParsePreferences("Default", json));

    [Fact]
    public void ReadAllProfiles_returns_empty_for_a_missing_folder() =>
        Assert.Empty(BrowserSitePermissions.ReadAllProfiles(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid())));

    [Fact]
    public void ReadAllProfiles_reads_Default_and_Profile_folders_and_skips_others()
    {
        var root = Path.Combine(Path.GetTempPath(), "lp-prefs-" + Guid.NewGuid());
        try
        {
            foreach (var name in new[] { "Default", "Profile 1", "System Profile" })
            {
                Directory.CreateDirectory(Path.Combine(root, name));
                File.WriteAllText(Path.Combine(root, name, "Preferences"), Prefs);
            }

            var found = BrowserSitePermissions.ReadAllProfiles(root);

            Assert.Equal(4, found.Count);
            Assert.Equal(new[] { "Default", "Profile 1" }, found.Select(p => p.Profile).Distinct().OrderBy(x => x));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Evaluate_fails_when_any_entry_is_blocked_and_says_how_to_fix_it()
    {
        var r = BrowserSitePermissions.Evaluate("Chrome", BrowserSitePermissions.ParsePreferences("Default", Prefs));

        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.False(string.IsNullOrWhiteSpace(r.Solution));
    }

    private static SitePermission P(string profile, string category, int setting) =>
        new(profile, category, "https://fbd.shipswithus.com:443", setting);

    [Fact]
    public void Evaluate_warns_instead_of_failing_when_only_some_profiles_block()
    {
        var r = BrowserSitePermissions.Evaluate("Chrome", new[]
        {
            P("Default", "loopback_network", BrowserSitePermissions.Allow),
            P("Profile 3", "loopback_network", BrowserSitePermissions.Block)
        });

        Assert.Equal(CheckStatus.Warn, r.Status);
        Assert.Contains("Profile 3", r.Detail);
    }

    [Fact]
    public void Evaluate_fails_only_when_every_profile_that_has_a_setting_blocks()
    {
        var r = BrowserSitePermissions.Evaluate("Chrome", new[]
        {
            P("Default", "loopback_network", BrowserSitePermissions.Block),
            P("Profile 1", "local_network_access", BrowserSitePermissions.Block)
        });

        Assert.Equal(CheckStatus.Fail, r.Status);
    }

    [Fact]
    public void Evaluate_ignores_a_LAN_only_block_because_localhost_is_what_MZL_needs()
    {
        var r = BrowserSitePermissions.Evaluate("Chrome", new[]
        {
            P("Default", "local_network", BrowserSitePermissions.Block),
            P("Default", "loopback_network", BrowserSitePermissions.Allow)
        });

        Assert.Equal(CheckStatus.Ok, r.Status);
    }

    [Fact]
    public void Evaluate_lets_the_loopback_entry_override_the_local_network_access_one()
    {
        var r = BrowserSitePermissions.Evaluate("Chrome", new[]
        {
            P("Default", "local_network_access", BrowserSitePermissions.Block),
            P("Default", "loopback_network", BrowserSitePermissions.Allow)
        });

        Assert.Equal(CheckStatus.Ok, r.Status);
    }

    [Fact]
    public void Evaluate_is_info_when_only_the_LAN_category_has_an_entry()
    {
        var r = BrowserSitePermissions.Evaluate("Chrome", new[] { P("Default", "local_network", BrowserSitePermissions.Block) });
        Assert.Equal(CheckStatus.Info, r.Status);
    }

    [Fact]
    public void Evaluate_is_ok_when_everything_is_allowed()
    {
        var allowed = new[]
        {
            new SitePermission("Default", "local_network_access", "https://fbd.shipswithus.com:443", BrowserSitePermissions.Allow),
            new SitePermission("Default", "loopback_network", "https://fbd.shipswithus.com:443", BrowserSitePermissions.Allow)
        };

        Assert.Equal(CheckStatus.Ok, BrowserSitePermissions.Evaluate("Chrome", allowed).Status);
    }

    [Fact]
    public void Evaluate_is_info_when_nothing_is_saved_or_the_choice_is_ask()
    {
        Assert.Equal(CheckStatus.Info, BrowserSitePermissions.Evaluate("Chrome", Array.Empty<SitePermission>()).Status);

        var ask = new[] { new SitePermission("Default", "loopback_network", "https://fbd.shipswithus.com:443", BrowserSitePermissions.Ask) };
        Assert.Equal(CheckStatus.Info, BrowserSitePermissions.Evaluate("Chrome", ask).Status);
    }
}
