// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System;
using System.IO;
using WixToolset.Dtf.WindowsInstaller;

namespace OutOfTheBox.Msi.CustomActions
{
    /// <summary>
    /// MSI custom action entry point wrapping <see cref="SecretResolution"/>: reuses whatever was
    /// already configured on a prior install (found via a RegistrySearch into
    /// EXISTINGBEARERTOKEN/EXISTINGSERVICEACCOUNTPASSWORD/EXISTINGCERTPASSWORD, run during
    /// AppSearch before this action), generates a cryptographically random value on a genuinely
    /// fresh install with nothing supplied via the command line, and leaves an operator-supplied
    /// value alone either way. Scheduled after AppSearch and before the config dialog /
    /// InstallExecuteSequence needs any of these properties.
    ///
    /// Also generates the HTTPS certificate the service binds Kestrel to (see
    /// <see cref="CertificateGenerator"/>) - a real, previously-unaddressed gap: without this, the
    /// service crashed on every single install with "No server certificate was specified," since
    /// nothing ever configured one. The data directory path is computed directly (not read from
    /// the MSI's own DATADIRFOLDER property) because that property isn't reliably resolved this
    /// early in the sequence (Directory-table properties finalize around CostFinalize, well after
    /// this action's AppSearch-relative scheduling point) - computing it independently, the same
    /// way Program.cs's own default does, sidesteps that ordering entirely rather than fighting it.
    /// Writing the PFX from an immediate (non-deferred) custom action means it isn't rollback-aware
    /// like component-installed files are - an accepted, proportionate trade-off: a leftover
    /// self-signed cert file after a failed/rolled-back install is inert, not a real problem, and
    /// avoids the CustomActionData marshaling a deferred CA pair would otherwise need.
    /// </summary>
    public static class ResolveSecretsAction
    {
        /// <summary>MSI custom action entry point - see the class summary for behavior.</summary>
        [CustomAction]
        public static ActionResult ResolveSecrets(Session session)
        {
            session["BEARERTOKEN"] = SecretResolution.Resolve(
                session["BEARERTOKEN"], session["EXISTINGBEARERTOKEN"], () => SecretResolution.GenerateToken(32));
            session["SERVICEACCOUNTPASSWORD"] = SecretResolution.Resolve(
                session["SERVICEACCOUNTPASSWORD"], session["EXISTINGSERVICEACCOUNTPASSWORD"], SecretResolution.GeneratePassword);

            var certPassword = SecretResolution.Resolve(
                session["CERTPASSWORD"], session["EXISTINGCERTPASSWORD"], SecretResolution.GeneratePassword);
            session["CERTPASSWORD"] = certPassword;

            var dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OutOfTheBox");
            var certificatePath = Path.Combine(dataDirectory, "outofthebox.pfx");

            // Never regenerate an existing certificate that the resolved password can still open -
            // an upgrade must not silently invalidate a certificate the operator or an sbx caller
            // may have already pinned/trusted, the same "preserve, don't rotate" reasoning
            // SecretResolution already applies to the bearer token and service account password.
            //
            // But "the file exists" alone isn't enough to trust it: EXISTINGCERTPASSWORD is read
            // from PersistedSecretsComponent, an ordinary (non-permanent) registry component that a
            // full uninstall removes - while the PFX itself lives in the data directory, which
            // deliberately survives uninstall (see Program.cs's own dataDirectory comment). An
            // uninstall followed by a reinstall (not an upgrade) therefore leaves the old PFX file
            // on disk with no recoverable password: EXISTINGCERTPASSWORD comes back empty, a fresh
            // certPassword gets generated above, and the file/password pair goes out of sync -
            // Kestrel then fails to start with "The specified network password is not correct."
            // Verifying the password against the actual file (not just checking existence) catches
            // that case and regenerates, exactly as if the file had never existed.
            var hasUsableCertificate = File.Exists(certificatePath)
                && CertificateGenerator.CanOpen(File.ReadAllBytes(certificatePath), certPassword);
            if (!hasUsableCertificate)
            {
                Directory.CreateDirectory(dataDirectory);
                File.WriteAllBytes(certificatePath, CertificateGenerator.CreateSelfSignedPfx(certPassword));
            }

            return ActionResult.Success;
        }
    }
}
