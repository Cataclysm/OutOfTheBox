// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Infrastructure.Repositories;

namespace OutOfTheBox.Host.ServiceRegistration;

/// <summary>
/// Registers repository listing/clone/delete/git-actions and the file tree browser (both
/// dashboard-only - no REST/MCP surface, called directly from Blazor component code-behind).
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
        services.AddScoped<IRepositoryManager, RepositoryManager>();
        services.AddHostedService<RepositoryStatsSampler>();

        // File tree browser (Section 23) - same dashboard-only reasoning as IRepositoryManager above,
        // but has no scoped dependency of its own (only the singleton WorkingDirectoryResolver/
        // RunRegistry), so it's registered singleton rather than scoped.
        services.AddSingleton<IRepositoryFileBrowser, RepositoryFileBrowser>();

        return services;
    }
}
