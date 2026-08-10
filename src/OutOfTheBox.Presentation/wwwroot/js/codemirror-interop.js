// Thin interop wrapper around the vendored CodeMirror 5 (js/vendor/codemirror.min.js) - progressively
// enhances a server-rendered, read-only <textarea> (FilePreviewDialog.razor) into a syntax-highlighted,
// line-numbered, foldable code view via CodeMirror.fromTextArea, the same reasoning dialog-interop.js's
// showModal/close wraps the native <dialog> element's own imperative API.

window.outOfTheBoxCodePreview = (() => {
    const editors = new Map();

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

    return {
        // mimeType is null for an unrecognized file - CodeMirror still renders it with line numbers
        // and generic (indentation-based) folding, just without color highlighting.
        render(elementId, mimeType) {
            destroyExisting(elementId);

            const textarea = document.getElementById(elementId);
            if (!textarea) {
                return;
            }

            const editor = CodeMirror.fromTextArea(textarea, {
                mode: mimeType || null,
                theme: "darcula",
                readOnly: true,
                lineNumbers: true,
                lineWrapping: true,
                foldGutter: true,
                foldOptions,
                gutters: ["CodeMirror-linenumbers", "CodeMirror-foldgutter"],
            });
            editor.getWrapperElement().classList.add("file-preview-codemirror");
            editors.set(elementId, editor);
        },

        destroy(elementId) {
            destroyExisting(elementId);
        },
    };
})();
