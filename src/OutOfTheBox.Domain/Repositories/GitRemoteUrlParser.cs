// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Domain.Repositories;

/// <summary>
/// Resolves the host a git remote URL points at - pure string parsing, no IO. Handles both a normal
/// absolute URL (<c>https://host/...</c>, <c>http://host/...</c>, <c>ssh://host/...</c>) and the
/// SCP-like form <c>user@host:path</c> that <c>git@github.com:org/repo.git</c>-style URLs use, which
/// is not a valid absolute URI on its own. Used to derive "which host does this credential/clone/push
/// target" automatically rather than asking an operator to type a host separately from a URL they
/// could disagree on, per design.md's "host is parsed from the clone URL" decision.
/// </summary>
public static class GitRemoteUrlParser
{
    /// <summary>Resolves <paramref name="url"/>'s host into <paramref name="host"/>, returning <see langword="false"/> if it can't be determined.</summary>
    public static bool TryGetHost(string? url, out string host)
    {
        host = string.Empty;

        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
        {
            host = uri.Host;
            return true;
        }

        // SCP-like syntax: user@host:path - no scheme, so not a valid absolute URI above.
        var atIndex = url.IndexOf('@');
        if (atIndex <= 0)
        {
            return false;
        }

        var colonIndex = url.IndexOf(':', atIndex + 1);
        if (colonIndex <= atIndex + 1)
        {
            return false;
        }

        host = url[(atIndex + 1)..colonIndex];
        return true;
    }
}
