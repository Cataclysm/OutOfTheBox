// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Mcp;
using ModelContextProtocol;

namespace OutOfTheBox.Presentation.Mcp;

/// <summary>
/// The one-line "is this tool currently enabled" gate every plain MCP tool (all but
/// <c>dotnet_run</c>/<c>git_run</c>, which gate per-subcommand instead - see
/// <c>CommandExecutionMcpTools.StartRunAsync</c>) checks first, before doing anything else. Extracted
/// once the same three lines had been copy-pasted at the top of eighteen tool methods across nine
/// files, each repeating the exact same message text.
/// </summary>
internal static class McpPermissionGate
{
    /// <summary>Throws an <see cref="McpException"/> naming <paramref name="key"/> if it's currently disabled in MCP Settings; otherwise does nothing.</summary>
    public static void EnsureEnabled(this IMcpPermissionStore permissionStore, string key)
    {
        if (!permissionStore.IsEnabled(key))
        {
            throw new McpException($"The '{key}' tool is currently disabled in MCP Settings - call get_mcp_permissions to see the current allowed set.");
        }
    }
}
