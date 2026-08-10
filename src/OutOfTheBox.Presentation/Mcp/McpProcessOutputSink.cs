// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Events;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Domain.Runs;

namespace OutOfTheBox.Presentation.Mcp;

/// <summary>
/// Forwards process output lines into an <see cref="McpRunOutputBuffer"/> so <c>read_run_output</c>
/// can poll them, for runs started by <c>dotnet_run</c>/<c>git_run</c>. Also publishes an
/// <see cref="RunEventType.OutputLine"/> event per accepted line, so a run started via MCP shows live
/// output on its dashboard run-detail subpage too - a run's behavior elsewhere in the system
/// shouldn't depend on which interface started it.
/// </summary>
public sealed class McpProcessOutputSink(McpRunOutputBuffer buffer, IRunEventBus runEventBus, Guid runId, RunKind kind, string repositoryPath) : IProcessOutputSink
{
    /// <summary>Whether output was dropped after the configured cap was reached.</summary>
    public bool Truncated => buffer.Truncated;

    /// <summary>Standard output accumulated so far, possibly truncated.</summary>
    public string Stdout => buffer.Stdout;

    /// <summary>Standard error accumulated so far, possibly truncated.</summary>
    public string Stderr => buffer.Stderr;

    /// <inheritdoc />
    public Task OnStandardOutputAsync(string line, CancellationToken cancellationToken) => Write("stdout", line);

    /// <inheritdoc />
    public Task OnStandardErrorAsync(string line, CancellationToken cancellationToken) => Write("stderr", line);

    private Task Write(string stream, string line)
    {
        if (buffer.Append(stream, line))
        {
            runEventBus.Publish(new RunEvent(runId, kind, RunEventType.OutputLine, repositoryPath) { OutputStream = stream, OutputLine = line });
        }

        return Task.CompletedTask;
    }
}
