using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace BuildAndTestService.Presentation.Execution;

/// <summary>
/// Writes Server-Sent Events for the command-execution endpoint's response stream: <c>stdout</c>/
/// <c>stderr</c> data events per output line, a terminal <c>done</c> event with the exit code and
/// truncation flag, or a terminal <c>error</c> event with a reason of <c>validation</c>,
/// <c>timeout</c>, or <c>cancelled</c>.
/// </summary>
public sealed class SseWriter(HttpResponse response)
{
    /// <summary>Writes a <c>stdout</c> data event for one output line.</summary>
    public Task WriteStandardOutputAsync(string line, CancellationToken cancellationToken) =>
        WriteEventAsync("stdout", line, cancellationToken);

    /// <summary>Writes a <c>stderr</c> data event for one output line.</summary>
    public Task WriteStandardErrorAsync(string line, CancellationToken cancellationToken) =>
        WriteEventAsync("stderr", line, cancellationToken);

    /// <summary>Writes the terminal <c>done</c> event for a run that completed with an exit code.</summary>
    public Task WriteDoneAsync(int exitCode, bool truncated, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new { exitCode, truncated });
        return WriteEventAsync("done", payload, cancellationToken);
    }

    /// <summary>Writes the terminal <c>error</c> event for a run that never produced an exit code.</summary>
    public Task WriteErrorAsync(string reason, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new { reason });
        return WriteEventAsync("error", payload, cancellationToken);
    }

    private async Task WriteEventAsync(string eventName, string data, CancellationToken cancellationToken)
    {
        // SSE "data:" lines can't contain raw newlines. Program output lines are already single
        // lines by construction (split on newline by the process reader before reaching here),
        // and the JSON payloads above are single-line by System.Text.Json's default serialization.
        await response.WriteAsync($"event: {eventName}\ndata: {data}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
