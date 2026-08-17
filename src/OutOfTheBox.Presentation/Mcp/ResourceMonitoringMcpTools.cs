// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.ComponentModel;
using OutOfTheBox.Application.Mcp;
using OutOfTheBox.Application.Monitoring;
using OutOfTheBox.Application.Persistence;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace OutOfTheBox.Presentation.Mcp;

/// <summary>
/// The <c>get_run_resources</c> MCP tool, per <c>mcp-resource-monitoring</c>'s spec: lets a caller
/// poll a run's recent CPU/RAM trend to judge whether it's hung or still working, without dashboard
/// access. Reads the same <see cref="ResourceHistoryBuffer"/> singleton the dashboard's own live
/// resource graphs already read from - this is a second reader, not a new writer; nothing about how
/// that buffer is populated changes. Deliberately does not call <see cref="IResourceSampler.SampleAsync"/>
/// directly (see design.md's "Data source" decision: that call is stateful/delta-based across ticks,
/// and an out-of-band call here would skew the background sampler's own next reading).
/// </summary>
[McpServerToolType]
public sealed class ResourceMonitoringMcpTools(IRunRepository runRepository, ResourceHistoryBuffer historyBuffer, IClock clock, IMcpPermissionStore permissionStore)
{
    /// <summary>Returns a run's recent CPU/RAM trend, plus a derived hung-vs-busy summary.</summary>
    /// <param name="runId">A run id returned by <c>dotnet_run</c>, <c>git_run</c>, or <c>clone_repository</c>.</param>
    [McpServerTool]
    [Description("Returns a run's recent CPU/RAM usage trend (from the trailing ~10-minute window) plus a derived summary - latest CPU%, peak CPU% in the window, and how many seconds it's been idle (CPU at or below a low-activity threshold). Use this to judge whether a still-running dotnet_run/git_run/clone_repository is hung or just slow, instead of guessing from silence in read_run_output. Returns an empty result (not an error) for a run with no samples yet - e.g. one just started, before the first sampler tick.")]
    public async Task<McpGetRunResourcesResult> GetRunResourcesAsync(
        [Description("A run id returned by dotnet_run, git_run, or clone_repository.")] Guid runId)
    {
        if (!permissionStore.IsEnabled("get_run_resources"))
        {
            throw new McpException("The 'get_run_resources' tool is currently disabled in MCP Settings.");
        }

        var run = await runRepository.FindByIdAsync(runId, CancellationToken.None)
            ?? throw new McpException($"Unknown run id '{runId}'.");

        var historyPoints = historyBuffer.Get(runId.ToString());
        var points = historyPoints.Select(p => new McpRunResourcePoint(p.Timestamp, p.CpuPercent, p.RamBytes)).ToList();

        var trendSummary = RunResourceTrend.Compute(historyPoints, clock.UtcNow);
        var trend = trendSummary is null
            ? null
            : new McpRunResourceTrend(trendSummary.LatestCpuPercent, trendSummary.PeakCpuPercent, trendSummary.IdleForSeconds);

        return new McpGetRunResourcesResult(points, trend);
    }
}
