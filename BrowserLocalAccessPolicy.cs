using Microsoft.Win32;

namespace LabelPrinter;

/// <summary>
/// Chrome/Edge block public http origins (e.g. http://test.shipswithus.com:8080) from
/// loading http://localhost:8000 (Private Network / Local Network Access). Real C-Lodop
/// hits the same wall on modern Chromium unless the browser allows the site.
///
/// When Lodop-compat is enabled we write per-user (HKCU) enterprise policies that allow
/// known MZL hosts to talk to loopback. User must fully restart the browser afterward.
/// </summary>
public static class BrowserLocalAccessPolicy
{
    // URL patterns Chrome accepts for these list policies (see chromeenterprise.google/policies).
    private static readonly string[] AllowedOrigins =
    [
        "http://test.shipswithus.com:8080",
        "http://test.shipswithus.com:*",
        "http://fbd.shipswithus.com",
        "http://fbd.shipswithus.com:*",
        "https://fbd.shipswithus.com",
        "https://fbd.shipswithus.com:*",
        "http://[*.]shipswithus.com:*",
        "https://[*.]shipswithus.com:*",
        "http://localhost:*",
        "http://127.0.0.1:*",
    ];

    private static readonly string[] BrowserPolicyRoots =
    [
        @"Software\Policies\Google\Chrome",
        @"Software\Policies\Microsoft\Edge",
    ];

    /// <summary>
    /// Returns true if any registry value was written. Never throws — policy is best-effort.
    /// </summary>
    public static bool TryApply(Action<string>? log = null)
    {
        var changed = false;
        try
        {
            foreach (var root in BrowserPolicyRoots)
            {
                if (ApplyForBrowser(root))
                    changed = true;
            }

            if (changed)
                log?.Invoke("Browser policy: allowed shipswithus.com → localhost (restart Chrome/Edge to apply).");
        }
        catch (Exception ex)
        {
            log?.Invoke($"Browser policy: failed to write ({ex.Message}).");
        }

        return changed;
    }

    private static bool ApplyForBrowser(string root)
    {
        var changed = false;

        using (var key = Registry.CurrentUser.CreateSubKey(root, writable: true)
               ?? throw new InvalidOperationException($"Cannot open {root}"))
        {
            // Legacy PNA: allow insecure (http) contexts to reach private/loopback.
            if (!Equals(key.GetValue("InsecurePrivateNetworkRequestsAllowed"), 1))
            {
                key.SetValue("InsecurePrivateNetworkRequestsAllowed", 1, RegistryValueKind.DWord);
                changed = true;
            }
        }

        changed |= WriteUrlList($@"{root}\InsecurePrivateNetworkRequestsAllowedForUrls", AllowedOrigins);
        changed |= WriteUrlList($@"{root}\LocalNetworkAccessAllowedForUrls", AllowedOrigins);
        // Chrome 146+ splits loopback out of "local network".
        changed |= WriteUrlList($@"{root}\LoopbackNetworkAccessAllowedForUrls", AllowedOrigins);

        return changed;
    }

    private static bool WriteUrlList(string subKeyPath, string[] urls)
    {
        using var key = Registry.CurrentUser.CreateSubKey(subKeyPath, writable: true)
            ?? throw new InvalidOperationException($"Cannot open {subKeyPath}");

        var changed = false;
        for (var i = 0; i < urls.Length; i++)
        {
            var name = (i + 1).ToString();
            var existing = key.GetValue(name) as string;
            if (!string.Equals(existing, urls[i], StringComparison.Ordinal))
            {
                key.SetValue(name, urls[i], RegistryValueKind.String);
                changed = true;
            }
        }

        return changed;
    }
}
