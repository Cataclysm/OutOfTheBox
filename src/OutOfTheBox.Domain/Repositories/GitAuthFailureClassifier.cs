// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Domain.Repositories;

/// <summary>
/// Classifies a completed git invocation's captured stderr as "likely an authentication failure" or
/// not - the one shared implementation the dashboard's clone-retry/quick-action-tooltip/needs-credential
/// logic and the MCP result-enrichment both use, per design.md's "shared, pure classifier" decision,
/// so the surfaces can't drift on what counts as auth-related. Git exposes no structured "this was
/// an auth error" signal, so this is inherently a heuristic over known message shapes - deliberately
/// conservative (a miss just falls back to today's generic-failure behavior; a false positive would
/// wrongly trigger a PAT prompt, the worse failure mode), per design.md's own risk note.
/// </summary>
public static class GitAuthFailureClassifier
{
    private static readonly string[] Patterns =
    [
        "authentication failed",
        "invalid username or password",
        "could not read username",
        "could not read password",
        "terminal prompts disabled",
        "bad credentials",
        "http basic: access denied",
        "returned error: 401",
        "returned error: 403",
        "requested url returned error: 401",
        "requested url returned error: 403",
    ];

    /// <summary>Whether <paramref name="stderr"/> matches a known git authentication-failure phrasing.</summary>
    public static bool IsLikelyAuthFailure(string? stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return false;
        }

        foreach (var pattern in Patterns)
        {
            if (stderr.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
