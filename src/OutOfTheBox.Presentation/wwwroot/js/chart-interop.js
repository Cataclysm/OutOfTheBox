// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.
//
// Thin interop wrapper around the vendored Chart.js (js/vendor/chart.umd.min.js) - per
// design.md's "Charting" decision, one chart instance per <canvas>, created once and updated
// incrementally (push + chart.update('none')) rather than recreated every tick.

window.outOfTheBoxCharts = (() => {
    const charts = new Map();

    // Chart.js's own defaults assume a light page - left alone, tick/legend text renders in a dark
    // gray that's nearly invisible against the dashboard's dark background. Set once, globally,
    // rather than per-chart, since every chart on this dashboard shares the same dark theme.
    Chart.defaults.color = "#9aa2af";
    Chart.defaults.borderColor = "rgba(255, 255, 255, 0.08)";
    Chart.defaults.font.family = "-apple-system, Segoe UI, Roboto, Helvetica, Arial, sans-serif";

    // CPU gets the brand's warm accent, RAM the cool one - the only two series this dashboard ever
    // charts, so a label-based lookup is simpler than plumbing a color choice through every caller.
    function colorForLabel(label) {
        return label.toLowerCase().startsWith("cpu")
            ? { border: "#ec4899", background: "rgba(236, 72, 153, 0.12)" }
            : { border: "#8b5cf6", background: "rgba(139, 92, 246, 0.12)" };
    }

    function createLineChart(canvasId, datasetLabels) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) {
            return;
        }

        const chart = new Chart(canvas, {
            type: "line",
            data: {
                datasets: datasetLabels.map(label => {
                    const color = colorForLabel(label);
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
                scales: {
                    x: { type: "time", ticks: { maxRotation: 0 } },
                    y: { beginAtZero: true },
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

        chart.data.datasets[datasetIndex].data.push({ x: timestampMs, y: value });
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
    }

    return { createLineChart, pushPoint, setSeries, destroyChart };
})();
