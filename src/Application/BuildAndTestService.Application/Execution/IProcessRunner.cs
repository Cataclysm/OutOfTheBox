namespace BuildAndTestService.Application.Execution;

/// <summary>
/// Runs the <c>dotnet</c> CLI as a child process, forwarding its output to
/// <see cref="IProcessOutputSink"/> as it's produced. Cancelling the run's cancellation token
/// (via a caller-triggered cancel or an execution timeout) kills the process tree and the call
/// throws <see cref="OperationCanceledException"/>; a process that exits on its own (any exit
/// code) instead returns normally.
/// </summary>
public interface IProcessRunner
{
    /// <summary>Runs the process described by <paramref name="request"/> to completion or cancellation.</summary>
    Task<ProcessRunResult> RunAsync(ProcessRunRequest request, IProcessOutputSink outputSink, CancellationToken cancellationToken);
}
