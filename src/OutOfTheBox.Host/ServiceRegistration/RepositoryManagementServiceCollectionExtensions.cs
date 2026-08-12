// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Infrastructure.Repositories;

namespace OutOfTheBox.Host.ServiceRegistration;

/// <summary>
/// Registers repository listing/clone/delete/git-actions (dashboard-only - no REST/MCP surface,
/// called directly from Blazor component code-behind, except list/clone/delete which
/// <c>CommandExecutionServiceCollectionExtensions</c>' MCP tools also reach) and the file tree
/// browser (both the dashboard's own tree and, per specs/mcp-file-management, the MCP
/// <c>find_files</c>/<c>get_file_info</c>/<c>delete_path</c> tools).
/// </summary>
public static class RepositoryManagementServiceCollectionExtensions
{
    /// <summary>Adds repository management and the file tree browser.</summary>
    public static IServiceCollection AddRepositoryManagementServices(this IServiceCollection services)
    {
        // IRepositoryManager is scoped (it depends on the scoped IRunRepository); the cache and stats
        // provider are process-wide singletons, the same reasoning as RunRegistry/IRunEventBus in
        // CommandExecutionServiceCollectionExtensions.
        services.AddSingleton<RepositoryStatsCache>();
        services.AddSingleton<IRepositoryStatsProvider, GitRepositoryStatsProvider>();
        services.AddSingleton<IRepositoryStatsEventBus, InMemoryRepositoryStatsEventBus>();

        // Singleton, not scoped, despite persisting via the scoped OutOfTheBoxDbContext - it resolves
        // a fresh scope/DbContext per call internally (see GitCredentialStore's own remarks), since
        // GitRepositoryStatsProvider (itself singleton, consumed by the singleton-lifetime
        // RepositoryStatsSampler background service) depends on it too and can't consume a scoped
        // service directly.
        services.AddSingleton<IGitCredentialStore, GitCredentialStore>();

        // Same singleton-resolves-its-own-scope reasoning as IGitCredentialStore just above -
        // CommandExecutionMcpTools' dotnet_run spawn path also depends on it, per design.md's
        // "dotnet_run gains environment-variable injection" decision.
        services.AddSingleton<INuGetFeedCredentialStore, NuGetFeedCredentialStore>();

        // So the dashboard's Credentials page can react live to an authorize/revoke made through
        // either credential store, regardless of caller (MCP tool or the page's own dialogs).
        services.AddSingleton<ICredentialEventBus, InMemoryCredentialEventBus>();

        // Machine-scoped (not account-scoped) so the DB-side copy of a credential both credential
        // stores now persist survives this service's dedicated account being recreated - see
        // ICredentialProtector's own remarks.
        services.AddSingleton<ICredentialProtector, DpapiCredentialProtector>();

        services.AddScoped<IRepositoryManager, RepositoryManager>();
        services.AddHostedService<RepositoryStatsSampler>();
        services.AddHostedService<RepositoryFetchSampler>();

        // Periodically re-derives git's credential helper and this machine's NuGet configuration from
        // the DB (now the durable source of truth for both) - repairs whatever the OS-level store lost.
        services.AddHostedService<CredentialSyncService>();

        // File tree browser (Section 23) - scoped, not singleton, for the same reason
        // IRepositoryManager just above is: it now depends on the scoped IRunRepository too
        // (DeleteAsync's own history recording, added alongside FindEntriesAsync/GetMetadataAsync).
        services.AddScoped<IRepositoryFileBrowser, RepositoryFileBrowser>();

        return services;
    }
}
