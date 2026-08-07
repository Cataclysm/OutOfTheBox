// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Text;
using BuildAndTestService.Application.Execution;

namespace BuildAndTestService.Presentation.Execution;

/// <summary>
/// Forwards process output lines to an <see cref="SseWriter"/>, enforcing the per-execution
/// output byte cap - once the cap is reached, further lines are dropped and <see cref="Truncated"/>
/// is set, but the process keeps running (it's reaped by the execution timeout, not by this sink).
/// </summary>
public sealed class SseProcessOutputSink(SseWriter writer, long capBytes) : IProcessOutputSink
{
    private long _bytesWritten;

    /// <summary>Whether output was dropped after the cap was reached.</summary>
    public bool Truncated { get; private set; }

    /// <inheritdoc />
    public Task OnStandardOutputAsync(string line, CancellationToken cancellationToken) =>
        WriteIfUnderCapAsync(writer.WriteStandardOutputAsync, line, cancellationToken);

    /// <inheritdoc />
    public Task OnStandardErrorAsync(string line, CancellationToken cancellationToken) =>
        WriteIfUnderCapAsync(writer.WriteStandardErrorAsync, line, cancellationToken);

    private async Task WriteIfUnderCapAsync(Func<string, CancellationToken, Task> write, string line, CancellationToken cancellationToken)
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
        await write(line, cancellationToken);
    }
}
