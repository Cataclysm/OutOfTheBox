// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

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
    /// Reads the operator's last-saved word-wrap choice from a client-side (browser cookie) store,
    /// defaulting to enabled when no choice has been saved yet - call before <see cref="RenderAsync"/>
    /// so a dialog's own word-wrap checkbox and the editor it renders start in agreement.
    /// </summary>
    ValueTask<bool> GetWordWrapPreferenceAsync();

    /// <summary>
    /// Replaces the read-only <c>&lt;textarea id="elementId"&gt;</c> already rendered with its
    /// content with a CodeMirror editor reading that same content. <paramref name="mimeType"/> is a
    /// CodeMirror MIME/mode identifier (see <see cref="CodePreviewLanguage"/>), or
    /// <see langword="null"/> for unrecognized content - still rendered with line numbers and generic
    /// folding, just without color highlighting. <paramref name="wordWrap"/> sets the editor's
    /// initial line-wrapping state, typically the value <see cref="GetWordWrapPreferenceAsync"/> just returned.
    /// </summary>
    ValueTask RenderAsync(string elementId, string? mimeType, bool wordWrap);

    /// <summary>
    /// Toggles line wrapping on the already-mounted editor for <paramref name="elementId"/> and saves
    /// the choice as the default for every preview opened after this one.
    /// </summary>
    ValueTask SetWordWrapAsync(string elementId, bool wordWrap);

    /// <summary>Destroys the editor for <paramref name="elementId"/>, if one exists, reverting the element back to a plain textarea.</summary>
    ValueTask DestroyAsync(string elementId);
}
