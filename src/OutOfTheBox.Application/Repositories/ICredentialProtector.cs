// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Application.Repositories;

/// <summary>
/// Machine-scoped encryption for a secret this service persists in its own database (a git host's
/// PAT, a NuGet feed's PAT) - deliberately scoped to the machine itself, not to whatever account this
/// service happens to run as: the dedicated <c>svc-outofthebox</c> service account has no guaranteed
/// loaded profile and is recreated (new SID, empty local vault) across a plain uninstall-then-reinstall,
/// so an account/profile-scoped key would not reliably survive the exact events
/// (<see cref="IGitCredentialStore"/>/<see cref="INuGetFeedCredentialStore"/>'s own OS-level stores
/// already don't survive) this DB-backed copy exists to be durable against in the first place.
/// </summary>
public interface ICredentialProtector
{
    /// <summary>Encrypts <paramref name="plaintext"/> for storage.</summary>
    byte[] Encrypt(string plaintext);

    /// <summary>
    /// Decrypts a value previously produced by <see cref="Encrypt"/>. Returns <see langword="false"/>
    /// (never throws) on any decryption failure - e.g. ciphertext produced under a since-migrated-away
    /// key/scope, or a machine key that no longer matches - so a caller treats that as "this credential
    /// is lost and needs re-authorization," not a crash.
    /// </summary>
    bool TryDecrypt(byte[] ciphertext, out string plaintext);
}
