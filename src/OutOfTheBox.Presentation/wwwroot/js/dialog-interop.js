// Thin wrapper around the native <dialog> element's imperative show/close API - Blazor has no
// built-in binding for it, and a plain @onclick can't call a DOM method directly.
window.outOfTheBoxDialogs = {
    showModal: function (element) {
        if (element && typeof element.showModal === "function" && !element.open) {
            attachBackdropDismiss(element);
            element.showModal();
        }
    },
    close: function (element) {
        if (element && typeof element.close === "function" && element.open) {
            element.close();
        }
    },
};

// Clicking outside a modal <dialog>'s own content (its backdrop) dismisses it the same way pressing
// Escape already does - the browser has no built-in gesture for this, only Escape. A backdrop click
// lands on the <dialog> element itself (its own content is a child, e.g. the .dialog-body div), so
// distinguishing "clicked the backdrop" from "clicked inside" comes down to comparing the click's
// coordinates against the dialog's own content box, not the element the event happened to bubble
// through. Firing the real "cancel" event first (rather than calling .close() directly) is what
// makes this genuinely equivalent to Escape - same event, same default-prevention hook - instead of
// a close that merely looks the same but skips whatever a future `@oncancel`-based handler expects.
function attachBackdropDismiss(element) {
    if (element.dataset.backdropDismissAttached) {
        return;
    }

    element.dataset.backdropDismissAttached = "true";
    element.addEventListener("click", function (event) {
        const rect = element.getBoundingClientRect();
        const clickedInsideContent = event.clientX >= rect.left && event.clientX <= rect.right
            && event.clientY >= rect.top && event.clientY <= rect.bottom;

        if (clickedInsideContent) {
            return;
        }

        if (element.dispatchEvent(new Event("cancel", { cancelable: true }))) {
            element.close();
        }
    });
}
