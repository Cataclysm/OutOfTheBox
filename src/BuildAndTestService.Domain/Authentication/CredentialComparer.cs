// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Security.Cryptography;
using System.Text;

namespace BuildAndTestService.Domain.Authentication;

/// <summary>
/// Compares a caller-supplied credential against the configured expected credential using a
/// constant-time comparison, so a timing side-channel can't be used to guess the correct value
/// one byte at a time.
/// </summary>
public static class CredentialComparer
{
    /// <summary>
    /// Returns <see langword="true"/> only if <paramref name="provided"/> is non-null, non-empty,
    /// and equal to <paramref name="expected"/>. The comparison itself runs in constant time for
    /// equal-length inputs; a length mismatch is rejected immediately without a timing guarantee,
    /// which is the accepted trade-off since credential length is not the sensitive part.
    /// </summary>
    public static bool Matches(string? provided, string expected)
    {
        if (string.IsNullOrEmpty(provided) || string.IsNullOrEmpty(expected))
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);

        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
