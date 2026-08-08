// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

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
    }
}
