// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Application.Diagnostics;

/// <summary>The kind of application Windows' Restart Manager reports a locking process as - mirrors the <c>RM_APP_TYPE</c> Win32 enum.</summary>
public enum FileLockApplicationType
{
    /// <summary>Restart Manager could not determine the application type.</summary>
    Unknown = 0,

    /// <summary>A process with a visible main window.</summary>
    MainWindow = 1,

    /// <summary>A process with a visible window that isn't its main one.</summary>
    OtherWindow = 2,

    /// <summary>A Windows service.</summary>
    Service = 3,

    /// <summary>A Windows Explorer process.</summary>
    Explorer = 4,

    /// <summary>A console application.</summary>
    Console = 5,

    /// <summary>A critical system process that cannot be restarted.</summary>
    Critical = 1000,
}

/// <summary>One process currently holding a file open, as reported by Windows' Restart Manager.</summary>
/// <param name="ProcessId">The locking process's id.</param>
/// <param name="ApplicationName">The application name Restart Manager itself reports for this process.</param>
/// <param name="ApplicationType">The kind of application Restart Manager classifies this process as.</param>
/// <param name="IsRestartable">Whether Restart Manager considers this process safe to restart automatically.</param>
public sealed record FileLockingProcess(int ProcessId, string ApplicationName, FileLockApplicationType ApplicationType, bool IsRestartable);
