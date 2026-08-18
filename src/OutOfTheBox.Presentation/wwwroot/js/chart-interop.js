// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.
//
// Thin interop wrapper around the vendored Chart.js (js/vendor/chart.umd.min.js) - per
// design.md's "Charting" decision, one chart instance per <canvas>, created once and updated
// incrementally (push + chart.update('none')) rather than recreated every tick.

window.outOfTheBoxCharts = (() => {
    const charts = new Map();
    const windowMs = new Map();

    // No entry (or an explicit null/undefined) in windowMs for a canvas means "never trim" - a run's
    // own full-duration history graph is meant to keep growing for as long as the run stays open,
    // not slide a fixed window. A live chart that *does* want a rolling window (every Status page
    // graph) passes one explicitly via createLineChart's own liveWindowMs argument instead of relying
    // on any implicit default here.

    // Chart.js's own defaults assume a light page - left alone, tick/legend text renders in a dark
    // gray that's nearly invisible against the dashboard's dark background. Set once, globally,
    // rather than per-chart, since every chart on this dashboard shares the same dark theme.
    Chart.defaults.color = "#9aa2af";
    Chart.defaults.borderColor = "rgba(255, 255, 255, 0.08)";
    Chart.defaults.font.family = "-apple-system, Segoe UI, Roboto, Helvetica, Arial, sans-serif";

    // CPU gets the brand's warm accent, RAM the cool one - the original two single-line charts this
    // dashboard ever drew, kept exactly as before for them. A chart with more than one dataset (per
    // CPU core, network sent/received) instead cycles through CATEGORICAL_PALETTE by dataset index,
    // since a label-based lookup can't tell N core lines or a Sent/Received pair apart.
    const CATEGORICAL_PALETTE = [
        "#ec4899", "#8b5cf6", "#06b6d4", "#f59e0b",
        "#22c55e", "#ef4444", "#3b82f6", "#eab308",
    ];

    function hexToRgba(hex, alpha) {
        const r = parseInt(hex.slice(1, 3), 16);
        const g = parseInt(hex.slice(3, 5), 16);
        const b = parseInt(hex.slice(5, 7), 16);
        return `rgba(${r}, ${g}, ${b}, ${alpha})`;
    }

    function colorFor(label, index, datasetCount) {
        if (datasetCount > 1) {
            const hex = CATEGORICAL_PALETTE[index % CATEGORICAL_PALETTE.length];
            return { border: hex, background: hexToRgba(hex, 0.12) };
        }

        return label.toLowerCase().startsWith("cpu")
            ? { border: "#ec4899", background: "rgba(236, 72, 153, 0.12)" }
            : { border: "#8b5cf6", background: "rgba(139, 92, 246, 0.12)" };
    }

    // Mirrors Status.razor's/Repos.razor's own FormatBytes exactly (same thresholds, same one
    // decimal place) - kept as a separate JS copy rather than plumbed from C# because this runs
    // inside a Chart.js tick callback, which only ever executes client-side.
    function formatBytes(bytes) {
        if (bytes >= 1024 * 1024 * 1024) {
            return (bytes / (1024 * 1024 * 1024)).toFixed(1) + " GB";
        }
        if (bytes >= 1024 * 1024) {
            return (bytes / (1024 * 1024)).toFixed(1) + " MB";
        }
        if (bytes >= 1024) {
            return (bytes / 1024).toFixed(1) + " KB";
        }
        return bytes + " B";
    }

    function formatBytesPerSecond(bytesPerSecond) {
        return formatBytes(bytesPerSecond) + "/s";
    }

    function createLineChart(canvasId, datasetLabels, yAxisFormat, showLegend, liveWindowMs) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) {
            return;
        }

        if (liveWindowMs != null) {
            windowMs.set(canvasId, liveWindowMs);
        }

        const yTicks = yAxisFormat === "bytes"
            ? { callback: formatBytes }
            : yAxisFormat === "bytesPerSecond"
                ? { callback: formatBytesPerSecond }
                : {};

        const chart = new Chart(canvas, {
            type: "line",
            data: {
                datasets: datasetLabels.map((label, index) => {
                    const color = colorFor(label, index, datasetLabels.length);
                    return {
                        label,
                        data: [],
                        borderWidth: 1.5,
                        borderColor: color.border,
                        backgroundColor: color.background,
                        fill: true,
                        pointRadius: 0,
                        tension: 0.2,
                    };
                }),
            },
            options: {
                animation: false,
                parsing: false,
                normalized: true,
                responsive: true,
                // Chart.js defaults to true here, which forces width = aspectRatio (default 2) *
                // height - since .resource-graph canvas caps height via CSS (max-height: 160px) but
                // leaves width to the flex layout (which can be far wider than 320px, e.g. the
                // Status page's own full-width CPU row), the chart ends up rendered at a fraction of
                // its actual container width instead of filling it. false decouples the two, so
                // width and height are each driven purely by the canvas's own CSS-computed box.
                maintainAspectRatio: false,
                plugins: {
                    legend: { display: showLegend !== false },
                },
                scales: {
                    x: { type: "time", ticks: { maxRotation: 0, autoSkip: true, maxTicksLimit: 5 } },
                    y: { beginAtZero: true, ticks: yTicks },
                },
            },
        });

        charts.set(canvasId, chart);
    }

    function pushPoint(canvasId, datasetIndex, timestampMs, value) {
        const chart = charts.get(canvasId);
        if (!chart) {
            return;
        }

        const data = chart.data.datasets[datasetIndex].data;
        data.push({ x: timestampMs, y: value });

        const window = windowMs.get(canvasId);
        if (window != null) {
            const cutoff = timestampMs - window;
            while (data.length > 0 && data[0].x < cutoff) {
                data.shift();
            }
        }

        chart.update("none");
    }

    function setSeries(canvasId, datasetIndex, points) {
        const chart = charts.get(canvasId);
        if (!chart) {
            return;
        }

        chart.data.datasets[datasetIndex].data = points.map(p => ({ x: p.timestampMs, y: p.value }));
        chart.update("none");
    }

    function destroyChart(canvasId) {
        const chart = charts.get(canvasId);
        if (!chart) {
            return;
        }

        chart.destroy();
        charts.delete(canvasId);
        windowMs.delete(canvasId);
    }

    return { createLineChart, pushPoint, setSeries, destroyChart };
})();
