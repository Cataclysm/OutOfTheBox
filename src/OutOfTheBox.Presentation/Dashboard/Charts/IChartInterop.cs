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
    /// <summary>Creates a line chart in the canvas identified by <paramref name="canvasId"/>, with one dataset per label. <paramref name="showLegend"/> is false for a many-line chart (per-core CPU) where a dataset-per-line legend is just noise, not information.</summary>
    ValueTask CreateLineChartAsync(string canvasId, IReadOnlyList<string> datasetLabels, ChartValueFormat yAxisFormat = ChartValueFormat.None, bool showLegend = true);

    /// <summary>Appends one point to the given dataset and redraws without animation.</summary>
    ValueTask PushPointAsync(string canvasId, int datasetIndex, DateTimeOffset timestamp, double value);

    /// <summary>Replaces a dataset's entire point series in one call - used for a full-duration history graph, which isn't fed incrementally.</summary>
    ValueTask SetSeriesAsync(string canvasId, int datasetIndex, IEnumerable<(DateTimeOffset Timestamp, double Value)> points);

    /// <summary>Destroys the chart instance and forgets its canvas id.</summary>
    ValueTask DestroyAsync(string canvasId);
}
