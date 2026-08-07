// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Domain.PathConfinement;

/// <summary>
/// Pure decision rule: given two already-canonicalized absolute paths, is one contained within
/// the other? Resolving a caller-supplied path to its canonical, symlink-followed form is IO and
/// belongs to Infrastructure; this type only makes the containment decision once that's done.
/// </summary>
public static class PathConfinementPolicy
{
    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="candidate"/> is equal to
    /// <paramref name="root"/> or is a descendant of it. Both paths must already be fully
    /// resolved, absolute paths. Comparison is ordinal and case-insensitive, matching Windows
    /// filesystem semantics.
    /// </summary>
    public static bool IsContained(string root, string candidate)
    {
        var normalizedRoot = NormalizeForComparison(root);
        var normalizedCandidate = NormalizeForComparison(candidate);

        if (string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Comparing with a trailing separator is what prevents a sibling directory that merely
        // shares a name prefix (root "C:\repos" vs candidate "C:\repos-evil") from matching.
        var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;

        return normalizedCandidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeForComparison(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
