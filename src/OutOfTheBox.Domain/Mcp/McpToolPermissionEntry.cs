// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Domain.Mcp;

/// <summary>
/// Whether one MCP tool, or one <c>dotnet_run</c>/<c>git_run</c> subcommand, is currently enabled -
/// per the operator-configurable MCP Settings dashboard page. <paramref name="Key"/> is either a bare
/// tool name (<c>"delete_repository"</c>) or <c>"{executable}:{subcommand}"</c>
/// (<c>"dotnet:publish"</c>, <c>"git:push"</c>) - one flat string-keyed row shape covers both cases
/// without a composite key, which EF Core can't express with a nullable member and "subcommand" is
/// only ever meaningful for two of the twenty tools.
/// </summary>
public sealed record McpToolPermissionEntry(string Key, bool Enabled);
