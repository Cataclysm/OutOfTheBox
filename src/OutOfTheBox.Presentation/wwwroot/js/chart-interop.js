// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.
//
// Thin interop wrapper around the vendored Chart.js (js/vendor/chart.umd.min.js) - per
// design.md's "Charting" decision, one chart instance per <canvas>, created once and updated
// incrementally (push + chart.update('none')) rather than recreated every tick.

window.outOfTheBoxCharts = (() => {
    const charts = new Map();

    function createLineChart(canvasId, datasetLabels) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) {
            return;
        }

        const chart = new Chart(canvas, {
            type: "line",
            data: {
                datasets: datasetLabels.map(label => ({
                    label,
                    data: [],
                    borderWidth: 1.5,
                    pointRadius: 0,
                    tension: 0.2,
                })),
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
