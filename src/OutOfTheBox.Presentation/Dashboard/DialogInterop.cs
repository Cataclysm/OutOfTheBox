// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace OutOfTheBox.Presentation.Dashboard;

/// <summary>
/// Thin typed wrapper around <c>wwwroot/js/dialog-interop.js</c>'s <c>outOfTheBoxDialogs</c> object -
/// every dialog component (<c>ConfirmDialog</c>, <c>CloneDialog</c>, <c>RenameDialog</c>,
/// <c>AddCredentialDialog</c>, <c>EditCredentialDialog</c>, <c>PatPromptDialog</c>,
/// <c>FilePreviewDialog</c>, <c>CommitFileDiffDialog</c>) called
/// <c>JS.InvokeVoidAsync("outOfTheBoxDialogs.showModal"/"outOfTheBoxDialogs.close", ...)</c> directly,
/// repeating the same two magic strings at every call site - extracted once so a typo in either
/// string is a compile error instead of a silently-inert button.
/// </summary>
public static class DialogInterop
{
    /// <summary>Opens <paramref name="dialog"/> as a modal (native <c>&lt;dialog&gt;.showModal()</c>) - a no-op if it's already open.</summary>
    public static ValueTask ShowModalAsync(this IJSRuntime js, ElementReference dialog) =>
        js.InvokeVoidAsync("outOfTheBoxDialogs.showModal", dialog);

    /// <summary>Closes <paramref name="dialog"/> (native <c>&lt;dialog&gt;.close()</c>) - a no-op if it's already closed.</summary>
    public static ValueTask CloseAsync(this IJSRuntime js, ElementReference dialog) =>
        js.InvokeVoidAsync("outOfTheBoxDialogs.close", dialog);
}
