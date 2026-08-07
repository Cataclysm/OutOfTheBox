// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Application.Monitoring;

/// <summary>Abstraction over the system clock, so time-dependent logic (delta-sampling, live buffer eviction) is testable with controlled timestamps.</summary>
public interface IClock
{
    /// <summary>The current UTC time.</summary>
    DateTimeOffset UtcNow { get; }
}
