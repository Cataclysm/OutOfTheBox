// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using OutOfTheBox.Application.Execution;
using OutOfTheBox.Domain.Execution;

namespace OutOfTheBox.Infrastructure.Execution;

/// <inheritdoc cref="IPathSanitizer" />
/// <remarks>
/// Which argument values name a filesystem path at all is a pure decision handled by
/// <see cref="CommandPathArgumentPolicy"/> (Domain, no IO) - this class's own job is just the
/// IO-touching half, checking each one against <see cref="IWorkingDirectoryResolver"/>'s real
/// canonicalization/containment logic.
/// </remarks>
public sealed class PathSanitizer(IWorkingDirectoryResolver workingDirectoryResolver) : IPathSanitizer
{
    /// <inheritdoc />
    public string? Validate(string executable, IReadOnlyList<string> arguments, string confinedRoot)
    {
        foreach (var candidate in CommandPathArgumentPolicy.ExtractCandidatePaths(executable, arguments))
        {
            if (RejectIfEscaping(candidate.Label, candidate.Value, confinedRoot) is string rejection)
            {
                return rejection;
            }
        }

        return null;
    }

    private string? RejectIfEscaping(string flagLabel, string value, string confinedRoot)
    {
        if (string.IsNullOrWhiteSpace(value) || workingDirectoryResolver.ResolveWithinRoot(confinedRoot, value).IsAllowed)
        {
            return null;
        }

        return $"'{flagLabel}' value '{value}' resolves outside the confined repository.";
    }
}
