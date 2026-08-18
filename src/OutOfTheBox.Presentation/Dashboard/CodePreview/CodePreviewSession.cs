// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Presentation.Dashboard.CodePreview;

/// <summary>
/// The CodeMirror lifecycle every read-only code/diff preview textarea needs - destroy any previous
/// instance before opening, read the saved word-wrap preference, render once new content arrives,
/// toggle word-wrap on demand, destroy on dispose. Extracted once <see cref="FilePreviewDialog"/>'s
/// code branch and <see cref="CommitFileDiffDialog"/> had copy-pasted this exact sequence against
/// <see cref="ICodePreviewInterop"/> verbatim - each dialog still owns its own markup (title, where
/// the word-wrap checkbox sits, the textarea itself, any extra messaging around it); this only owns
/// the CodeMirror-side state and interop calls.
/// </summary>
public sealed class CodePreviewSession(ICodePreviewInterop interop) : IAsyncDisposable
{
    private bool _pendingRender;

    /// <summary>The id of the <c>&lt;textarea&gt;</c> this session's CodeMirror instance is/will be attached to - unique per session instance.</summary>
    public string ElementId { get; } = $"code-preview-{Guid.NewGuid():N}";

    /// <summary>The current word-wrap state - starts at the operator's saved preference (see <see cref="PrepareForOpenAsync"/>), then follows <see cref="ToggleWordWrapAsync"/>.</summary>
    public bool WordWrap { get; private set; } = true;

    /// <summary>
    /// Tears down any CodeMirror instance left over from a previously previewed file, and refreshes
    /// <see cref="WordWrap"/> from the saved preference - call once per dialog open, before setting
    /// any new content, so CodeMirror never has to fight a re-render it didn't create.
    /// </summary>
    public async Task PrepareForOpenAsync()
    {
        await interop.DestroyAsync(ElementId);
        WordWrap = await interop.GetWordWrapPreferenceAsync();
        _pendingRender = false;
    }

    /// <summary>Marks that new text has just been assigned to the textarea - call right after setting it, before the next <c>StateHasChanged</c>.</summary>
    public void MarkContentReady() => _pendingRender = true;

    /// <summary>Call from <c>OnAfterRenderAsync</c> - attaches CodeMirror to the textarea if <see cref="MarkContentReady"/> was called since the last render; otherwise does nothing.</summary>
    public async Task RenderIfPendingAsync(string? mimeType)
    {
        if (!_pendingRender)
        {
            return;
        }

        _pendingRender = false;
        await interop.RenderAsync(ElementId, mimeType, WordWrap);
    }

    /// <summary>Applies a new word-wrap choice to the mounted editor and persists it as the default for the next preview opened.</summary>
    public async Task ToggleWordWrapAsync(bool wordWrap)
    {
        WordWrap = wordWrap;
        await interop.SetWordWrapAsync(ElementId, wordWrap);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await interop.DestroyAsync(ElementId);
}
