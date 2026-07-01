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
