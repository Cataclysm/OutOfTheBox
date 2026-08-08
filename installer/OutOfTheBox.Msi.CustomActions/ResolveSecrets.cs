// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using WixToolset.Dtf.WindowsInstaller;

namespace OutOfTheBox.Msi.CustomActions
{
    /// <summary>
    /// MSI custom action entry point wrapping <see cref="SecretResolution"/>: reuses whatever was
    /// already configured on a prior install (found via a RegistrySearch into
    /// EXISTINGBEARERTOKEN/EXISTINGSERVICEACCOUNTPASSWORD, run during AppSearch before this
    /// action), generates a cryptographically random value on a genuinely fresh install with
    /// nothing supplied via the command line, and leaves an operator-supplied value alone either
    /// way. Scheduled after AppSearch and before the config dialog / InstallExecuteSequence needs
    /// either property.
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

            return ActionResult.Success;
        }
    }
}
