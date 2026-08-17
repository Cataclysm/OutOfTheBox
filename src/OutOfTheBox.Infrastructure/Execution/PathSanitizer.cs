// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Execution;

namespace OutOfTheBox.Infrastructure.Execution;

/// <inheritdoc cref="IPathSanitizer" />
public sealed class PathSanitizer(IWorkingDirectoryResolver workingDirectoryResolver) : IPathSanitizer
{
    // dotnet build/test's own output-redirecting flags - both take their value as the next
    // argument or via "=value", handled by ValidateValueFlags below.
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

    /// <inheritdoc />
    public string? Validate(string executable, IReadOnlyList<string> arguments, string confinedRoot)
    {
        var valueFlags = executable switch
        {
            "dotnet" => DotnetValueFlags,
            "git" => GitValueFlags,
            _ => [],
        };

        for (var i = 0; i < arguments.Count; i++)
        {
            if (executable == "dotnet" && ValidateMsBuildProperties(arguments[i], confinedRoot) is string propertyRejection)
            {
                return propertyRejection;
            }

            if (ValidateValueFlag(valueFlags, arguments, i, confinedRoot) is string flagRejection)
            {
                return flagRejection;
            }
        }

        return null;
    }

    private string? ValidateValueFlag(string[] valueFlags, IReadOnlyList<string> arguments, int index, string confinedRoot)
    {
        var argument = arguments[index];

        foreach (var flag in valueFlags)
        {
            if (string.Equals(argument, flag, StringComparison.OrdinalIgnoreCase))
            {
                // No value follows (a bare trailing flag) - nothing to validate here; the real CLI
                // reports its own usage error for a missing value.
                return index + 1 < arguments.Count ? RejectIfEscaping(flag, arguments[index + 1], confinedRoot) : null;
            }

            var prefix = flag + "=";
            if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return RejectIfEscaping(flag, argument[prefix.Length..], confinedRoot);
            }
        }

        return null;
    }

    private string? ValidateMsBuildProperties(string argument, string confinedRoot)
    {
        const string prefix = "-p:";
        if (!argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
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

            var propertyValue = segment[(equalsIndex + 1)..];
            if (RejectIfEscaping($"-p:{propertyName}", propertyValue, confinedRoot) is string rejection)
            {
                return rejection;
            }
        }

        return null;
    }

    private static bool IsKnownPathProperty(string name) =>
        KnownDotnetPathProperties.Contains(name, StringComparer.OrdinalIgnoreCase)
        || name.EndsWith("Path", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("Dir", StringComparison.OrdinalIgnoreCase);

    private string? RejectIfEscaping(string flagLabel, string value, string confinedRoot)
    {
        if (string.IsNullOrWhiteSpace(value) || workingDirectoryResolver.ResolveWithinRoot(confinedRoot, value).IsAllowed)
        {
            return null;
        }

        return $"'{flagLabel}' value '{value}' resolves outside the confined repository.";
    }
}
