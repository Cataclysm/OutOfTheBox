// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.ComponentModel;
using OutOfTheBox.Application.Mcp;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Domain.Repositories;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace OutOfTheBox.Presentation.Mcp;

/// <summary>
/// MCP <c>authorize_git_host</c>/<c>list_authorized_git_hosts</c>/<c>revoke_git_host_authorization</c>
/// tools, per <c>mcp-git-credentials</c>' spec: lets a caller store, list, and revoke PAT-based git
/// credentials for a remote host, so <c>git_run</c>/<c>clone_repository</c> can authenticate against
/// it transparently with no change to either tool's own contract. All three reuse
/// <see cref="IGitCredentialStore"/> directly - the same port the dashboard's Credentials page (add/
/// edit) and clone-retry PAT prompt call, so the surfaces can never end up with divergent
/// credentials for the same host.
/// </summary>
[McpServerToolType]
public sealed class GitCredentialsMcpTools(IGitCredentialStore gitCredentialStore, IMcpPermissionStore permissionStore)
{
    /// <summary>Stores a personal access token for a remote host.</summary>
    /// <param name="host">The remote host, e.g. <c>github.com</c> or <c>dev.azure.com</c>.</param>
    /// <param name="token">The personal access token.</param>
    [McpServerTool]
    [Description("Stores a personal access token (PAT) for a remote git host (e.g. \"github.com\", \"dev.azure.com\"), so subsequent git_run/clone_repository calls against an https://{host}/... remote authenticate transparently. Verifies the credential is actually retrievable before reporting success. Re-authorizing a host replaces its existing credential. Requires a git credential helper to be configured on this system.")]
    public async Task<McpAuthorizeGitHostResult> AuthorizeGitHostAsync(
        [Description("The remote host, e.g. \"github.com\" or \"dev.azure.com\".")] string host,
        [Description("The personal access token.")] string token)
    {
        if (!permissionStore.IsEnabled("authorize_git_host"))
        {
            throw new McpException("The 'authorize_git_host' tool is currently disabled in MCP Settings.");
        }

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(token))
        {
            throw new McpException("host and token must both be supplied.");
        }

        var result = await gitCredentialStore.AuthorizeAsync(host, token, CancellationToken.None);

        return result switch
        {
            GitCredentialAuthorizeResult.Succeeded => new McpAuthorizeGitHostResult(true),
            GitCredentialAuthorizeResult.NoCredentialHelperConfigured =>
                throw new McpException("No git credential helper is configured on this system - reinstall Git for Windows, or run `git config --global credential.helper manager`."),
            GitCredentialAuthorizeResult.VerificationFailed =>
                throw new McpException($"The credential for '{host}' was written but could not be verified as retrievable - the write may not have persisted."),
            GitCredentialAuthorizeResult.GitUnreachable gitUnreachable =>
                throw new McpException($"git could not be started: {gitUnreachable.ErrorMessage}"),
            _ => throw new McpException("Unexpected result authorizing the git host."),
        };
    }

    /// <summary>Lists every host with a stored credential, and whether it currently appears to be working.</summary>
    [McpServerTool]
    [Description("Lists every remote host with a credential authorized via authorize_git_host, with when it was authorized and its current health (whether the most recent pull/push/force-push/fetch/clone against it succeeded or failed for an authentication reason). Never includes the token itself, which this service writes once and never reads back.")]
    public Task<IReadOnlyList<GitHostAuthorizationSummary>> ListAuthorizedGitHostsAsync()
    {
        if (!permissionStore.IsEnabled("list_authorized_git_hosts"))
        {
            throw new McpException("The 'list_authorized_git_hosts' tool is currently disabled in MCP Settings.");
        }

        return gitCredentialStore.ListAuthorizedHostsAsync(CancellationToken.None);
    }

    /// <summary>Removes a host's stored credential.</summary>
    /// <param name="host">The remote host to revoke.</param>
    [McpServerTool]
    [Description("Removes a remote host's git credential, both from the git credential helper and this service's own record of it. Does not, and cannot, invalidate the personal access token itself on the remote host - that has to be done there directly.")]
    public async Task<McpRevokeGitHostResult> RevokeGitHostAuthorizationAsync(
        [Description("The remote host to revoke.")] string host)
    {
        if (!permissionStore.IsEnabled("revoke_git_host_authorization"))
        {
            throw new McpException("The 'revoke_git_host_authorization' tool is currently disabled in MCP Settings.");
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            throw new McpException("host must be supplied.");
        }

        var result = await gitCredentialStore.RevokeAsync(host, CancellationToken.None);

        return result switch
        {
            GitCredentialRevokeResult.Revoked => new McpRevokeGitHostResult(true),
            GitCredentialRevokeResult.NothingToRevoke =>
                throw new McpException($"'{host}' has no credential authorized via authorize_git_host - nothing to revoke."),
            GitCredentialRevokeResult.GitUnreachable gitUnreachable =>
                throw new McpException($"git could not be started: {gitUnreachable.ErrorMessage}"),
            _ => throw new McpException("Unexpected result revoking the git host credential."),
        };
    }
}

/// <summary>The result of a successful <c>authorize_git_host</c> call.</summary>
/// <param name="Authorized">Always <see langword="true"/> - a failure throws instead of returning a result with this <see langword="false"/>.</param>
public sealed record McpAuthorizeGitHostResult(bool Authorized);

/// <summary>The result of a successful <c>revoke_git_host_authorization</c> call.</summary>
/// <param name="Revoked">Always <see langword="true"/> - a failure throws instead of returning a result with this <see langword="false"/>.</param>
public sealed record McpRevokeGitHostResult(bool Revoked);
