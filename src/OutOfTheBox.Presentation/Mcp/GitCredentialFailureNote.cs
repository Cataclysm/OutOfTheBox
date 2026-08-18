// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using System.ComponentModel;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Domain.Repositories;

namespace OutOfTheBox.Presentation.Mcp;

/// <summary>
/// Shared by <c>CommandExecutionMcpTools</c> (<c>git_run</c>) and <c>RepositoryAccessMcpTools</c>
/// (<c>clone_repository</c>) to append a specific, actionable note to a failed run's stderr when it
/// classifies as an authentication failure, per <c>mcp-git-credentials</c>' "A git_run/
/// clone_repository failure that looks like an authentication problem names it as one" requirement.
/// Lives in Presentation, not Infrastructure, since this project's Presentation layer has no
/// reference to Infrastructure at all - the small "git remote get-url origin" capture below is a
/// deliberate, narrow duplication of <c>GitCaptureRunner</c>'s own technique (Infrastructure-internal,
/// unreachable from here), the same reasoning <c>RepositoryAccessMcpTools</c>' own doc comment
/// already gives for reimplementing clone entirely rather than reusing <c>RepositoryManager</c>.
/// </summary>
internal static class GitCredentialFailureNote
{
    /// <summary>
    /// Returns <paramref name="stderr"/> unchanged unless it classifies as an authentication
    /// failure, in which case a note identifying <paramref name="host"/>'s current authorization
    /// state (never authorized, versus authorized-but-no-longer-working) is appended.
    /// </summary>
    public static async Task<string?> AppendIfAuthFailureAsync(IGitCredentialStore gitCredentialStore, string host, string? stderr, CancellationToken cancellationToken)
    {
        if (!GitAuthFailureClassifier.IsLikelyAuthFailure(stderr))
        {
            return stderr;
        }

        var normalizedHost = host.Trim().ToLowerInvariant();
        var authorizedHosts = await gitCredentialStore.ListAuthorizedHostsAsync(cancellationToken);
        var authorization = authorizedHosts.FirstOrDefault(h => h.Host == normalizedHost);

        var note = authorization is null
            ? $"This looks like an authentication failure against '{host}' - no credential is currently authorized for it. Call authorize_git_host to set one up."
            : $"This looks like an authentication failure against '{host}' - a credential was authorized for it on {authorization.AuthorizedAtUtc:yyyy-MM-dd}, but is no longer working. It may have expired or been revoked - call authorize_git_host to replace it.";

        return string.IsNullOrEmpty(stderr) ? note : $"{stderr}\n\n{note}";
    }

    /// <summary>
    /// Resolves <paramref name="workingDirectory"/>'s <c>origin</c> remote to a host, or
    /// <see langword="null"/> if there is none or it doesn't resolve - a narrow, Presentation-local
    /// capture (see this class's own remarks for why it can't call into Infrastructure).
    /// </summary>
    public static async Task<string?> TryResolveOriginHostAsync(IProcessRunner processRunner, string workingDirectory, CancellationToken cancellationToken)
    {
        var sink = new CapturingOutputSink();

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

            var result = await processRunner.RunAsync(new ProcessRunRequest(["remote", "get-url", "origin"], workingDirectory, "git"), sink, linkedCts.Token);
            if (result.ExitCode != 0)
            {
                return null;
            }
        }
        catch (OperationCanceledException)
        {
            // Timed out - no host to enrich with; the caller's own failure is reported as-is.
            return null;
        }
        catch (Win32Exception)
        {
            // git.exe unreachable - same reasoning as the timeout case above.
            return null;
        }

        return GitRemoteUrlParser.TryGetHost(sink.Stdout.Trim(), out var host) ? host : null;
    }

    private sealed class CapturingOutputSink : IProcessOutputSink
    {
        private readonly System.Text.StringBuilder _stdout = new();

        public string Stdout => _stdout.ToString();

        public Task OnStandardOutputAsync(string line, CancellationToken cancellationToken)
        {
            _stdout.AppendLine(line);
            return Task.CompletedTask;
        }

        public Task OnStandardErrorAsync(string line, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
