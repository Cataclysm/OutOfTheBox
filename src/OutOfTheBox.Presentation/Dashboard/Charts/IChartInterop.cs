// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Presentation.Dashboard.Charts;

/// <summary>How a chart's y-axis tick labels should be rendered - lets a byte-valued series (RAM) show human-readable units instead of raw numbers, without every caller re-deriving the same formatting.</summary>
public enum ChartValueFormat
{
    /// <summary>Ticks rendered as Chart.js's own default numeric formatting.</summary>
    None,

    /// <summary>Ticks rendered as human-readable byte units (B/KB/MB/GB).</summary>
    Bytes,

    /// <summary>Ticks rendered as human-readable byte-per-second rates (B/s, KB/s, MB/s, GB/s).</summary>
    BytesPerSecond,
}

/// <summary>
/// Abstraction over the Chart.js JS interop calls used by this dashboard's graph components - lets
/// tests substitute a spy instead of driving a real JS engine, the same precedent this project
/// already applies to <c>IRunEventBus</c>/<c>IResourceEventBus</c> for live-update testing, since
/// there's no Blazor-interactive browser test client in this project's toolchain.
/// </summary>
public interface IChartInterop
{
    /// <summary>
    /// Creates a line chart in the canvas identified by <paramref name="canvasId"/>, with one
    /// dataset per label. <paramref name="showLegend"/> is false for a many-line chart (per-core
    /// CPU) where a dataset-per-line legend is just noise, not information. <paramref name="liveWindow"/>
    /// bounds how far back <see cref="PushPointAsync"/> keeps data for this specific chart
    /// client-side, once it's fed incrementally - omitted (<see langword="null"/>) means never trim,
    /// letting the chart's dataset keep growing for as long as points keep arriving (a run's own
    /// full-duration history graph, which still receives live pushes while that run stays in
    /// flight - there is no fixed window to slide, the run's whole recorded duration is the point).
    /// A chart that only wants a fixed rolling window (every Status page graph) passes one
    /// explicitly. Meaningless for a chart never fed via <see cref="PushPointAsync"/> at all (seeded
    /// once via <see cref="SetSeriesAsync"/> and never updated again, e.g. an already-finished run's
    /// history graph).
    /// </summary>
    ValueTask CreateLineChartAsync(string canvasId, IReadOnlyList<string> datasetLabels, ChartValueFormat yAxisFormat = ChartValueFormat.None, bool showLegend = true, TimeSpan? liveWindow = null);

    /// <summary>Appends one point to the given dataset and redraws without animation - trims data older than the chart's own <c>liveWindow</c> (see <see cref="CreateLineChartAsync"/>).</summary>
    ValueTask PushPointAsync(string canvasId, int datasetIndex, DateTimeOffset timestamp, double value);

    /// <summary>Replaces a dataset's entire point series in one call - used for a full-duration history graph, which isn't fed incrementally.</summary>
    ValueTask SetSeriesAsync(string canvasId, int datasetIndex, IEnumerable<(DateTimeOffset Timestamp, double Value)> points);

    /// <summary>Destroys the chart instance and forgets its canvas id.</summary>
    ValueTask DestroyAsync(string canvasId);
}
