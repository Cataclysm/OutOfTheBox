// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Presentation.Mcp;

namespace OutOfTheBox.UnitTests.Presentation.Mcp;

/// <summary>
/// Covers <see cref="McpRunOutputBuffer"/>'s offset-based polling contract directly - the piece
/// design.md calls out as touching shared execution-engine plumbing, so it gets its own focused
/// coverage per tasks.md 2.3, independent of any tool handler that will eventually call it.
/// </summary>
public sealed class McpRunOutputBufferTests
{
    [Fact]
    public void ReadSince_from_offset_zero_returns_everything_appended_so_far()
    {
        var buffer = new McpRunOutputBuffer(capBytes: 1024);
        buffer.Append("stdout", "first line");
        buffer.Append("stderr", "a warning");

        var page = buffer.ReadSince(0);

        Assert.Equal("first line" + Environment.NewLine, page.Stdout);
        Assert.Equal("a warning" + Environment.NewLine, page.Stderr);
        Assert.False(page.Truncated);
    }

    [Fact]
    public void ReadSince_from_a_non_zero_offset_returns_only_output_appended_after_it()
    {
        var buffer = new McpRunOutputBuffer(capBytes: 1024);
        buffer.Append("stdout", "first line");
        var firstPage = buffer.ReadSince(0);

        buffer.Append("stdout", "second line");
        var secondPage = buffer.ReadSince(firstPage.NextOffset);

        Assert.Equal("second line" + Environment.NewLine, secondPage.Stdout);
        Assert.DoesNotContain("first line", secondPage.Stdout);
    }

    [Fact]
    public void ReadSince_the_buffers_own_current_offset_returns_nothing_new_without_erroring()
    {
        var buffer = new McpRunOutputBuffer(capBytes: 1024);
        buffer.Append("stdout", "only line");
        var page = buffer.ReadSince(0);

        var repeatedPage = buffer.ReadSince(page.NextOffset);

        Assert.Equal(string.Empty, repeatedPage.Stdout);
        Assert.Equal(string.Empty, repeatedPage.Stderr);
        Assert.Equal(page.NextOffset, repeatedPage.NextOffset);
    }

    [Fact]
    public void ReadSince_is_repeatable_after_the_run_has_finished_producing_output()
    {
        // Simulates polling a run that has already reached a terminal state - per
        // mcp-command-execution's "Polling after completion" scenario, repeated reads must keep
        // returning the same (now-stable) content, never error just because nothing changed.
        var buffer = new McpRunOutputBuffer(capBytes: 1024);
        buffer.Append("stdout", "done");
        var page = buffer.ReadSince(0);

        var firstRepeat = buffer.ReadSince(page.NextOffset);
        var secondRepeat = buffer.ReadSince(page.NextOffset);

        Assert.Equal(string.Empty, firstRepeat.Stdout);
        Assert.Equal(string.Empty, secondRepeat.Stdout);
        Assert.Equal(firstRepeat.NextOffset, secondRepeat.NextOffset);
    }

    [Fact]
    public void Append_past_the_cap_stops_accepting_further_output_and_marks_truncated()
    {
        var buffer = new McpRunOutputBuffer(capBytes: 10);

        var firstAccepted = buffer.Append("stdout", "0123456789");
        var secondAccepted = buffer.Append("stdout", "this pushes past the cap");

        Assert.True(firstAccepted);
        Assert.False(secondAccepted);
        Assert.True(buffer.Truncated);
    }

    [Fact]
    public void ReadSince_reports_truncated_once_the_cap_has_been_reached()
    {
        var buffer = new McpRunOutputBuffer(capBytes: 5);
        buffer.Append("stdout", "this line alone exceeds the cap");

        var page = buffer.ReadSince(0);

        Assert.True(page.Truncated);
        Assert.Equal(string.Empty, page.Stdout);
    }
}
