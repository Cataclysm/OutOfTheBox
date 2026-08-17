// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.ComponentModel;
using OutOfTheBox.Application.Mcp;
using OutOfTheBox.Domain.Mcp;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace OutOfTheBox.Presentation.Mcp;

/// <summary>
/// The <c>get_mcp_permissions</c> MCP tool: lets a caller see which tools/subcommands are currently
/// enabled, per the operator-configurable MCP Settings dashboard page, instead of discovering it only
/// by trial and error (every other tool's own rejection message points back here). A thin read of
/// <see cref="IMcpPermissionStore"/> - no caching, no argument, always current as of the moment it's
/// called.
/// </summary>
[McpServerToolType]
public sealed class McpPermissionsMcpTools(IMcpPermissionStore permissionStore)
{
    /// <summary>Lists every known MCP tool and dotnet_run/git_run subcommand, and whether each is currently enabled.</summary>
    [McpServerTool(Name = "get_mcp_permissions")]
    [Description("Lists every MCP tool this service exposes, and every dotnet_run/git_run subcommand it knows about, with whether each is currently enabled - per the operator-configurable MCP Settings dashboard page. IMPORTANT: this can change at any time while you're working - an operator may enable or disable a tool/subcommand mid-session from the dashboard. Don't cache this result and assume it stays valid; call it again if a call that worked a moment ago starts being rejected, and treat that rejection as a permission having just changed, not a bug. A subcommand entirely absent from this list (not just disabled) is permanently unavailable - no setting can ever enable it.")]
    public Task<McpPermissionsResult> GetMcpPermissionsAsync()
    {
        if (!permissionStore.IsEnabled("get_mcp_permissions"))
        {
            throw new McpException("The 'get_mcp_permissions' tool is currently disabled in MCP Settings.");
        }

        var permissions = McpToolCatalog.AllKeys()
            .Select(key => new McpPermissionState(key, permissionStore.IsEnabled(key)))
            .ToList();

        return Task.FromResult(new McpPermissionsResult(permissions));
    }
}

/// <summary>One entry in a <c>get_mcp_permissions</c> result.</summary>
/// <param name="Key">The tool name (e.g. <c>"delete_path"</c>), or, for a dotnet_run/git_run subcommand, <c>"{executable}:{subcommand}"</c> (e.g. <c>"git:push"</c>).</param>
/// <param name="Enabled">Whether this key is currently enabled.</param>
public sealed record McpPermissionState(string Key, bool Enabled);

/// <summary>The result of a <c>get_mcp_permissions</c> call.</summary>
/// <param name="Permissions">Every known key and its current enabled state.</param>
public sealed record McpPermissionsResult(IReadOnlyList<McpPermissionState> Permissions);
