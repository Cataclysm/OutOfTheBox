// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using Microsoft.JSInterop;

namespace OutOfTheBox.Presentation.Dashboard.Charts;

/// <inheritdoc />
public sealed class ChartInterop(IJSRuntime jsRuntime) : IChartInterop
{
    /// <inheritdoc />
    public ValueTask CreateLineChartAsync(string canvasId, IReadOnlyList<string> datasetLabels, ChartValueFormat yAxisFormat = ChartValueFormat.None) =>
        jsRuntime.InvokeVoidAsync(
            "outOfTheBoxCharts.createLineChart",
            canvasId,
            datasetLabels,
            yAxisFormat == ChartValueFormat.Bytes ? "bytes" : null);

    /// <inheritdoc />
    public ValueTask PushPointAsync(string canvasId, int datasetIndex, DateTimeOffset timestamp, double value) =>
        jsRuntime.InvokeVoidAsync("outOfTheBoxCharts.pushPoint", canvasId, datasetIndex, timestamp.ToUnixTimeMilliseconds(), value);

    /// <inheritdoc />
    public ValueTask SetSeriesAsync(string canvasId, int datasetIndex, IEnumerable<(DateTimeOffset Timestamp, double Value)> points) =>
        jsRuntime.InvokeVoidAsync(
            "outOfTheBoxCharts.setSeries",
            canvasId,
            datasetIndex,
            points.Select(p => new { timestampMs = p.Timestamp.ToUnixTimeMilliseconds(), value = p.Value }));

    /// <inheritdoc />
    public async ValueTask DestroyAsync(string canvasId)
    {
        // Best-effort: this is normally called from a component's DisposeAsync during page
        // navigation/circuit teardown, where the circuit may already be gone by the time cleanup
        // runs - nothing useful to do about that server-side, so the exception is swallowed here
        // rather than every caller needing its own try/catch.
        try
        {
            await jsRuntime.InvokeVoidAsync("outOfTheBoxCharts.destroyChart", canvasId);
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
