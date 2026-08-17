// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Application.Execution;

/// <summary>
/// Scans a <c>dotnet</c>/<c>git</c> argument list for known path-bearing flags/MSBuild properties
/// (<c>--output</c>, <c>-p:OutputPath=</c>, <c>git log --output=</c>, ...) and confines each
/// recognized value the same way <see cref="IWorkingDirectoryResolver"/> already confines the
/// <c>workingDirectory</c> parameter itself - closing the gap where the working directory is
/// correctly confined but an argument value could still point the command's own output/inspection
/// elsewhere on the host. A heuristic over a curated, known flag set, not a full CLI parser - an
/// unrecognized flag is left alone, the same "best-effort, errs toward under- not over-blocking"
/// posture <c>GitAuthFailureClassifier</c> already documents for a similar free-text heuristic.
/// </summary>
public interface IPathSanitizer
{
    /// <summary>
    /// Returns <see langword="null"/> if every recognized path-bearing value in
    /// <paramref name="arguments"/> resolves inside <paramref name="confinedRoot"/>, or a specific
    /// rejection reason (naming the offending flag and value) otherwise.
    /// </summary>
    /// <param name="executable">"dotnet" or "git" - selects which flags/properties are recognized.</param>
    /// <param name="arguments">The full argument list, including the subcommand as the first element.</param>
    /// <param name="confinedRoot">The already-confined, fully-resolved working directory any recognized path value must resolve inside.</param>
    string? Validate(string executable, IReadOnlyList<string> arguments, string confinedRoot);
}
