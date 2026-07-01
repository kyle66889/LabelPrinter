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
