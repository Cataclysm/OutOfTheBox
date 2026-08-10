// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace OutOfTheBox.Msi.CustomActions
{
    /// <summary>
    /// Generates the self-signed HTTPS certificate the installed service binds Kestrel to - the
    /// installer never did this before, leaving it as a manual, undocumented-at-startup step
    /// (INSTALL.md's own "Certificate" section) that a real install proved to be a genuine gap:
    /// the service crashes outright on startup ("No server certificate was specified") rather than
    /// failing softly, and nothing before that point told the operator this step was required.
    /// </summary>
    public static class CertificateGenerator
    {
        /// <summary>
        /// Creates a self-signed server-authentication certificate covering this machine's own
        /// hostname, loopback, and local IPv4 addresses (gathered live, since this runs on the
        /// actual target machine at install time) and exports it as PFX bytes protected by
        /// <paramref name="password"/>.
        /// </summary>
        public static byte[] CreateSelfSignedPfx(string password)
        {
            using (var rsa = RSA.Create(2048))
            {
                var request = new CertificateRequest(
                    $"CN={Environment.MachineName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

                request.CertificateExtensions.Add(
                    new X509KeyUsageExtension(
                        X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));
                request.CertificateExtensions.Add(
                    new X509EnhancedKeyUsageExtension(
                        new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, critical: false)); // Server Authentication

                var sanBuilder = new SubjectAlternativeNameBuilder();
                sanBuilder.AddDnsName(Environment.MachineName);
                sanBuilder.AddDnsName("localhost");
                sanBuilder.AddIpAddress(IPAddress.Loopback);
                foreach (var address in GetLocalIPv4Addresses())
                {
                    sanBuilder.AddIpAddress(address);
                }

                request.CertificateExtensions.Add(sanBuilder.Build());

                var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
                var notAfter = DateTimeOffset.UtcNow.AddYears(10);

                using (var certificate = request.CreateSelfSigned(notBefore, notAfter))
                {
                    return certificate.Export(X509ContentType.Pfx, password);
                }
            }
        }

        /// <summary>
        /// Extracts just the public certificate (no private key) from <paramref name="pfxBytes"/>,
        /// PEM-encoded - safe to hand to anyone who needs to trust this self-signed certificate (an
        /// operator's PC, the sbx sandbox's MCP client), unlike the password-protected PFX itself,
        /// which carries the private key. Hand-rolled PEM encoding rather than the BCL's
        /// <c>PemEncoding</c> type, since this project targets net472 (that type was added in .NET 5).
        /// </summary>
        public static string ExportPublicCertificatePem(byte[] pfxBytes, string password)
        {
            using (var certificate = new X509Certificate2(pfxBytes, password))
            {
                var der = certificate.Export(X509ContentType.Cert);
                return "-----BEGIN CERTIFICATE-----\r\n" + FormatBase64Lines(der) + "-----END CERTIFICATE-----\r\n";
            }
        }

        /// <summary>Base64-encodes <paramref name="data"/> wrapped at the conventional 64-character PEM line length.</summary>
        private static string FormatBase64Lines(byte[] data)
        {
            var base64 = Convert.ToBase64String(data);
            var builder = new StringBuilder();

            for (var offset = 0; offset < base64.Length; offset += 64)
            {
                var length = Math.Min(64, base64.Length - offset);
                builder.Append(base64, offset, length).Append("\r\n");
            }

            return builder.ToString();
        }

        /// <summary>
        /// True if <paramref name="password"/> actually opens the PFX in <paramref name="pfxBytes"/>.
        /// Used to detect a certificate/password mismatch before trusting an on-disk certificate as
        /// still usable - see the caller in <see cref="ResolveSecretsAction"/> for why this can
        /// happen even though neither side is nominally ever regenerated.
        /// </summary>
        public static bool CanOpen(byte[] pfxBytes, string password)
        {
            try
            {
                using (new X509Certificate2(pfxBytes, password))
                {
                    return true;
                }
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        private static IEnumerable<IPAddress> GetLocalIPv4Addresses()
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        yield return unicast.Address;
                    }
                }
            }
        }
    }
}
