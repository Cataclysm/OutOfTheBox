// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace BuildAndTestService.Domain.Runs;

/// <summary>
/// Pure rule for computing a run's effective execution timeout, per
/// specs/dotnet-command-execution's "Caller may override the execution timeout per request"
/// requirement: the caller's value is used when supplied, otherwise the configured default -
/// either way, the result is never allowed to exceed the configured maximum.
/// </summary>
public static class ExecutionTimeoutPolicy
{
    /// <summary>Resolves the effective timeout for a run.</summary>
    public static TimeSpan Resolve(TimeSpan? callerSupplied, TimeSpan configuredDefault, TimeSpan configuredMaximum)
    {
        var candidate = callerSupplied ?? configuredDefault;
        return candidate > configuredMaximum ? configuredMaximum : candidate;
    }
}
