// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Diagnostics;
using System.Runtime.Versioning;
using OutOfTheBox.Application.Concurrency;
using OutOfTheBox.Application.Monitoring;

namespace OutOfTheBox.Infrastructure.Monitoring;

/// <inheritdoc cref="IProcessMonitor" />
[SupportedOSPlatform("windows")]
public sealed class ProcessMonitor(RunRegistry runRegistry) : IProcessMonitor
{
    /// <inheritdoc />
    public async Task<bool> KillAsync(int processId, DateTime expectedStartTime, CancellationToken cancellationToken)
    {
        var trackedRoots = runRegistry.GetTrackedProcessRoots();
        if (trackedRoots.Count == 0)
        {
            return false;
        }

        // A fresh WMI walk, not the last-sampled snapshot - kills are rare, operator-triggered
        // actions, so re-verifying scope from scratch here is cheap relative to the certainty it
        // buys, per design.md's "Kill scope enforcement" decision.
        var allProcesses = await WmiProcessTree.GetAllProcessesAsync(cancellationToken);
        var descendantsByRoot = WmiProcessTree.DiscoverDescendants(allProcesses, [.. trackedRoots.Select(r => r.ProcessId)]);

        var isInScope = descendantsByRoot.Values.Any(tree => tree.Contains(processId));
        if (!isInScope)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(processId);

            // Guards against PID reuse: the caller's PID came from a possibly-seconds-old
            // resource-sample snapshot, and Windows recycles process ids - if the live process at
            // this id started at a different time than what the caller last observed, it's a
            // different, unrelated process wearing the same PID, and killing it would be exactly
            // the "out of scope" mistake this whole verification exists to prevent.
            if (process.StartTime != expectedStartTime)
            {
                return false;
            }

            process.Kill(entireProcessTree: true);
            return true;
        }
        catch (ArgumentException)
        {
            // No longer running.
            return false;
        }
        catch (InvalidOperationException)
        {
            // Exited between the StartTime check and Kill - benign race.
            return false;
        }
    }
}
