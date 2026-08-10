// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Text;
using OutOfTheBox.Application.Events;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Domain.Runs;

namespace OutOfTheBox.Infrastructure.Repositories;

/// <summary>
/// Accumulates a repository clone's stdout/stderr (cap-respecting, for persistence into its
/// <see cref="Run"/> row) and publishes an <see cref="RunEventType.OutputLine"/> event per forwarded
/// line, the same accumulate-and-publish pattern <c>McpProcessOutputSink</c> uses for
/// <c>dotnet</c>/<c>git</c> runs - a clone has no <c>read_run_output</c>-style poll consumer of its
/// own (it's started from Blazor, not an MCP tool call), so this is that same pattern without the
/// offset-addressable buffer half.
/// </summary>
public sealed class RepositoryCloneOutputSink(IRunEventBus runEventBus, Guid runId, string repositoryPath, long capBytes) : IProcessOutputSink
{
    private readonly StringBuilder _stdout = new();
    private readonly StringBuilder _stderr = new();
    private long _bytesWritten;

    /// <summary>Whether output was dropped after the cap was reached.</summary>
    public bool Truncated { get; private set; }

    /// <summary>Standard output accumulated so far, truncated the same way further lines are dropped.</summary>
    public string Stdout => _stdout.ToString();

    /// <summary>Standard error accumulated so far, truncated the same way further lines are dropped.</summary>
    public string Stderr => _stderr.ToString();

    /// <inheritdoc />
    public Task OnStandardOutputAsync(string line, CancellationToken cancellationToken) =>
        RecordIfUnderCapAsync(_stdout, "stdout", line);

    /// <inheritdoc />
    public Task OnStandardErrorAsync(string line, CancellationToken cancellationToken) =>
        RecordIfUnderCapAsync(_stderr, "stderr", line);

    private Task RecordIfUnderCapAsync(StringBuilder accumulator, string stream, string line)
    {
        if (Truncated)
        {
            return Task.CompletedTask;
        }

        var lineBytes = Encoding.UTF8.GetByteCount(line);
        if (_bytesWritten + lineBytes > capBytes)
        {
            Truncated = true;
            return Task.CompletedTask;
        }

        _bytesWritten += lineBytes;
        accumulator.AppendLine(line);
        runEventBus.Publish(new RunEvent(runId, RunKind.RepositoryClone, RunEventType.OutputLine, repositoryPath) { OutputStream = stream, OutputLine = line });

        return Task.CompletedTask;
    }
}
