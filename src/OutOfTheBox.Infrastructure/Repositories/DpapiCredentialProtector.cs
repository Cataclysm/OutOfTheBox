// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Security.Cryptography;
using System.Text;
using OutOfTheBox.Application.Repositories;
using Microsoft.Extensions.Logging;

namespace OutOfTheBox.Infrastructure.Repositories;

/// <inheritdoc cref="ICredentialProtector" />
/// <remarks>
/// DPAPI with <see cref="DataProtectionScope.LocalMachine"/> - not <see cref="DataProtectionScope.CurrentUser"/>,
/// which is what this codebase used previously (see the superseded <c>NuGetCredentialProtector</c> in
/// git history) and is exactly the scoping this type exists to move away from: a real, reported bug
/// confirmed that a service-account-profile-scoped secret (git's own credential-helper storage) does
/// not survive a plain uninstall-then-reinstall, since the dedicated service account is recreated with
/// a new SID and an empty vault. <see cref="DataProtectionScope.CurrentUser"/> ties a DPAPI key to that
/// same fragile account/profile, so it would not actually fix the durability problem this type's
/// callers persist a DB-side copy to solve - only <see cref="DataProtectionScope.LocalMachine"/> (keyed
/// to the machine itself, decryptable by any sufficiently-privileged process on it regardless of which
/// account) survives that boundary.
/// </remarks>
public sealed class DpapiCredentialProtector(ILogger<DpapiCredentialProtector> logger) : ICredentialProtector
{
    /// <inheritdoc />
    public byte[] Encrypt(string plaintext) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), optionalEntropy: null, DataProtectionScope.LocalMachine);

    /// <inheritdoc />
    public bool TryDecrypt(byte[] ciphertext, out string plaintext)
    {
        try
        {
            plaintext = Encoding.UTF8.GetString(ProtectedData.Unprotect(ciphertext, optionalEntropy: null, DataProtectionScope.LocalMachine));
            return true;
        }
        catch (CryptographicException ex)
        {
            logger.LogWarning(ex, "Could not decrypt a stored credential - it may have been encrypted under a different key/scope (e.g. before this service moved to machine-scoped DPAPI) and needs re-authorization.");
            plaintext = string.Empty;
            return false;
        }
    }
}
