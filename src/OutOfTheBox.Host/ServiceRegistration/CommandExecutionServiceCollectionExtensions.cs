// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using OutOfTheBox.Application.Concurrency;
using OutOfTheBox.Application.Diagnostics;
using OutOfTheBox.Application.Events;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Mcp;
using OutOfTheBox.Infrastructure.Diagnostics;
using OutOfTheBox.Infrastructure.Events;
using OutOfTheBox.Infrastructure.Execution;
using OutOfTheBox.Infrastructure.Mcp;
using OutOfTheBox.Presentation.Mcp;

namespace OutOfTheBox.Host.ServiceRegistration;

/// <summary>
/// Registers the MCP server (sbx-mcp-server) and the process-execution infrastructure its tools
/// (and the dashboard's own git/dotnet actions) run on top of.
/// </summary>
public static class CommandExecutionServiceCollectionExtensions
{
    /// <summary>Adds the MCP server, its tool assembly, and the shared process-execution/run-tracking services.</summary>
    public static IServiceCollection AddCommandExecutionServices(this IServiceCollection services)
    {
        // MCP server (sbx-mcp-server) - this service's sole command-execution/file-transfer/repository-access
        // entry point for the sbx sandbox caller, reached over Streamable HTTP at /mcp. Stateless mode:
        // every tool call here is already independently bearer-token-authenticated and none of this
        // service's tools need a server-initiated request back to the caller (sampling/elicitation), so
        // there is nothing a stateful MCP session would buy that plain per-call statelessness doesn't
        // already have.
        services.AddMcpServer()
            .WithHttpTransport(options => options.Stateless = true)
            .WithToolsFromAssembly(typeof(CommandExecutionMcpTools).Assembly);

        // Process-wide in-memory store of MCP-started runs' output, polled by read_run_output - same
        // singleton-lifetime reasoning as RunRegistry below (Section 2 of sbx-mcp-server).
        services.AddSingleton<McpRunOutputRegistry>();

        // Infrastructure implementations registered against Application's ports - Host (via this
        // project's own ServiceRegistration extension methods) is the one place allowed to reference
        // both Infrastructure and Presentation; neither references the other directly.
        services.AddSingleton<IWorkingDirectoryResolver, WorkingDirectoryResolver>();
        services.AddSingleton<IPathSanitizer, PathSanitizer>();
        services.AddSingleton<IProcessRunner, CliProcessRunner>();
        services.AddSingleton<IInstalledToolVersionsProvider, InstalledToolVersionsProvider>();
        services.AddSingleton<IEnvironmentInfoProvider, EnvironmentInfoProvider>();
        services.AddSingleton<IFileLockInspector, RestartManagerFileLockInspector>();

        // Backs every [McpServerTool]'s own enabled-check plus the MCP Settings dashboard page -
        // loaded once at startup via LoadMcpPermissionsAsync (Program.cs), read synchronously
        // in-memory from then on.
        services.AddSingleton<IMcpPermissionStore, McpPermissionStore>();

        // Process-wide in-memory state - must be a singleton, not scoped/transient, or the per-repository
        // lock would be meaningless (each request would get its own empty registry).
        services.AddSingleton<RunRegistry>();

        // Also process-wide/in-memory, for the same reason - one bus shared by every publisher and every
        // dashboard subscriber, not one per request/circuit.
        services.AddSingleton<IRunEventBus, InMemoryRunEventBus>();

        return services;
    }
}
