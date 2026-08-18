// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using Microsoft.JSInterop;

namespace OutOfTheBox.Presentation.Dashboard.CodePreview;

/// <inheritdoc />
public sealed class CodePreviewInterop(IJSRuntime jsRuntime) : ICodePreviewInterop
{
    /// <inheritdoc />
    public ValueTask<bool> GetWordWrapPreferenceAsync() =>
        jsRuntime.InvokeAsync<bool>("outOfTheBoxCodePreview.getWordWrapPreference");

    /// <inheritdoc />
    public ValueTask RenderAsync(string elementId, string? mimeType, bool wordWrap) =>
        jsRuntime.InvokeVoidAsync("outOfTheBoxCodePreview.render", elementId, mimeType, wordWrap);

    /// <inheritdoc />
    public ValueTask SetWordWrapAsync(string elementId, bool wordWrap) =>
        jsRuntime.InvokeVoidAsync("outOfTheBoxCodePreview.setWordWrap", elementId, wordWrap);

    /// <inheritdoc />
    public async ValueTask DestroyAsync(string elementId)
    {
        // Best-effort: normally called from FilePreviewDialog's DisposeAsync during page
        // navigation/circuit teardown, where the circuit may already be gone by the time cleanup
        // runs - the same reasoning ChartInterop.DestroyAsync already documents for its own case,
        // including the InvalidOperationException ("statically rendering") failure mode confirmed
        // live alongside JSDisconnectedException - see that method's own remarks.
        try
        {
            await jsRuntime.InvokeVoidAsync("outOfTheBoxCodePreview.destroy", elementId);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or InvalidOperationException)
        {
        }
    }
}
