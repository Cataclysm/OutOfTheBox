// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using System.Diagnostics;
using System.Runtime.Versioning;
using OutOfTheBox.Application.Concurrency;
using OutOfTheBox.Application.Monitoring;
using Microsoft.Extensions.Logging;

namespace OutOfTheBox.Infrastructure.Monitoring;

/// <inheritdoc cref="IProcessMonitor" />
[SupportedOSPlatform("windows")]
public sealed class ProcessMonitor(RunRegistry runRegistry, ILogger<ProcessMonitor> logger) : IProcessMonitor
{
    /// <inheritdoc />
    public async Task<bool> KillAsync(int processId, DateTime expectedStartTime, CancellationToken cancellationToken)
    {
        var trackedRoots = runRegistry.GetTrackedProcessRoots();
        if (trackedRoots.Count == 0)
        {
            return false;
        }

        // A fresh process-tree walk, not the last-sampled snapshot - kills are rare, operator-triggered
        // actions, so re-verifying scope from scratch here is cheap relative to the certainty it
        // buys, per design.md's "Kill scope enforcement" decision.
        IReadOnlyDictionary<int, ProcessInfo> allProcesses;
        try
        {
            allProcesses = await ProcessTree.GetAllProcessesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Without this, a failed snapshot would surface only as an unhandled exception in
            // the Blazor circuit handling the operator's kill click - visible as "something broke,"
            // but with no indication why. Fails the kill attempt (matching every other "couldn't
            // verify scope, refuse to kill" path's false return) rather than propagating.
            logger.LogError(ex, "Failed to enumerate processes while attempting to kill process {ProcessId}.", processId);
            return false;
        }

        var descendantsByRoot = ProcessTree.DiscoverDescendants(allProcesses, [.. trackedRoots.Select(r => r.ProcessId)]);

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
