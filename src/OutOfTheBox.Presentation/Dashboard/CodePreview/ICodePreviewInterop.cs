// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Presentation.Dashboard.CodePreview;

/// <summary>
/// Abstraction over the CodeMirror JS interop calls <see cref="FilePreviewDialog"/> uses to render a
/// syntax-highlighted, foldable code preview - lets tests substitute a spy instead of driving a real
/// JS engine, the same precedent this project already applies to <c>IChartInterop</c>, since there's
/// no Blazor-interactive browser test client in this project's toolchain.
/// </summary>
public interface ICodePreviewInterop
{
    /// <summary>
    /// Replaces the read-only <c>&lt;textarea id="elementId"&gt;</c> already rendered with its
    /// content with a CodeMirror editor reading that same content. <paramref name="mimeType"/> is a
    /// CodeMirror MIME/mode identifier (see <see cref="CodePreviewLanguage"/>), or
    /// <see langword="null"/> for unrecognized content - still rendered with line numbers and generic
    /// folding, just without color highlighting.
    /// </summary>
    ValueTask RenderAsync(string elementId, string? mimeType);

    /// <summary>Destroys the editor for <paramref name="elementId"/>, if one exists, reverting the element back to a plain textarea.</summary>
    ValueTask DestroyAsync(string elementId);
}
