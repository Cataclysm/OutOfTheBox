// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Diagnostics;
using System.Threading.Channels;
using BuildAndTestService.Application.Execution;

namespace BuildAndTestService.Infrastructure.Execution;

/// <summary>
/// Runs <c>dotnet.exe</c> via <see cref="Process"/> with <c>UseShellExecute = false</c> and
/// arguments passed through <see cref="ProcessStartInfo.ArgumentList"/> - each element becomes
/// one literal argv entry, so the OS never invokes a shell to parse them (no injection via shell
/// metacharacters, regardless of what a caller's argument string contains).
/// </summary>
public sealed class DotnetProcessRunner : IProcessRunner
{
    /// <inheritdoc />
    public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, IProcessOutputSink outputSink, CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<(bool IsError, string Line)>();

        using var process = new Process { StartInfo = BuildStartInfo(request), EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                channel.Writer.TryWrite((false, e.Data));
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                channel.Writer.TryWrite((true, e.Data));
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var killRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // Process exited between the HasExited check and Kill - benign race, nothing to do.
            }
        });

        // Pumping is intentionally decoupled from cancellationToken: even when the run is being
        // killed, already-queued output lines should still be forwarded rather than dropped.
        var pumpTask = PumpOutputAsync(channel.Reader, outputSink, CancellationToken.None);

        OperationCanceledException? cancellation = null;
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException ex)
        {
            cancellation = ex;
        }

        channel.Writer.TryComplete();
        await pumpTask;

        if (cancellation is not null)
        {
            throw cancellation;
        }

        return new ProcessRunResult(process.ExitCode);
    }

    /// <summary>
    /// Builds the <see cref="ProcessStartInfo"/> for <paramref name="request"/>. Extracted as its
    /// own method so tests can verify argument handling (no shell, no string concatenation)
    /// without actually spawning a process.
    /// </summary>
    public static ProcessStartInfo BuildStartInfo(ProcessRunRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static async Task PumpOutputAsync(
        ChannelReader<(bool IsError, string Line)> reader,
        IProcessOutputSink sink,
        CancellationToken cancellationToken)
    {
        await foreach (var (isError, line) in reader.ReadAllAsync(cancellationToken))
        {
            if (isError)
            {
                await sink.OnStandardErrorAsync(line, cancellationToken);
            }
            else
            {
                await sink.OnStandardOutputAsync(line, cancellationToken);
            }
        }
    }
}
