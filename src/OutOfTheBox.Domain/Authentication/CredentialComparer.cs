// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using System.Security.Cryptography;
using System.Text;

namespace OutOfTheBox.Domain.Authentication;

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
