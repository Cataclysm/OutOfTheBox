// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using OutOfTheBox.Application.Execution;

namespace OutOfTheBox.Infrastructure.Execution;

/// <summary>
/// Runs <see cref="ProcessRunRequest.Executable"/> (<c>dotnet</c> or <c>git</c> - always fixed by
/// the calling endpoint, never caller-supplied) via <see cref="Process"/> with
/// <c>UseShellExecute = false</c> and arguments passed through
/// <see cref="ProcessStartInfo.ArgumentList"/> - each element becomes one literal argv entry, so
/// the OS never invokes a shell to parse them (no injection via shell metacharacters, regardless
/// of what a caller's argument string contains).
/// </summary>
public sealed class CliProcessRunner : IProcessRunner
{
    /// <inheritdoc />
    public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, IProcessOutputSink outputSink, CancellationToken cancellationToken, Action<int>? onStarted = null)
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
        onStarted?.Invoke(process.Id);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (request.StandardInput is not null)
        {
            await process.StandardInput.WriteAsync(request.StandardInput);
            process.StandardInput.Close();
        }

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
            FileName = request.Executable,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = request.StandardInput is not null,
            CreateNoWindow = true,
            // Without this, .NET decodes the child's redirected streams using the console's own
            // output code page (on Windows, typically a legacy ANSI/OEM one, not UTF-8) - but git
            // itself writes commit messages (author names, subjects, bodies) as UTF-8 by default
            // regardless of console codepage, since that's how git stores commit objects. Decoding
            // those UTF-8 bytes with the wrong codepage doesn't fail outright, it just mangles any
            // multi-byte sequence (accented characters, emoji, other symbols) into mojibake -
            // exactly what a commit message containing an emoji rendered as on the dashboard. `dotnet`
            // itself already defaults to UTF-8 output, so this is a no-op for that executable.
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
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
