// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Domain.Repositories;

/// <summary>
/// Parses <c>git log</c>'s <c>%D</c> decoration field (e.g. <c>"HEAD -&gt; main, origin/main, tag: v1.0"</c>)
/// into structured <see cref="CommitRef"/>s. Pure string parsing, no IO - the repository's actual
/// configured remote names are passed in (rather than re-derived here) since that's the only
/// reliable way to tell a remote-tracking branch like <c>origin/main</c> apart from a local branch
/// that happens to contain a slash, like <c>feature/x</c> - both have the same shape in <c>%D</c>'s
/// output.
/// </summary>
public static class GitDecorationParser
{
    private const string TagPrefix = "tag: ";
    private const string HeadArrow = " -> ";

    /// <summary>Parses a comma-separated <c>%D</c> decoration string into <see cref="CommitRef"/>s, skipping the bare <c>HEAD</c> token (detached-HEAD state is surfaced elsewhere, not as a ref).</summary>
    public static IReadOnlyList<CommitRef> Parse(string? decorations, IReadOnlySet<string> remoteNames)
    {
        if (string.IsNullOrWhiteSpace(decorations))
        {
            return [];
        }

        var refs = new List<CommitRef>();

        foreach (var rawToken in decorations.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var token = rawToken.Trim();
            if (token.Length == 0 || token == "HEAD")
            {
                continue;
            }

            if (token.StartsWith(TagPrefix, StringComparison.Ordinal))
            {
                refs.Add(new CommitRef(token[TagPrefix.Length..], CommitRefKind.Tag, IsCurrent: false));
                continue;
            }

            var arrowIndex = token.IndexOf(HeadArrow, StringComparison.Ordinal);
            if (arrowIndex >= 0)
            {
                refs.Add(new CommitRef(token[(arrowIndex + HeadArrow.Length)..], CommitRefKind.LocalBranch, IsCurrent: true));
                continue;
            }

            var slashIndex = token.IndexOf('/');
            var isRemote = slashIndex > 0 && remoteNames.Contains(token[..slashIndex]);
            refs.Add(new CommitRef(token, isRemote ? CommitRefKind.RemoteBranch : CommitRefKind.LocalBranch, IsCurrent: false));
        }

        return refs;
    }
}
