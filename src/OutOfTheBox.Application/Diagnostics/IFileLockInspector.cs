// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Application.Diagnostics;

/// <summary>Identifies which process(es) currently have a specific file open, for the <c>get_file_lock_info</c> MCP tool.</summary>
public interface IFileLockInspector
{
    /// <summary>Returns every process currently holding <paramref name="filePath"/> open - empty if the file is not locked by anything.</summary>
    Task<IReadOnlyList<FileLockingProcess>> GetLockingProcessesAsync(string filePath, CancellationToken cancellationToken);
}
