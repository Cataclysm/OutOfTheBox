// Thin wrapper around the native <dialog> element's imperative show/close API - Blazor has no
// built-in binding for it, and a plain @onclick can't call a DOM method directly.
window.outOfTheBoxDialogs = {
    showModal: function (element) {
        if (element && typeof element.showModal === "function" && !element.open) {
            element.showModal();
        }
    },
    close: function (element) {
        if (element && typeof element.close === "function" && element.open) {
            element.close();
        }
    },
};
