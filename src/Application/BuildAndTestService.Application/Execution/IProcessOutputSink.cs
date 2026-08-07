namespace BuildAndTestService.Application.Execution;

/// <summary>
/// Receives a running process's stdout/stderr lines as they're produced, so a caller (the
/// SSE-writing endpoint handler) can forward each one immediately rather than waiting for the
/// process to exit.
/// </summary>
public interface IProcessOutputSink
{
    /// <summary>Called for each line of stdout, in order, as it's produced.</summary>
    Task OnStandardOutputAsync(string line, CancellationToken cancellationToken);

    /// <summary>Called for each line of stderr, in order, as it's produced.</summary>
    Task OnStandardErrorAsync(string line, CancellationToken cancellationToken);
}
