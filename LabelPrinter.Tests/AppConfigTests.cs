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
        // First-run defaults: size rows off; MZL compat is the only enabled path.
        Assert.All(formats, f => Assert.False(f.Enabled));
    }

    [Fact]
    public void FirstRunDefaults_enable_startup_and_lodop_only()
    {
        var config = AppConfig.CreateFirstRunDefaults(new[] { "OneNote", "BIXOLON XD3-40d - BPL-Z", "Fax" });

        Assert.True(config.RunAtStartup);
        Assert.True(config.LodopCompat.Enabled);
        Assert.Equal("BIXOLON XD3-40d - BPL-Z", config.LodopCompat.PrinterName);
        Assert.All(config.LabelFormats, f => Assert.False(f.Enabled));
    }

    [Fact]
    public void PickDefaultLodopPrinterName_falls_back_to_first_when_no_bixolon()
    {
        Assert.Equal("OneNote", AppConfig.PickDefaultLodopPrinterName(new[] { "OneNote", "Fax" }));
        Assert.Equal("", AppConfig.PickDefaultLodopPrinterName(Array.Empty<string>()));
    }

    [Fact]
    public void EnsureLodopPrinterIfEmpty_only_fills_blank_name()
    {
        var config = new AppConfig
        {
            LodopCompat = new LodopCompatConfig { Enabled = true, PrinterName = "Keep Me" }
        };
        Assert.False(config.EnsureLodopPrinterIfEmpty(new[] { "BIXOLON X" }));
        Assert.Equal("Keep Me", config.LodopCompat.PrinterName);

        config.LodopCompat.PrinterName = "";
        Assert.True(config.EnsureLodopPrinterIfEmpty(new[] { "BIXOLON X" }));
        Assert.Equal("BIXOLON X", config.LodopCompat.PrinterName);
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
        config.LabelFormats[2].Enabled = true;

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

    [Fact]
    public void ValidateFormats_returns_no_errors_for_distinct_enabled_ports()
    {
        var config = new AppConfig { LabelFormats = AppConfig.CreateDefaultFormats() };

        var errors = config.ValidateFormats();

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateFormats_flags_duplicate_port_among_enabled_formats()
    {
        var config = new AppConfig { LabelFormats = AppConfig.CreateDefaultFormats() };
        config.LabelFormats[0].Enabled = true;
        config.LabelFormats[1].Enabled = true;
        config.LabelFormats[0].Port = config.LabelFormats[1].Port; // 4x2 and 4x3 collide

        var errors = config.ValidateFormats();

        Assert.Single(errors);
        Assert.Contains(config.LabelFormats[1].Port.ToString(), errors[0]);
    }

    [Fact]
    public void ValidateFormats_ignores_port_collisions_on_disabled_formats()
    {
        var config = new AppConfig { LabelFormats = AppConfig.CreateDefaultFormats() };
        config.LabelFormats[0].Enabled = false;
        config.LabelFormats[0].Port = config.LabelFormats[1].Port; // collision but 4x2 disabled

        var errors = config.ValidateFormats();

        Assert.Empty(errors);
    }
}
