// A minimal Chart.js v4 date adapter using plain JS `Date` arithmetic - no external date library
// (date-fns/luxon/moment) is vendored anywhere else in this project, and this dashboard's only use
// of the "time" scale is a live-updating epoch-millisecond series (see chart-interop.js), which
// doesn't need anything more sophisticated than this covers. Missing entirely until now: Chart.js's
// own "time" scale type requires *some* adapter to be registered, or it throws "This method is not
// implemented" the moment it tries to compute tick labels - a real, previously-unnoticed bug only
// surfaced by actually opening a browser's dev tools console against a real deployment, not by
// checking that the page/data rendered.
(function () {
    function pad(n) {
        return n < 10 ? "0" + n : "" + n;
    }

    function formatDate(ms, unit) {
        var d = new Date(ms);
        switch (unit) {
            case "millisecond":
                return pad(d.getHours()) + ":" + pad(d.getMinutes()) + ":" + pad(d.getSeconds()) + "." + d.getMilliseconds();
            case "second":
                return pad(d.getHours()) + ":" + pad(d.getMinutes()) + ":" + pad(d.getSeconds());
            case "minute":
            case "hour":
                return pad(d.getHours()) + ":" + pad(d.getMinutes());
            case "day":
            case "week":
                return d.toLocaleDateString(undefined, { month: "short", day: "numeric" });
            case "month":
                return d.toLocaleDateString(undefined, { month: "short", year: "numeric" });
            case "year":
                return "" + d.getFullYear();
            default:
                return d.toLocaleString();
        }
    }

    var MS_PER_UNIT = {
        millisecond: 1,
        second: 1000,
        minute: 60 * 1000,
        hour: 60 * 60 * 1000,
        day: 24 * 60 * 60 * 1000,
        week: 7 * 24 * 60 * 60 * 1000,
        month: 30.44 * 24 * 60 * 60 * 1000,
        quarter: 91.31 * 24 * 60 * 60 * 1000,
        year: 365.25 * 24 * 60 * 60 * 1000,
    };

    Chart._adapters._date.override({
        _id: "native",

        formats: function () {
            return {
                datetime: "datetime",
                millisecond: "millisecond",
                second: "second",
                minute: "minute",
                hour: "hour",
                day: "day",
                week: "week",
                month: "month",
                quarter: "month",
                year: "year",
            };
        },

        parse: function (value) {
            if (value === null || value === undefined) {
                return null;
            }
            if (value instanceof Date) {
                return value.getTime();
            }
            if (typeof value === "number") {
                return value;
            }
            var parsed = Date.parse(value);
            return isNaN(parsed) ? null : parsed;
        },

        format: function (time, format) {
            return formatDate(time, format);
        },

        add: function (time, amount, unit) {
            var d = new Date(time);
            switch (unit) {
                case "millisecond": d.setMilliseconds(d.getMilliseconds() + amount); break;
                case "second": d.setSeconds(d.getSeconds() + amount); break;
                case "minute": d.setMinutes(d.getMinutes() + amount); break;
                case "hour": d.setHours(d.getHours() + amount); break;
                case "day": d.setDate(d.getDate() + amount); break;
                case "week": d.setDate(d.getDate() + amount * 7); break;
                case "month": d.setMonth(d.getMonth() + amount); break;
                case "quarter": d.setMonth(d.getMonth() + amount * 3); break;
                case "year": d.setFullYear(d.getFullYear() + amount); break;
                default: break;
            }
            return d.getTime();
        },

        diff: function (max, min, unit) {
            var perUnit = MS_PER_UNIT[unit] || 1;
            return (max - min) / perUnit;
        },

        startOf: function (time, unit, weekday) {
            var d = new Date(time);
            switch (unit) {
                case "second": d.setMilliseconds(0); break;
                case "minute": d.setSeconds(0, 0); break;
                case "hour": d.setMinutes(0, 0, 0); break;
                case "day": d.setHours(0, 0, 0, 0); break;
                case "week": {
                    d.setHours(0, 0, 0, 0);
                    var target = weekday === undefined ? 0 : weekday;
                    var diff = (d.getDay() - target + 7) % 7;
                    d.setDate(d.getDate() - diff);
                    break;
                }
                case "month": d.setDate(1); d.setHours(0, 0, 0, 0); break;
                case "quarter": {
                    var quarterStartMonth = Math.floor(d.getMonth() / 3) * 3;
                    d.setMonth(quarterStartMonth, 1);
                    d.setHours(0, 0, 0, 0);
                    break;
                }
                case "year": d.setMonth(0, 1); d.setHours(0, 0, 0, 0); break;
                default: break;
            }
            return d.getTime();
        },

        endOf: function (time, unit) {
            var start = this.startOf(time, unit);
            var next = this.add(start, 1, unit);
            return next - 1;
        },
    });
})();
