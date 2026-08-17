// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Collections.Concurrent;
using OutOfTheBox.Application.Mcp;
using OutOfTheBox.Domain.Mcp;
using OutOfTheBox.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace OutOfTheBox.Infrastructure.Mcp;

/// <inheritdoc cref="IMcpPermissionStore" />
/// <remarks>
/// Registered singleton - resolves its own scoped <see cref="OutOfTheBoxDbContext"/> per call via
/// <see cref="IServiceScopeFactory"/>, the same pattern <c>GitCredentialStore</c>/<c>CredentialSyncService</c>
/// already use for a singleton needing scoped database access. The in-memory
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> is the same "cache backed by the database, refreshed
/// on write rather than polled" shape <c>RepositoryStatsCache</c> already establishes.
/// </remarks>
public sealed class McpPermissionStore(IServiceScopeFactory serviceScopeFactory) : IMcpPermissionStore
{
    private readonly ConcurrentDictionary<string, bool> _cache = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutOfTheBoxDbContext>();

        var existing = await dbContext.McpToolPermissions.AsNoTracking().ToDictionaryAsync(p => p.Key, p => p.Enabled, StringComparer.Ordinal, cancellationToken);

        var seeded = new List<McpToolPermissionEntry>();
        foreach (var key in McpToolCatalog.AllKeys())
        {
            if (existing.TryGetValue(key, out var enabled))
            {
                _cache[key] = enabled;
                continue;
            }

            enabled = McpToolCatalog.DefaultEnabled(key);
            _cache[key] = enabled;
            seeded.Add(new McpToolPermissionEntry(key, enabled));
        }

        if (seeded.Count > 0)
        {
            dbContext.McpToolPermissions.AddRange(seeded);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public bool IsEnabled(string key) => _cache.TryGetValue(key, out var enabled) && enabled;

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, bool>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>(_cache, StringComparer.Ordinal));

    /// <inheritdoc />
    public async Task SetEnabledAsync(string key, bool enabled, CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutOfTheBoxDbContext>();

        var existing = await dbContext.McpToolPermissions.FirstOrDefaultAsync(p => p.Key == key, cancellationToken);
        if (existing is null)
        {
            dbContext.McpToolPermissions.Add(new McpToolPermissionEntry(key, enabled));
        }
        else
        {
            dbContext.Entry(existing).CurrentValues.SetValues(new McpToolPermissionEntry(key, enabled));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // Updated only after the database write succeeds, so a failed persist never leaves the
        // in-memory cache and the durable record disagreeing about what's actually enabled.
        _cache[key] = enabled;
    }
}
