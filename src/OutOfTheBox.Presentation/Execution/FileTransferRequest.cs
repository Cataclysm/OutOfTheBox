// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Presentation.Execution;

/// <summary>The JSON request body for <c>POST /files</c>.</summary>
public sealed record FileTransferRequest(string? Repository, string? Path);
