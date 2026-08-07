// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Application.Monitoring;

/// <summary>
/// Terminates a spawned process, scoped to only what a currently-tracked run's process tree
/// actually owns - per design.md's "Kill scope enforcement" decision: server-side, always
/// re-verified immediately before killing, never trusting a possibly-stale client-supplied PID
/// alone. Called directly from Blazor component code-behind (the kill button), never through an
/// HTTP endpoint - the same in-process pattern as <c>IRepositoryManager</c>.
/// </summary>
public interface IProcessMonitor
{
    /// <summary>
    /// Kills <paramref name="processId"/> and its descendants, but only if it currently resolves to
    /// a live process that is (a) part of a currently-tracked run's process tree and (b) still the
    /// same process the caller last observed - <paramref name="expectedStartTime"/> is compared
    /// against the live process's own <c>StartTime</c> to reject a stale PID that's since been
    /// reused by an unrelated process. Returns <see langword="false"/> without killing anything if
    /// either check fails.
    /// </summary>
    Task<bool> KillAsync(int processId, DateTime expectedStartTime, CancellationToken cancellationToken);
}
