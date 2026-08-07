// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Monitoring;

namespace OutOfTheBox.Infrastructure.Monitoring;

/// <inheritdoc cref="IClock" />
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
