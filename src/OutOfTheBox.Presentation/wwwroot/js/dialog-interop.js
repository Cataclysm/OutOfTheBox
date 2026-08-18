// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

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
// distinguishing "clicked the backdrop" from "clicked inside" needs BOTH: event.target === element
// (a click on any descendant - including one that bubbles up - always has target set to that
// descendant, never the dialog itself) AND the coordinates falling outside the dialog's own content
// box (target === element is also true for a click that lands in the dialog's own padding, which is
// still visually "inside", not the backdrop). Coordinates alone aren't enough: a keyboard-activated
// click (Enter/Space on a focused control) or a script-dispatched .click() carries clientX/clientY
// of 0, which reads as "outside" no matter where the actual control sits - requiring target ===
// element first rules those out, since their target is the focused/dispatched-on element, not the
// dialog. Firing the real "cancel" event first (rather than calling .close() directly) is what makes
// this genuinely equivalent to Escape - same event, same default-prevention hook - instead of a
// close that merely looks the same but skips whatever a future `@oncancel`-based handler expects.
function attachBackdropDismiss(element) {
    if (element.dataset.backdropDismissAttached) {
        return;
    }

    element.dataset.backdropDismissAttached = "true";
    element.addEventListener("click", function (event) {
        if (event.target !== element) {
            return;
        }

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
