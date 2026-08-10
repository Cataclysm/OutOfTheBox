// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

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
        // runs - the same reasoning ChartInterop.DestroyAsync already documents for its own case.
        try
        {
            await jsRuntime.InvokeVoidAsync("outOfTheBoxCodePreview.destroy", elementId);
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
