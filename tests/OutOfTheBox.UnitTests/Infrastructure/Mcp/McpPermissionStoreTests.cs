// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Domain.Mcp;
using OutOfTheBox.Infrastructure.Mcp;
using OutOfTheBox.UnitTests.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace OutOfTheBox.UnitTests.Infrastructure.Mcp;

/// <summary>
/// Exercises <see cref="McpPermissionStore"/> against a real in-memory SQLite database (same
/// <see cref="SqliteInMemoryDbContextFactory"/> pattern already used throughout
/// <c>tests/OutOfTheBox.UnitTests/Infrastructure/Repositories/*Tests.cs</c>).
/// </summary>
public sealed class McpPermissionStoreTests : IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbContextFactory = new();
    private readonly ServiceProvider _serviceProvider;

    public McpPermissionStoreTests()
    {
        var services = new ServiceCollection();
        services.AddTransient(_ => _dbContextFactory.CreateContext());
        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _dbContextFactory.Dispose();
    }

    private McpPermissionStore CreateStore() => new(_serviceProvider.GetRequiredService<IServiceScopeFactory>());

    [Fact]
    public async Task LoadAsync_seeds_every_catalog_key_with_its_default_on_a_fresh_database()
    {
        var store = CreateStore();

        await store.LoadAsync(CancellationToken.None);

        Assert.True(store.IsEnabled("find_files"));
        Assert.True(store.IsEnabled("dotnet:build"));
        Assert.False(store.IsEnabled("dotnet:publish"));
        Assert.True(store.IsEnabled("git:status"));
        Assert.False(store.IsEnabled("git:push"));
    }

    [Theory]
    [InlineData("delete_repository")]
    [InlineData("clone_repository")]
    [InlineData("delete_path")]
    [InlineData("authorize_git_host")]
    [InlineData("revoke_git_host_authorization")]
    [InlineData("authorize_nuget_feed")]
    [InlineData("revoke_nuget_feed_authorization")]
    public async Task LoadAsync_seeds_every_mutating_plain_tool_as_disabled(string key)
    {
        var store = CreateStore();

        await store.LoadAsync(CancellationToken.None);

        Assert.False(store.IsEnabled(key));
    }

    [Theory]
    [InlineData("read_run_output")]
    [InlineData("cancel_run")]
    [InlineData("get_run_resources")]
    [InlineData("list_repositories")]
    [InlineData("find_files")]
    [InlineData("get_file_info")]
    [InlineData("transfer_file")]
    [InlineData("get_file_lock_info")]
    [InlineData("list_authorized_git_hosts")]
    [InlineData("list_authorized_nuget_feeds")]
    [InlineData("get_environment_info")]
    public async Task LoadAsync_seeds_every_read_only_plain_tool_and_cancel_run_as_enabled(string key)
    {
        var store = CreateStore();

        await store.LoadAsync(CancellationToken.None);

        Assert.True(store.IsEnabled(key));
    }

    [Fact]
    public async Task LoadAsync_persists_the_seeded_defaults_so_a_fresh_store_sees_the_same_rows()
    {
        await CreateStore().LoadAsync(CancellationToken.None);

        var all = await CreateStore().GetAllAsync(CancellationToken.None);
        // Not populated until LoadAsync runs on this second instance too - GetAllAsync only reports
        // this instance's own in-memory cache, matching IsEnabled's own "never awaits the database"
        // contract; re-load to confirm the earlier seed actually reached the database.
        Assert.Empty(all);

        var reloaded = CreateStore();
        await reloaded.LoadAsync(CancellationToken.None);
        Assert.True(reloaded.IsEnabled("find_files"));
        Assert.False(reloaded.IsEnabled("dotnet:publish"));
    }

    [Fact]
    public async Task SetEnabledAsync_updates_the_cache_immediately_and_persists_across_a_reload()
    {
        var store = CreateStore();
        await store.LoadAsync(CancellationToken.None);
        Assert.True(store.IsEnabled("find_files"));

        await store.SetEnabledAsync("find_files", false, CancellationToken.None);
        Assert.False(store.IsEnabled("find_files"));

        var reloaded = CreateStore();
        await reloaded.LoadAsync(CancellationToken.None);
        Assert.False(reloaded.IsEnabled("find_files"));
    }

    [Fact]
    public async Task SetEnabledAsync_can_turn_a_default_disabled_subcommand_on()
    {
        var store = CreateStore();
        await store.LoadAsync(CancellationToken.None);
        Assert.False(store.IsEnabled("dotnet:publish"));

        await store.SetEnabledAsync("dotnet:publish", true, CancellationToken.None);

        Assert.True(store.IsEnabled("dotnet:publish"));
    }

    [Fact]
    public void IsEnabled_returns_false_for_an_unknown_key_before_any_load() =>
        Assert.False(CreateStore().IsEnabled("not_a_real_tool"));

    [Fact]
    public async Task GetAllAsync_reports_every_catalog_key_after_loading()
    {
        var store = CreateStore();
        await store.LoadAsync(CancellationToken.None);

        var all = await store.GetAllAsync(CancellationToken.None);

        foreach (var key in McpToolCatalog.AllKeys())
        {
            Assert.True(all.ContainsKey(key));
        }
    }
}
