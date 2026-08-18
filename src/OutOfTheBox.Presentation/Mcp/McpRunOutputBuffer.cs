// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using System.Text;

namespace OutOfTheBox.Presentation.Mcp;

/// <summary>
/// Retains one MCP-started run's stdout/stderr, addressable by an opaque, monotonically increasing
/// byte offset, so <c>read_run_output</c> (per <c>mcp-command-execution</c>'s spec) can be polled
/// repeatedly - including after the run reaches a terminal state - and each call returns only what's
/// new since the offset it was given. Enforces the same output size cap
/// (<see cref="OutOfTheBox.Application.Configuration.ServiceOptions.OutputCapBytes"/>) every
/// run-output path in this service enforces, via the same "stop accepting further output, flag
/// truncated, let the process keep running" policy.
/// </summary>
/// <remarks>
/// Retained for the process's lifetime once created - there is no eviction in this version. Bounded
/// per run by <paramref name="capBytes"/> the same way streamed output already is; unbounded only in
/// run *count* over a long-running service handling many MCP-started runs. Accepted as a reasonable
/// v1 gap (the same class of documented, accepted risk <see cref="OutOfTheBox.Application.Concurrency.RunRegistry"/>'s
/// own remarks already carry for a different concern) rather than building eviction machinery
/// tasks.md never called for; revisit if this proves to matter in practice.
/// </remarks>
public sealed class McpRunOutputBuffer(long capBytes)
{
    private readonly Lock _lock = new();
    private readonly List<Chunk> _chunks = [];
    private readonly StringBuilder _stdout = new();
    private readonly StringBuilder _stderr = new();
    private long _bytesWritten;

    /// <summary>Whether output was dropped after the cap was reached.</summary>
    public bool Truncated { get; private set; }

    /// <summary>The full standard output accumulated so far, possibly truncated.</summary>
    public string Stdout
    {
        get { lock (_lock) { return _stdout.ToString(); } }
    }

    /// <summary>The full standard error accumulated so far, possibly truncated.</summary>
    public string Stderr
    {
        get { lock (_lock) { return _stderr.ToString(); } }
    }

    /// <summary>
    /// Appends one line from <paramref name="stream"/> ("stdout" or "stderr"). Returns
    /// <see langword="false"/>, without appending, once the cap has already been reached or this
    /// line would exceed it - matching this service's own "drop, don't wrap" output-cap policy.
    /// </summary>
    public bool Append(string stream, string line)
    {
        lock (_lock)
        {
            if (Truncated)
            {
                return false;
            }

            var lineBytes = Encoding.UTF8.GetByteCount(line);
            if (_bytesWritten + lineBytes > capBytes)
            {
                Truncated = true;
                return false;
            }

            _bytesWritten += lineBytes;
            _chunks.Add(new Chunk(stream, line, _bytesWritten));

            (stream == "stdout" ? _stdout : _stderr).AppendLine(line);
            return true;
        }
    }

    /// <summary>
    /// Returns whatever stdout/stderr content was appended after <paramref name="offset"/> (0 for
    /// everything from the start), plus the new offset to pass on the next call. Safe to call
    /// repeatedly, including with the buffer's own current offset (returns empty strings, unchanged
    /// offset) - a run that has already reached a terminal state simply stops producing anything new.
    /// </summary>
    public McpRunOutputPage ReadSince(long offset)
    {
        lock (_lock)
        {
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var newOffset = offset;

            foreach (var chunk in _chunks)
            {
                if (chunk.OffsetAfter <= offset)
                {
                    continue;
                }

                (chunk.Stream == "stdout" ? stdout : stderr).AppendLine(chunk.Line);
                newOffset = chunk.OffsetAfter;
            }

            return new McpRunOutputPage(stdout.ToString(), stderr.ToString(), newOffset, Truncated);
        }
    }

    private sealed record Chunk(string Stream, string Line, long OffsetAfter);
}

/// <summary>One <see cref="McpRunOutputBuffer.ReadSince"/> result: the new output since the requested offset, and the offset to poll from next.</summary>
public sealed record McpRunOutputPage(string Stdout, string Stderr, long NextOffset, bool Truncated);
