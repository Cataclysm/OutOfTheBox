// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

// Thin interop wrapper around the vendored CodeMirror 5 (js/vendor/codemirror.min.js) - progressively
// enhances a server-rendered, read-only <textarea> (FilePreviewDialog.razor) into a syntax-highlighted,
// line-numbered, foldable code view via CodeMirror.fromTextArea, the same reasoning dialog-interop.js's
// showModal/close wraps the native <dialog> element's own imperative API.

window.outOfTheBoxCodePreview = (() => {
    const editors = new Map();

    // A plain client-side cookie (not a server round-trip) - the operator's word-wrap choice is
    // purely a rendering preference this browser cares about, not application state the server has
    // any use for. Long-lived (a year) since there's no natural "expiry" for an editor preference.
    const wordWrapCookieName = "ootb-word-wrap";

    // Combines every vendored fold strategy (braces, XML/HTML tags, comment blocks, indentation) so
    // the fold gutter's arrows work regardless of which language mode - or none - is active, rather
    // than needing a strategy chosen per mode.
    const foldOptions = {
        rangeFinder: CodeMirror.fold.combine(
            CodeMirror.fold.brace,
            CodeMirror.fold.xml,
            CodeMirror.fold.comment,
            CodeMirror.fold.indent),
    };

    function destroyExisting(elementId) {
        const existing = editors.get(elementId);
        if (existing) {
            existing.toTextArea();
            editors.delete(elementId);
        }
    }

    // No cookie yet (first-ever preview) defaults to wrapping ON, per the operator's own stated
    // default - only an explicit prior "off" choice turns it off for future previews.
    function getWordWrapPreference() {
        const match = document.cookie.match(new RegExp("(?:^|; )" + wordWrapCookieName + "=([^;]*)"));
        return match ? match[1] === "1" : true;
    }

    function setWordWrapCookie(wordWrap) {
        document.cookie = `${wordWrapCookieName}=${wordWrap ? "1" : "0"}; path=/; max-age=31536000; SameSite=Strict`;
    }

    return {
        getWordWrapPreference,

        // mimeType is null for an unrecognized file - CodeMirror still renders it with line numbers
        // and generic (indentation-based) folding, just without color highlighting. wordWrap is
        // passed in by the caller (already resolved from getWordWrapPreference, typically) rather
        // than read again here, so a dialog's checkbox and the editor it renders can never disagree
        // about the starting state.
        render(elementId, mimeType, wordWrap) {
            destroyExisting(elementId);

            const textarea = document.getElementById(elementId);
            if (!textarea) {
                return;
            }

            const editor = CodeMirror.fromTextArea(textarea, {
                mode: mimeType || null,
                theme: "visual-studio-dark",
                readOnly: true,
                lineNumbers: true,
                lineWrapping: wordWrap,
                foldGutter: true,
                foldOptions,
                gutters: ["CodeMirror-linenumbers", "CodeMirror-foldgutter"],
            });
            editor.getWrapperElement().classList.add("file-preview-codemirror");
            editors.set(elementId, editor);
        },

        // Toggled live from an already-open preview's own checkbox - updates the mounted editor
        // in place (no re-render) and saves the choice as the default for every preview opened
        // after this one, per the operator's "remember the last state" request.
        setWordWrap(elementId, wordWrap) {
            setWordWrapCookie(wordWrap);

            const editor = editors.get(elementId);
            if (editor) {
                editor.setOption("lineWrapping", wordWrap);
            }
        },

        destroy(elementId) {
            destroyExisting(elementId);
        },
    };
})();
