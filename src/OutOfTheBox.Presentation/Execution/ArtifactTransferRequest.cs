// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Presentation.Execution;

/// <summary>The JSON request body for <c>POST /artifacts</c>.</summary>
public sealed record ArtifactTransferRequest(string? Repo, string? Path);
