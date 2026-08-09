// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Presentation.Dashboard;

/// <summary>What a <see cref="FileTreeNode"/> asks the root <see cref="FileTree"/> to show via its shared <see cref="ConfirmDialog"/>.</summary>
public readonly record struct FileTreeConfirmRequest(string Title, string Message, Func<Task> OnConfirmed, string ConfirmLabel);

/// <summary>What a <see cref="FileTreeNode"/> asks the root <see cref="FileTree"/> to show via its shared <see cref="FilePreviewDialog"/>.</summary>
public readonly record struct FilePreviewRequest(string RepositoryName, string RelativePath, string Name, string DownloadUrl);
