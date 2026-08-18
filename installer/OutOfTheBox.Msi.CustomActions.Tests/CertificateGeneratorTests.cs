// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace OutOfTheBox.Msi.CustomActions.Tests
{
    public class CertificateGeneratorTests
    {
        [Fact]
        public void CreateSelfSignedPfx_produces_a_certificate_loadable_with_the_same_password()
        {
            var pfxBytes = CertificateGenerator.CreateSelfSignedPfx("TestPassword123!");

            using (var certificate = new X509Certificate2(pfxBytes, "TestPassword123!"))
            {
                Assert.True(certificate.HasPrivateKey);
                Assert.Equal($"CN={Environment.MachineName}", certificate.Subject);
            }
        }

        [Fact]
        public void CreateSelfSignedPfx_rejects_the_wrong_password()
        {
            var pfxBytes = CertificateGenerator.CreateSelfSignedPfx("CorrectPassword");

            Assert.ThrowsAny<CryptographicException>(
                () => new X509Certificate2(pfxBytes, "WrongPassword"));
        }

        [Fact]
        public void CreateSelfSignedPfx_covers_localhost_and_loopback_in_the_subject_alternative_name()
        {
            var pfxBytes = CertificateGenerator.CreateSelfSignedPfx("TestPassword123!");

            using (var certificate = new X509Certificate2(pfxBytes, "TestPassword123!"))
            {
                var sanExtension = certificate.Extensions["2.5.29.17"];
                Assert.NotNull(sanExtension);
                var sanText = sanExtension.Format(false);
                Assert.Contains("localhost", sanText, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(Environment.MachineName, sanText, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void CreateSelfSignedPfx_is_valid_for_a_long_window_covering_now()
        {
            var pfxBytes = CertificateGenerator.CreateSelfSignedPfx("TestPassword123!");

            using (var certificate = new X509Certificate2(pfxBytes, "TestPassword123!"))
            {
                Assert.True(certificate.NotBefore <= DateTime.Now);
                Assert.True(certificate.NotAfter > DateTime.Now.AddYears(5));
            }
        }

        [Fact]
        public void CanOpen_returns_true_for_the_matching_password()
        {
            var pfxBytes = CertificateGenerator.CreateSelfSignedPfx("TestPassword123!");

            Assert.True(CertificateGenerator.CanOpen(pfxBytes, "TestPassword123!"));
        }

        [Fact]
        public void CanOpen_returns_false_for_a_mismatched_password()
        {
            var pfxBytes = CertificateGenerator.CreateSelfSignedPfx("CorrectPassword");

            Assert.False(CertificateGenerator.CanOpen(pfxBytes, "WrongPassword"));
        }

        [Fact]
        public void ExportPublicCertificatePem_is_well_formed_PEM()
        {
            var pfxBytes = CertificateGenerator.CreateSelfSignedPfx("TestPassword123!");

            var pem = CertificateGenerator.ExportPublicCertificatePem(pfxBytes, "TestPassword123!");

            Assert.StartsWith("-----BEGIN CERTIFICATE-----\r\n", pem);
            Assert.EndsWith("-----END CERTIFICATE-----\r\n", pem);
        }

        [Fact]
        public void ExportPublicCertificatePem_round_trips_to_the_same_certificate_without_a_private_key()
        {
            var pfxBytes = CertificateGenerator.CreateSelfSignedPfx("TestPassword123!");
            using (var original = new X509Certificate2(pfxBytes, "TestPassword123!"))
            {
                var pem = CertificateGenerator.ExportPublicCertificatePem(pfxBytes, "TestPassword123!");
                var body = pem
                    .Replace("-----BEGIN CERTIFICATE-----", string.Empty)
                    .Replace("-----END CERTIFICATE-----", string.Empty);
                var der = Convert.FromBase64String(body);

                using (var roundTripped = new X509Certificate2(der))
                {
                    Assert.False(roundTripped.HasPrivateKey);
                    Assert.Equal(original.Thumbprint, roundTripped.Thumbprint);
                }
            }
        }
    }
}
