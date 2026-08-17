// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Domain.Execution;

/// <summary>
/// Pure decision rule: given a fixed executable ("dotnet" or "git") and the full argument list an MCP
/// caller supplied, which of those arguments' values name a filesystem path at all - the "what looks
/// like a path" half of path confinement. Whether a given value actually escapes the confined
/// repository is a separate, IO-touching concern (<c>IWorkingDirectoryResolver</c>, Infrastructure);
/// this type only ever inspects already-in-memory strings, the same "no IO, not even a NuGet package
/// beyond the BCL" bar every other Domain policy in this namespace holds itself to (see
/// <see cref="CommandSubcommandPolicy"/>'s own remarks).
/// </summary>
public static class CommandPathArgumentPolicy
{
    // dotnet build/test's own output-redirecting flags - both take their value as the next
    // argument or via "=value", handled by ExtractValueFlagPaths below.
    private static readonly string[] DotnetValueFlags = ["-o", "--output", "--results-directory"];

    // git log/diff/show's own write-to-file flag.
    private static readonly string[] GitValueFlags = ["--output"];

    // MSBuild properties whose value is always a filesystem path/directory, regardless of name
    // pattern - kept alongside the generic "ends with Path/Dir" catch-all below so the common ones
    // are recognized even if a future MSBuild version renames the generic suffix convention.
    private static readonly string[] KnownDotnetPathProperties =
    [
        "OutputPath", "BaseOutputPath", "IntermediateOutputPath", "BaseIntermediateOutputPath",
        "PublishDir", "PackageOutputPath", "RestorePackagesPath", "MSBuildProjectExtensionsPath", "ArtifactsPath",
    ];

    /// <summary>One argument value that names a filesystem path, and the flag/property label it came from (for the rejection message a caller builds if it turns out to escape).</summary>
    public readonly record struct CandidatePathArgument(string Label, string Value);

    /// <summary>Every path-bearing value in <paramref name="arguments"/> for <paramref name="executable"/> - a value flag's own value (<c>-o out</c>, <c>--output=out</c>) or a known MSBuild path property (<c>-p:OutputPath=out</c>, including multiple semicolon-separated properties in one token).</summary>
    public static IEnumerable<CandidatePathArgument> ExtractCandidatePaths(string executable, IReadOnlyList<string> arguments)
    {
        var valueFlags = executable switch
        {
            "dotnet" => DotnetValueFlags,
            "git" => GitValueFlags,
            _ => [],
        };

        for (var i = 0; i < arguments.Count; i++)
        {
            if (executable == "dotnet")
            {
                foreach (var candidate in ExtractMsBuildPropertyPaths(arguments[i]))
                {
                    yield return candidate;
                }
            }

            if (ExtractValueFlagPath(valueFlags, arguments, i) is CandidatePathArgument valueFlagCandidate)
            {
                yield return valueFlagCandidate;
            }
        }
    }

    private static CandidatePathArgument? ExtractValueFlagPath(string[] valueFlags, IReadOnlyList<string> arguments, int index)
    {
        var argument = arguments[index];

        foreach (var flag in valueFlags)
        {
            if (string.Equals(argument, flag, StringComparison.OrdinalIgnoreCase))
            {
                // No value follows (a bare trailing flag) - nothing to extract here; the real CLI
                // reports its own usage error for a missing value.
                return index + 1 < arguments.Count ? new CandidatePathArgument(flag, arguments[index + 1]) : null;
            }

            var prefix = flag + "=";
            if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return new CandidatePathArgument(flag, argument[prefix.Length..]);
            }
        }

        return null;
    }

    private static IEnumerable<CandidatePathArgument> ExtractMsBuildPropertyPaths(string argument)
    {
        const string prefix = "-p:";
        if (!argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        // MSBuild allows multiple properties in one -p: token, semicolon-separated
        // ("-p:OutputPath=x;BaseOutputPath=y").
        foreach (var segment in argument[prefix.Length..].Split(';'))
        {
            var equalsIndex = segment.IndexOf('=');
            if (equalsIndex <= 0)
            {
                continue;
            }

            var propertyName = segment[..equalsIndex];
            if (!IsKnownPathProperty(propertyName))
            {
                continue;
            }

            yield return new CandidatePathArgument($"-p:{propertyName}", segment[(equalsIndex + 1)..]);
        }
    }

    private static bool IsKnownPathProperty(string name) =>
        KnownDotnetPathProperties.Contains(name, StringComparer.OrdinalIgnoreCase)
        || name.EndsWith("Path", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("Dir", StringComparison.OrdinalIgnoreCase);
}
