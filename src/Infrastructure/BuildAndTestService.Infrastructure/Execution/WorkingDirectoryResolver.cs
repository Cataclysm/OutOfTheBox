using BuildAndTestService.Application.Configuration;
using BuildAndTestService.Application.Execution;
using BuildAndTestService.Domain.PathConfinement;
using Microsoft.Extensions.Options;

namespace BuildAndTestService.Infrastructure.Execution;

/// <summary>
/// Resolves a caller-supplied working directory against <see cref="ServiceOptions.RootDirectory"/>
/// using lexical canonicalization (<see cref="Path.GetFullPath(string)"/>) plus symlink/junction
/// resolution, then defers the actual containment decision to
/// <see cref="PathConfinementPolicy"/>.
/// </summary>
public sealed class WorkingDirectoryResolver(IOptions<ServiceOptions> options) : IWorkingDirectoryResolver
{
    /// <inheritdoc />
    public WorkingDirectoryResolution Resolve(string relativeWorkingDirectory)
    {
        var rootFullPath = Path.GetFullPath(options.Value.RootDirectory);

        string combined;
        try
        {
            // Path.Combine discards the root entirely if relativeWorkingDirectory is itself
            // rooted/absolute (e.g. "C:\Windows"), so an absolute-path escape attempt still ends
            // up correctly rejected by the containment check below rather than needing special-casing.
            combined = Path.GetFullPath(Path.Combine(rootFullPath, relativeWorkingDirectory));
        }
        catch (ArgumentException)
        {
            return WorkingDirectoryResolution.Rejected();
        }

        var finalPath = ResolveSymlinkTarget(combined);

        return PathConfinementPolicy.IsContained(rootFullPath, finalPath)
            ? WorkingDirectoryResolution.Allowed(finalPath)
            : WorkingDirectoryResolution.Rejected();
    }

    /// <summary>
    /// If <paramref name="path"/> is itself a symbolic link or junction, follows it to its final
    /// target so the containment check runs against where the path actually leads, not just its
    /// lexical text. Non-links (including paths that don't exist yet) pass through unchanged.
    /// </summary>
    private static string ResolveSymlinkTarget(string path)
    {
        try
        {
            var finalTarget = Directory.ResolveLinkTarget(path, returnFinalTarget: true);
            return finalTarget?.FullName ?? path;
        }
        catch (IOException)
        {
            return path;
        }
        catch (UnauthorizedAccessException)
        {
            return path;
        }
    }
}
