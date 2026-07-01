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
