// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Presentation.Dashboard;

/// <summary>
/// Constants shared by every list/table component that plays a CSS exit animation before actually
/// removing a row (<c>Repositories</c>, <c>Credentials</c>, <c>FileTreeNode</c>, <c>Status</c>'s own
/// run list) - see <c>dashboard.css</c>'s own "Generic list/row entrance and exit animation" remarks
/// for why the entrance half needs no code at all, while the exit half does: Blazor removes a
/// dropped item's DOM node synchronously in the same render that drops it from its backing
/// collection, before any CSS animation would have a chance to play, so a caller instead adds
/// <see cref="ExitingClass"/> to the row for one extra render, awaits <see cref="ExitDuration"/>,
/// and only then performs the actual removal.
/// </summary>
public static class ListAnimation
{
    /// <summary>CSS class that plays the row-exit animation (gated behind <c>prefers-reduced-motion</c> in <c>dashboard.css</c>) and disables pointer events on the row while it plays.</summary>
    public const string ExitingClass = "list-item-exiting";

    /// <summary>
    /// How long a caller pauses after adding <see cref="ExitingClass"/> before actually removing the
    /// row - matches the CSS animation's own duration. Deliberately not skipped for an operator who
    /// has requested reduced motion: only the visible animation is conditional (via the CSS media
    /// query), not this fixed pause, since an unconditional delay-then-remove needs no JS interop to
    /// detect the operator's OS-level motion preference from C# at all.
    /// </summary>
    public static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(200);
}
