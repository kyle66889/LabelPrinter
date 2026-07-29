using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LabelPrinter.Services;

/// <summary>
/// Self-signed cert for <c>localhost.lodop.net</c> (public DNS → 127.0.0.1). MZL's
/// https pages load <c>https://localhost.lodop.net:8443/CLodopfuncs.js</c>; the browser
/// must trust this cert or the script fails with ERR_CERT_AUTHORITY_INVALID.
/// Stored in CurrentUser\My and mirrored into CurrentUser\Root (no admin required).
/// </summary>
public static class LodopCompatCertificate
{
    public const string HostName = "localhost.lodop.net";
    private const string SubjectCn = "CN=localhost.lodop.net";
    private const string FriendlyName = "LabelPrinter LodopCompat";

    public static X509Certificate2 GetOrCreate(Action<string>? log = null)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);

        foreach (var existing in store.Certificates)
        {
            if (!string.Equals(existing.FriendlyName, FriendlyName, StringComparison.Ordinal))
                continue;
            if (existing.NotAfter <= DateTime.Now.AddDays(30))
                continue;
            if (!existing.HasPrivateKey)
                continue;

            return existing;
        }

        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(SubjectCn, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false)); // serverAuth

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(HostName);
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        san.AddIpAddress(IPAddress.IPv6Loopback);
        req.CertificateExtensions.Add(san.Build());

        var created = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(10));
        created.FriendlyName = FriendlyName;

        // Re-import so the private key is persisted in the user key store (SslStream needs it).
        var pfx = created.Export(X509ContentType.Pfx);
        created.Dispose();
        var cert = new X509Certificate2(
            pfx,
            (string?)null,
            X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
        cert.FriendlyName = FriendlyName;

        store.Add(cert);
        log?.Invoke($"Lodop-compat: created TLS cert for {HostName} (thumbprint {cert.Thumbprint}).");
        return cert;
    }

    /// <summary>
    /// Install into CurrentUser\Root so Chrome trusts https://localhost.lodop.net.
    /// Must NOT run on the UI/startup thread — Windows may show a confirmation dialog
    /// that blocks the tray process indefinitely.
    /// </summary>
    public static void EnsureTrustedRootAsync(X509Certificate2 cert, Action<string>? log = null)
    {
        _ = Task.Run(() =>
        {
            try
            {
                using var root = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
                root.Open(OpenFlags.ReadWrite);
                var found = root.Certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint, validOnly: false);
                if (found.Count > 0)
                    return;

                root.Add(new X509Certificate2(cert.Export(X509ContentType.Cert)));
                log?.Invoke($"Lodop-compat: trusted {HostName} cert in CurrentUser\\Root (restart browser if TLS errors persist).");
            }
            catch (Exception ex)
            {
                log?.Invoke($"Lodop-compat: could not trust cert in Root ({ex.Message}). Open https://{HostName}:8443/CLodopfuncs.js once and accept the certificate, or install it manually.");
            }
        });
    }
}
