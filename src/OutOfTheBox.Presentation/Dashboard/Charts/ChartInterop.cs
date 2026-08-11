// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using Microsoft.JSInterop;

namespace OutOfTheBox.Presentation.Dashboard.Charts;

/// <inheritdoc />
public sealed class ChartInterop(IJSRuntime jsRuntime) : IChartInterop
{
    /// <inheritdoc />
    public ValueTask CreateLineChartAsync(string canvasId, IReadOnlyList<string> datasetLabels, ChartValueFormat yAxisFormat = ChartValueFormat.None, bool showLegend = true, TimeSpan? liveWindow = null) =>
        jsRuntime.InvokeVoidAsync(
            "outOfTheBoxCharts.createLineChart",
            canvasId,
            datasetLabels,
            yAxisFormat switch
            {
                ChartValueFormat.Bytes => "bytes",
                ChartValueFormat.BytesPerSecond => "bytesPerSecond",
                _ => null,
            },
            showLegend,
            liveWindow?.TotalMilliseconds);

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
        // rather than every caller needing its own try/catch. Two distinct failure modes here,
        // confirmed live (a real deployment's log, not just theorized): JSDisconnectedException for
        // a circuit that connected and later disconnected, and InvalidOperationException ("JavaScript
        // interop calls cannot be issued at this time... component is being statically rendered")
        // for a component disposed during the static prerender pass, before any circuit/interactivity
        // exists at all - a genuinely different timing than disconnection, which the original
        // JSDisconnectedException-only catch didn't cover.
        try
        {
            await jsRuntime.InvokeVoidAsync("outOfTheBoxCharts.destroyChart", canvasId);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or InvalidOperationException)
        {
        }
    }
}
