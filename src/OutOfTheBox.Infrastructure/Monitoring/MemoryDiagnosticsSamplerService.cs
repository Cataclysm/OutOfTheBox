// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using System.Diagnostics;
using OutOfTheBox.Application.Concurrency;
using OutOfTheBox.Application.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.Infrastructure.Monitoring;

/// <summary>
/// Background sampler ticking every <see cref="ServiceOptions.MemoryDiagnosticsIntervalSeconds"/>
/// (default 300s): logs one structured, greppable line of process/GC memory figures plus this
/// process's own tracked-state counts, at Information level under the <c>OutOfTheBox</c> category the
/// file log already keeps at that level (see <c>LoggingWebApplicationBuilderExtensions</c>).
/// </summary>
/// <remarks>
/// Exists specifically to build a long time series for diagnosing *where* service memory growth
/// comes from once a build is actually running for a while, rather than guessing from a single
/// point-in-time reading:
/// <list type="bullet">
/// <item><description><c>WorkingSetMb</c>/<c>PrivateMb</c> - what Task Manager/the dashboard's own
/// "service RAM" figure (<see cref="HostResourceSampler"/>) shows.</description></item>
/// <item><description><c>ManagedHeapMb</c>/<c>GcCommittedMb</c> - what the .NET GC itself accounts
/// for. <c>WorkingSetMb - GcCommittedMb</c> is everything the GC does *not* know about: native/COM
/// allocations, OS handles, thread stacks, loaded modules. A growing gap here without
/// <c>ManagedHeapMb</c> growing points at native memory, not a managed leak - the
/// <c>WmiProcessTree</c>/COM-interop growth this sampler was added to help confirm or rule out is
/// exactly this shape.</description></item>
/// <item><description><c>Gen0Mb</c>/<c>Gen1Mb</c>/<c>Gen2Mb</c>/<c>LohMb</c> - which GC generation
/// is actually holding the managed memory, if any is growing. A growing Gen2/LOH with stable
/// Gen0/Gen1 points at long-lived or large objects being retained somewhere, not per-request
/// churn.</description></item>
/// <item><description><c>HandleCount</c>/<c>ThreadCount</c> - a leaked OS handle (e.g. an
/// unreleased COM object or an unclosed Win32 handle) or a runaway thread pool shows up here even
/// when it wouldn't show up as managed-heap growth at all.</description></item>
/// <item><description><c>TrackedRuns</c> - <see cref="RunRegistry.Count"/>: a value that never
/// settles back toward zero between builds points at a run/transfer never releasing its lock.</description></item>
/// </list>
/// </remarks>
public sealed class MemoryDiagnosticsSamplerService(
    IOptions<ServiceOptions> options,
    RunRegistry runRegistry,
    ILogger<MemoryDiagnosticsSamplerService> logger) : BackgroundService
{
    private const double BytesPerMebibyte = 1024 * 1024;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.MemoryDiagnosticsIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        try
        {
            do
            {
                Tick();
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown (stoppingToken cancelled) - not an error.
        }
    }

    /// <summary>
    /// Logs one sample. Public (like <c>HostResourceSamplerService.TickAsync</c>) so tests can
    /// exercise it directly rather than waiting on the real <see cref="PeriodicTimer"/>-driven loop.
    /// Never throws for a sampling failure, for the same reason
    /// <c>HostResourceSamplerService.TickAsync</c> doesn't - an exception escaping
    /// <see cref="ExecuteAsync"/> would stop the entire host by default.
    /// </summary>
    public void Tick()
    {
        try
        {
            TickCore();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to sample memory diagnostics this tick.");
        }
    }

    private void TickCore()
    {
        // Every figure below costs a real syscall/GC introspection call - skip computing any of
        // them if Information logging is disabled for this category, rather than paying for values
        // nothing will read.
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        using var process = Process.GetCurrentProcess();
        process.Refresh();

        var gcInfo = GC.GetGCMemoryInfo();

        // Indexed by position (0=Gen0, 1=Gen1, 2=Gen2, 3=LOH, 4=POH) rather than assuming a fixed
        // array length - degrades gracefully (missing entries just stay 0) rather than throwing if
        // a runtime ever reports fewer.
        var gen0 = gcInfo.GenerationInfo.Length > 0 ? gcInfo.GenerationInfo[0].SizeAfterBytes / BytesPerMebibyte : 0;
        var gen1 = gcInfo.GenerationInfo.Length > 1 ? gcInfo.GenerationInfo[1].SizeAfterBytes / BytesPerMebibyte : 0;
        var gen2 = gcInfo.GenerationInfo.Length > 2 ? gcInfo.GenerationInfo[2].SizeAfterBytes / BytesPerMebibyte : 0;
        var loh = gcInfo.GenerationInfo.Length > 3 ? gcInfo.GenerationInfo[3].SizeAfterBytes / BytesPerMebibyte : 0;

        logger.LogInformation(
            "MemoryDiagnostics WorkingSetMb={WorkingSetMb:F1} PrivateMb={PrivateMb:F1} " +
            "ManagedHeapMb={ManagedHeapMb:F1} GcCommittedMb={GcCommittedMb:F1} " +
            "Gen0Mb={Gen0Mb:F1} Gen1Mb={Gen1Mb:F1} Gen2Mb={Gen2Mb:F1} LohMb={LohMb:F1} " +
            "GcCounts={GcGen0Count}/{GcGen1Count}/{GcGen2Count} " +
            "HandleCount={HandleCount} ThreadCount={ThreadCount} TrackedRuns={TrackedRuns}",
            process.WorkingSet64 / BytesPerMebibyte,
            process.PrivateMemorySize64 / BytesPerMebibyte,
            GC.GetTotalMemory(false) / BytesPerMebibyte,
            gcInfo.TotalCommittedBytes / BytesPerMebibyte,
            gen0,
            gen1,
            gen2,
            loh,
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            process.HandleCount,
            process.Threads.Count,
            runRegistry.Count);
    }
}
