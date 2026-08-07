// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Text;
using OutOfTheBox.Application.Execution;

namespace OutOfTheBox.Presentation.Execution;

/// <summary>
/// Forwards process output lines to an <see cref="SseWriter"/>, enforcing the per-execution
/// output byte cap - once the cap is reached, further lines are dropped and <see cref="Truncated"/>
/// is set, but the process keeps running (it's reaped by the execution timeout, not by this sink).
/// Also accumulates the (possibly truncated) text into <see cref="Stdout"/>/<see cref="Stderr"/>,
/// so the caller can persist exactly what was streamed once the run reaches a terminal state, per
/// specs/run-history's "Persisted output respects the same size limit as streamed output" requirement.
/// </summary>
public sealed class SseProcessOutputSink(SseWriter writer, long capBytes) : IProcessOutputSink
{
    private readonly StringBuilder _stdout = new();
    private readonly StringBuilder _stderr = new();
    private long _bytesWritten;

    /// <summary>Whether output was dropped after the cap was reached.</summary>
    public bool Truncated { get; private set; }

    /// <summary>Standard output accumulated so far, truncated the same way the streamed output was.</summary>
    public string Stdout => _stdout.ToString();

    /// <summary>Standard error accumulated so far, truncated the same way the streamed output was.</summary>
    public string Stderr => _stderr.ToString();

    /// <inheritdoc />
    public Task OnStandardOutputAsync(string line, CancellationToken cancellationToken) =>
        WriteIfUnderCapAsync(writer.WriteStandardOutputAsync, _stdout, line, cancellationToken);

    /// <inheritdoc />
    public Task OnStandardErrorAsync(string line, CancellationToken cancellationToken) =>
        WriteIfUnderCapAsync(writer.WriteStandardErrorAsync, _stderr, line, cancellationToken);

    private async Task WriteIfUnderCapAsync(
        Func<string, CancellationToken, Task> write, StringBuilder accumulator, string line, CancellationToken cancellationToken)
    {
        if (Truncated)
        {
            return;
        }

        var lineBytes = Encoding.UTF8.GetByteCount(line);
        if (_bytesWritten + lineBytes > capBytes)
        {
            Truncated = true;
            return;
        }

        _bytesWritten += lineBytes;
        accumulator.AppendLine(line);
        await write(line, cancellationToken);
    }
}
