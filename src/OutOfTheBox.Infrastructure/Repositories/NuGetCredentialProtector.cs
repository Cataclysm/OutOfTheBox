// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Security.Cryptography;
using System.Text;

namespace OutOfTheBox.Infrastructure.Repositories;

/// <summary>
/// DPAPI encryption for an Azure DevOps Artifacts feed's PAT before it is persisted by
/// <see cref="NuGetFeedCredentialStore"/> - see design.md's "durable storage for the Azure DevOps
/// Artifacts mechanism" decision for why this service persists this one class of secret itself
/// (unlike the generic-feed mechanism, whose secret lives only in the NuGet configuration, encrypted
/// by NuGet's own <c>EncryptionUtility</c>, never here). User-scoped (<see cref="DataProtectionScope.CurrentUser"/>)
/// - whether this behaves correctly under this service's dedicated, possibly-profile-less service
/// account is an unverified risk noted in design.md, not assumed here.
/// </summary>
public static class NuGetCredentialProtector
{
    /// <summary>Encrypts <paramref name="plaintext"/> for storage.</summary>
    public static byte[] Encrypt(string plaintext) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), optionalEntropy: null, DataProtectionScope.CurrentUser);

    /// <summary>Decrypts a value previously produced by <see cref="Encrypt"/>.</summary>
    public static string Decrypt(byte[] ciphertext) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(ciphertext, optionalEntropy: null, DataProtectionScope.CurrentUser));
}
