// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Domain.Repositories;
using OutOfTheBox.Presentation.Dashboard;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace OutOfTheBox.UnitTests.Presentation.Dashboard;

/// <summary>
/// Renders the real <see cref="FileTreeNode"/> component via bUnit, covering the click-to-preview
/// behavior added alongside the constant-row-height/icons-then-size CSS fix: a text file's row click
/// opens <see cref="FilePreviewDialog"/> with its content, an image file's opens it with an
/// <c>&lt;img&gt;</c>, an unsupported file type's does nothing, and a directory row still just
/// toggles expand rather than attempting a preview.
/// </summary>
public sealed class FileTreeNodeComponentTests : BunitContext, IDisposable
{
    private readonly string _repositoryRoot = Directory.CreateTempSubdirectory("filetreenode-tests-").FullName;

    public FileTreeNodeComponentTests()
    {
        // ShowAsync's dialog-open call is fire-and-forget for these tests' purposes - only the
        // resulting markup matters, not that a real <dialog> actually became modal (bUnit has no
        // browser behind it for that to mean anything).
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IRepositoryFileBrowser>(new FakeRepositoryFileBrowser(_repositoryRoot));
    }

    [Fact]
    public void Clicking_a_text_file_row_opens_the_preview_dialog_with_its_content()
    {
        File.WriteAllText(Path.Combine(_repositoryRoot, "readme.txt"), "hello from the preview test");

        var (confirmDialog, previewDialog) = RenderDialogs();
        var cut = RenderNode("readme.txt", isDirectory: false, sizeBytes: 28, confirmDialog, previewDialog);

        cut.Find(".file-tree-row").Click();

        previewDialog.WaitForAssertion(() =>
        {
            Assert.Contains("hello from the preview test", previewDialog.Markup);
            Assert.Contains("readme.txt", previewDialog.Markup);
        });
    }

    [Fact]
    public void Clicking_an_image_file_row_opens_the_preview_dialog_with_an_img_tag()
    {
        var (confirmDialog, previewDialog) = RenderDialogs();
        var cut = RenderNode("pixel.png", isDirectory: false, sizeBytes: 69, confirmDialog, previewDialog);

        cut.Find(".file-tree-row").Click();

        previewDialog.WaitForAssertion(() =>
        {
            Assert.Contains("<img", previewDialog.Markup);
            Assert.Contains("/dashboard-files/repo?path=pixel.png", previewDialog.Markup);
        });
    }

    [Fact]
    public void Clicking_an_unsupported_file_type_row_leaves_the_preview_dialog_untouched()
    {
        var (confirmDialog, previewDialog) = RenderDialogs();
        var cut = RenderNode("data.bin", isDirectory: false, sizeBytes: 15, confirmDialog, previewDialog);

        cut.Find(".file-tree-row").Click();

        // Nothing to WaitForAssertion on (no async work is ever kicked off) - a direct check is
        // correct here, not a race.
        Assert.DoesNotContain("data.bin", previewDialog.Markup);
        Assert.DoesNotContain("<img", previewDialog.Markup);
    }

    [Fact]
    public void Clicking_a_directory_row_toggles_expand_instead_of_attempting_a_preview()
    {
        Directory.CreateDirectory(Path.Combine(_repositoryRoot, "subdir"));
        File.WriteAllText(Path.Combine(_repositoryRoot, "subdir", "nested.txt"), "nested content");

        var (confirmDialog, previewDialog) = RenderDialogs();
        var cut = RenderNode("subdir", isDirectory: true, sizeBytes: null, confirmDialog, previewDialog);

        cut.Find(".file-tree-row").Click();

        cut.WaitForAssertion(() => Assert.Contains("nested.txt", cut.Markup));
        Assert.DoesNotContain("subdir", previewDialog.Markup);
    }

    private (IRenderedComponent<ConfirmDialog> Confirm, IRenderedComponent<FilePreviewDialog> Preview) RenderDialogs() =>
        (Render<ConfirmDialog>(), Render<FilePreviewDialog>());

    private IRenderedComponent<FileTreeNode> RenderNode(
        string name, bool isDirectory, long? sizeBytes, IRenderedComponent<ConfirmDialog> confirmDialog, IRenderedComponent<FilePreviewDialog> previewDialog) =>
        Render<FileTreeNode>(parameters => parameters
            .Add(p => p.RepositoryName, "repo")
            .Add(p => p.Name, name)
            .Add(p => p.IsDirectory, isDirectory)
            .Add(p => p.SizeBytes, sizeBytes)
            .Add(p => p.ConfirmDialogRef, confirmDialog.Instance)
            .Add(p => p.PreviewDialogRef, previewDialog.Instance));

    /// <inheritdoc />
    public new void Dispose()
    {
        Directory.Delete(_repositoryRoot, recursive: true);
        base.Dispose();
    }

    private sealed class FakeRepositoryFileBrowser(string repositoryRoot) : IRepositoryFileBrowser
    {
        public Task<IReadOnlyList<RepositoryFileEntry>> ListDirectoryAsync(string repositoryName, string relativePath, CancellationToken cancellationToken)
        {
            var directoryPath = Path.Combine(repositoryRoot, relativePath);
            IReadOnlyList<RepositoryFileEntry> entries = Directory.Exists(directoryPath)
                ? [.. Directory.EnumerateFileSystemEntries(directoryPath).Select(ToEntry)]
                : [];
            return Task.FromResult(entries);
        }

        public Task<RepositoryFileActionResult> DeleteAsync(string repositoryName, string relativePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public Task<RepositoryFileActionResult> RenameAsync(string repositoryName, string relativePath, string newName, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public Task<string?> ResolveConfinedFilePathAsync(string repositoryName, string relativePath, CancellationToken cancellationToken)
        {
            var filePath = Path.Combine(repositoryRoot, relativePath);
            return Task.FromResult(File.Exists(filePath) ? filePath : null);
        }

        private static RepositoryFileEntry ToEntry(string path) => new(
            Path.GetFileName(path), Directory.Exists(path), Directory.Exists(path) ? null : new FileInfo(path).Length, DateTimeOffset.UtcNow);
    }
}
