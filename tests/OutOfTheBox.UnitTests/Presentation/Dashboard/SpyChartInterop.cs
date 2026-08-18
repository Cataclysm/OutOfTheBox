// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using OutOfTheBox.Presentation.Dashboard.Charts;

namespace OutOfTheBox.UnitTests.Presentation.Dashboard;

/// <summary>
/// Records every Chart.js interop call instead of driving a real JS engine - shared between
/// <see cref="StatusComponentTests"/> (live graphs) and <see cref="RunDetailComponentTests"/>
/// (full-duration history graphs), the same "spy instead of a browser" precedent this project
/// already applies to <c>SpyProcessMonitor</c>.
/// </summary>
internal sealed class SpyChartInterop : IChartInterop
{
    public List<string> CreatedCanvasIds { get; } = [];

    public List<(string CanvasId, IReadOnlyList<string> DatasetLabels, ChartValueFormat YAxisFormat, bool ShowLegend, TimeSpan? LiveWindow)> CreatedCharts { get; } = [];

    public List<(string CanvasId, int DatasetIndex, DateTimeOffset Timestamp, double Value)> PushedPoints { get; } = [];

    public List<(string CanvasId, int DatasetIndex, List<(DateTimeOffset Timestamp, double Value)> Points)> SeriesSet { get; } = [];

    public List<string> DestroyedCanvasIds { get; } = [];

    public ValueTask CreateLineChartAsync(string canvasId, IReadOnlyList<string> datasetLabels, ChartValueFormat yAxisFormat = ChartValueFormat.None, bool showLegend = true, TimeSpan? liveWindow = null)
    {
        CreatedCanvasIds.Add(canvasId);
        CreatedCharts.Add((canvasId, datasetLabels, yAxisFormat, showLegend, liveWindow));
        return ValueTask.CompletedTask;
    }

    public ValueTask PushPointAsync(string canvasId, int datasetIndex, DateTimeOffset timestamp, double value)
    {
        PushedPoints.Add((canvasId, datasetIndex, timestamp, value));
        return ValueTask.CompletedTask;
    }

    public ValueTask SetSeriesAsync(string canvasId, int datasetIndex, IEnumerable<(DateTimeOffset Timestamp, double Value)> points)
    {
        SeriesSet.Add((canvasId, datasetIndex, [.. points]));
        return ValueTask.CompletedTask;
    }

    public ValueTask DestroyAsync(string canvasId)
    {
        DestroyedCanvasIds.Add(canvasId);
        return ValueTask.CompletedTask;
    }
}
