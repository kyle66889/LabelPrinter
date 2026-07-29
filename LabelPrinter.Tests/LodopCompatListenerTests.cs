using System.Text.RegularExpressions;
using LabelPrinter.Services;
using Xunit;

namespace LabelPrinter.Tests;

public class LodopCompatListenerTests
{
    [Theory]
    [InlineData(8000)]
    [InlineData(18000)]
    public void BuildClodopFuncsJs_exposes_the_functions_getLodop_hard_requires(int port)
    {
        var js = LodopCompatListener.BuildClodopFuncsJs(port);

        Assert.Contains("function getCLodop()", js);
        Assert.Contains("SET_LICENSES:", js); // getLodop() calls this unconditionally — must exist or it throws
        Assert.Contains("ADD_PRINT_PDF:", js);
        Assert.Contains("SET_PRINTER_INDEX:", js);
        Assert.Contains("PRINT:", js);
        Assert.Contains(".catch(", js); // silent fetch failures are the main "MZL 没反应" failure mode
        Assert.True(LodopCompatListener.LooksLikeOurClodopFuncsJs(js));
    }

    [Theory]
    [InlineData(8000, false)]
    [InlineData(18000, false)]
    [InlineData(8443, true)]
    [InlineData(8444, true)]
    public void BuildClodopFuncsJs_posts_to_an_absolute_url_on_the_port_the_request_hit(int port, bool https)
    {
        var js = LodopCompatListener.BuildClodopFuncsJs(port, https);

        var expectedHost = https ? "localhost.lodop.net" : "localhost";
        var expectedScheme = https ? "https" : "http";
        var match = Regex.Match(js, @"fetch\('(https?://[^']+/lodop_print)'");
        Assert.True(match.Success, "Expected an absolute fetch URL.");
        Assert.Equal($"{expectedScheme}://{expectedHost}:{port}/lodop_print", match.Groups[1].Value);
    }
}
