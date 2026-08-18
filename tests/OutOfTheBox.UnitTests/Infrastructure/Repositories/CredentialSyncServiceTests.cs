// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Domain.Repositories;
using OutOfTheBox.Infrastructure.Repositories;
using OutOfTheBox.UnitTests.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.UnitTests.Infrastructure.Repositories;

/// <summary>
/// Covers <see cref="CredentialSyncService"/>'s git-host sweep against a scripted fake
/// <see cref="IProcessRunner"/> (no real <c>git.exe</c>) and its row-selection logic for the NuGet
/// feed sweep (Azure DevOps Artifacts rows skipped, undecryptable rows skipped) - never the generic
/// feed mechanism's real writes to this machine's own NuGet configuration, per this project's "no
/// real process/IO spawning in UnitTests" convention (see <c>NuGetFeedCredentialStoreTests</c>' own
/// doc comment); that real read/write round-trip is a BehaviorTests concern instead.
/// </summary>
public sealed class CredentialSyncServiceTests : IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbContextFactory = new();
    private readonly ServiceProvider _serviceProvider;
    private readonly DpapiCredentialProtector _credentialProtector = new(NullLogger<DpapiCredentialProtector>.Instance);
    private readonly FakeCredentialEventBus _credentialEventBus = new();

    public CredentialSyncServiceTests()
    {
        var services = new ServiceCollection();
        services.AddTransient(_ => _dbContextFactory.CreateContext());
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task SyncAllOnceAsync_repairs_a_git_host_whose_credential_helper_entry_is_missing()
    {
        await SeedGitHostAsync("github.com", "s3cr3t-pat");
        var processRunner = new ScriptedGitCredentialProcessRunner();

        await CreateService(processRunner).SyncAllOnceAsync(CancellationToken.None);

        Assert.True(processRunner.TryGetStored("github.com", out var stored));
        Assert.Equal("s3cr3t-pat", stored);
        Assert.Equal(1, _credentialEventBus.PublishCount);
    }

    [Fact]
    public async Task SyncAllOnceAsync_does_not_re_approve_a_git_host_whose_credential_helper_entry_already_matches()
    {
        await SeedGitHostAsync("github.com", "s3cr3t-pat");
        var processRunner = new ScriptedGitCredentialProcessRunner();
        processRunner.Seed("github.com", "s3cr3t-pat");

        await CreateService(processRunner).SyncAllOnceAsync(CancellationToken.None);

        Assert.Equal(0, processRunner.ApproveCallCount);
        Assert.Equal(0, _credentialEventBus.PublishCount);
    }

    [Fact]
    public async Task SyncAllOnceAsync_repairs_other_hosts_when_one_hosts_sync_throws()
    {
        await SeedGitHostAsync("broken.example.com", "broken-pat");
        await SeedGitHostAsync("github.com", "s3cr3t-pat");
        var processRunner = new ThrowingForHostProcessRunner("broken.example.com");

        await CreateService(processRunner).SyncAllOnceAsync(CancellationToken.None);

        Assert.True(processRunner.TryGetStored("github.com", out var stored));
        Assert.Equal("s3cr3t-pat", stored);
    }

    [Fact]
    public async Task SyncAllOnceAsync_skips_a_git_host_row_that_cannot_be_decrypted()
    {
        await using var dbContext = _dbContextFactory.CreateContext();
        dbContext.GitHostAuthorizations.Add(new GitHostAuthorization("github.com", DateTimeOffset.UtcNow, [1, 2, 3]));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var processRunner = new ScriptedGitCredentialProcessRunner();

        await CreateService(processRunner).SyncAllOnceAsync(CancellationToken.None);

        Assert.False(processRunner.TryGetStored("github.com", out _));
        Assert.Equal(0, _credentialEventBus.PublishCount);
    }

    [Fact]
    public async Task SyncAllOnceAsync_skips_an_Azure_DevOps_Artifacts_feed_without_touching_NuGet_configuration()
    {
        await using (var dbContext = _dbContextFactory.CreateContext())
        {
            dbContext.NuGetFeedAuthorizations.Add(new NuGetFeedAuthorization(
                "https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json",
                DateTimeOffset.UtcNow,
                _credentialProtector.Encrypt("s3cr3t-pat")));
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        // No real NuGet configuration IO happens for this row (it's Azure DevOps Artifacts, skipped
        // entirely) - if it did, this would still run in CI but would mutate the real machine's NuGet
        // config, which this project's convention forbids in UnitTests.
        await CreateService(new ScriptedGitCredentialProcessRunner()).SyncAllOnceAsync(CancellationToken.None);

        Assert.Equal(0, _credentialEventBus.PublishCount);
    }

    [Fact]
    public async Task SyncAllOnceAsync_skips_a_NuGet_feed_row_that_cannot_be_decrypted()
    {
        await using (var dbContext = _dbContextFactory.CreateContext())
        {
            dbContext.NuGetFeedAuthorizations.Add(new NuGetFeedAuthorization("https://example.com/feed/index.json", DateTimeOffset.UtcNow, [1, 2, 3]));
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await CreateService(new ScriptedGitCredentialProcessRunner()).SyncAllOnceAsync(CancellationToken.None);

        Assert.Equal(0, _credentialEventBus.PublishCount);
    }

    [Fact]
    public async Task EnsureInitialSyncAsync_actually_runs_the_sweep_not_just_a_future_promise()
    {
        await SeedGitHostAsync("github.com", "s3cr3t-pat");
        var processRunner = new ScriptedGitCredentialProcessRunner();
        var service = CreateService(processRunner);

        // Distinguishes "started and ran" from "merely scheduled" - a caller awaiting this must see
        // the repair actually applied, not just a signal some other, independently-timed loop will
        // eventually set.
        await service.EnsureInitialSyncAsync(CancellationToken.None);

        Assert.True(processRunner.TryGetStored("github.com", out var stored));
        Assert.Equal("s3cr3t-pat", stored);
    }

    [Fact]
    public async Task EnsureInitialSyncAsync_runs_the_sweep_only_once_across_concurrent_callers()
    {
        await SeedGitHostAsync("github.com", "s3cr3t-pat");
        var processRunner = new ScriptedGitCredentialProcessRunner();
        var service = CreateService(processRunner);

        // Simulates RepositoryFetchSampler and this service's own ExecuteAsync loop both reaching
        // this call around the same moment at startup - only one sweep must actually run.
        await Task.WhenAll(
            service.EnsureInitialSyncAsync(CancellationToken.None),
            service.EnsureInitialSyncAsync(CancellationToken.None));

        Assert.Equal(1, processRunner.ApproveCallCount);
    }

    private async Task SeedGitHostAsync(string host, string token)
    {
        await using var dbContext = _dbContextFactory.CreateContext();
        dbContext.GitHostAuthorizations.Add(new GitHostAuthorization(host, DateTimeOffset.UtcNow, _credentialProtector.Encrypt(token)));
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private CredentialSyncService CreateService(IProcessRunner processRunner) => new(
        Options.Create(new ServiceOptions { RootDirectory = Path.GetTempPath(), CredentialSyncIntervalSeconds = 1 }),
        _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
        _credentialProtector,
        _credentialEventBus,
        processRunner,
        new ApplicationLifetime(NullLogger<ApplicationLifetime>.Instance),
        NullLogger<CredentialSyncService>.Instance);

    /// <inheritdoc />
    public void Dispose()
    {
        _serviceProvider.Dispose();
        _dbContextFactory.Dispose();
    }

    /// <summary>Scripts `git credential fill`/`approve`/`reject` against an in-memory host-to-password map, never spawning a real process.</summary>
    private class ScriptedGitCredentialProcessRunner : IProcessRunner
    {
        private readonly Dictionary<string, string> _stored = new(StringComparer.Ordinal);

        public int ApproveCallCount { get; private set; }

        public void Seed(string host, string password) => _stored[host] = password;

        public bool TryGetStored(string host, out string password) => _stored.TryGetValue(host, out password!);

        public virtual async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, IProcessOutputSink outputSink, CancellationToken cancellationToken, Action<int>? onStarted = null)
        {
            if (request.Arguments.Count < 2 || request.Arguments[0] != "credential")
            {
                return new ProcessRunResult(0);
            }

            var fields = ParseFields(request.StandardInput ?? string.Empty);
            var host = fields.GetValueOrDefault("host", string.Empty);

            switch (request.Arguments[1])
            {
                case "fill":
                    if (_stored.TryGetValue(host, out var password))
                    {
                        await outputSink.OnStandardOutputAsync($"password={password}", cancellationToken);
                        return new ProcessRunResult(0);
                    }

                    return new ProcessRunResult(1);

                case "approve":
                    ApproveCallCount++;
                    _stored[host] = fields.GetValueOrDefault("password", string.Empty);
                    return new ProcessRunResult(0);

                case "reject":
                    _stored.Remove(host);
                    return new ProcessRunResult(0);

                default:
                    return new ProcessRunResult(0);
            }
        }

        private static Dictionary<string, string> ParseFields(string input)
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in input.Split('\n'))
            {
                var separatorIndex = line.IndexOf('=');
                if (separatorIndex > 0)
                {
                    fields[line[..separatorIndex]] = line[(separatorIndex + 1)..].TrimEnd('\r');
                }
            }

            return fields;
        }
    }

    /// <summary>Behaves exactly like <see cref="ScriptedGitCredentialProcessRunner"/>, except every invocation naming <paramref name="brokenHost"/> throws - proves one host's failure never blocks another's repair.</summary>
    private sealed class ThrowingForHostProcessRunner(string brokenHost) : ScriptedGitCredentialProcessRunner
    {
        public override Task<ProcessRunResult> RunAsync(ProcessRunRequest request, IProcessOutputSink outputSink, CancellationToken cancellationToken, Action<int>? onStarted = null) =>
            (request.StandardInput ?? string.Empty).Contains($"host={brokenHost}", StringComparison.Ordinal)
                ? throw new InvalidOperationException("Simulated failure for " + brokenHost)
                : base.RunAsync(request, outputSink, cancellationToken, onStarted);
    }

    private sealed class FakeCredentialEventBus : ICredentialEventBus
    {
        public int PublishCount { get; private set; }

        public void Publish() => PublishCount++;

        public IDisposable Subscribe(Action handler) => throw new NotSupportedException();
    }

}
