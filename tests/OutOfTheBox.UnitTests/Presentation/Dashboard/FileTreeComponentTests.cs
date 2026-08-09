// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Domain.Repositories;
using OutOfTheBox.Presentation.Dashboard;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace OutOfTheBox.UnitTests.Presentation.Dashboard;

/// <summary>
/// Renders the real <see cref="FileTree"/> (not <see cref="FileTreeNode"/> in isolation with
/// hand-fed dialog instances) via bUnit - deliberately, since an earlier version of these tests
/// wired <see cref="ConfirmDialog"/>/<see cref="FilePreviewDialog"/> directly into
/// <c>FileTreeNode.ConfirmDialogRef</c>/<c>PreviewDialogRef</c> parameters and passed, while the real
/// app crashed the whole Blazor circuit on the very first click: those dialog instances are only
/// assigned via <c>@ref</c> once <see cref="FileTree"/>'s own render completes, which happens after a
/// nested <see cref="FileTreeNode"/>'s parameters are evaluated on the render that first creates it,
/// so the reference it received was still null. Rendering the real root and clicking through it is
/// what actually exercises that timing and would have caught it - see FileTreeNode's own remarks on
/// <c>RequestConfirm</c>/<c>RequestPreview</c> for the fix (bubbled EventCallbacks, not instances).
/// </summary>
public sealed class FileTreeComponentTests : BunitContext, IDisposable
{
    private readonly string _repositoryRoot = Directory.CreateTempSubdirectory("filetree-tests-").FullName;

    public FileTreeComponentTests()
    {
        // Dialog-open JS interop is fire-and-forget for these tests' purposes - only the resulting
        // markup matters, not that a real <dialog> actually became modal (bUnit has no browser
        // behind it for that to mean anything).
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IRepositoryFileBrowser>(new FakeRepositoryFileBrowser(_repositoryRoot));
    }

    [Fact]
    public void Clicking_a_text_file_row_opens_the_preview_dialog_with_its_content()
    {
        File.WriteAllText(Path.Combine(_repositoryRoot, "readme.txt"), "hello from the preview test");

        var cut = Render<FileTree>(parameters => parameters.Add(p => p.RepositoryName, "repo"));
        cut.WaitForAssertion(() => Assert.Contains("readme.txt", cut.Markup));

        cut.Find(".file-tree-row").Click();

        cut.WaitForAssertion(() => Assert.Contains("hello from the preview test", cut.Markup));
    }

    [Fact]
    public void Clicking_an_image_file_row_opens_the_preview_dialog_with_an_img_tag()
    {
        File.WriteAllText(Path.Combine(_repositoryRoot, "pixel.png"), "not real image bytes - classification is by extension only");

        var cut = Render<FileTree>(parameters => parameters.Add(p => p.RepositoryName, "repo"));
        cut.WaitForAssertion(() => Assert.Contains("pixel.png", cut.Markup));

        cut.Find(".file-tree-row").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("<img", cut.Markup);
            Assert.Contains("/dashboard-files/repo?path=pixel.png", cut.Markup);
        });
    }

    [Fact]
    public void Clicking_an_unsupported_file_type_row_does_not_crash_or_open_a_dialog()
    {
        File.WriteAllText(Path.Combine(_repositoryRoot, "data.bin"), "binary-ish content");

        var cut = Render<FileTree>(parameters => parameters.Add(p => p.RepositoryName, "repo"));
        cut.WaitForAssertion(() => Assert.Contains("data.bin", cut.Markup));

        cut.Find(".file-tree-row").Click();

        // Nothing to WaitForAssertion on (no async work is ever kicked off for an unsupported type) -
        // a direct check is correct here, not a race. The real regression this guards is the click
        // not throwing at all (an uncaught exception here fails the test on its own).
        Assert.DoesNotContain("<img", cut.Markup);
        Assert.DoesNotContain("run-output", cut.Markup);
    }

    [Fact]
    public void Clicking_a_directory_row_toggles_expand_instead_of_attempting_a_preview()
    {
        Directory.CreateDirectory(Path.Combine(_repositoryRoot, "subdir"));
        File.WriteAllText(Path.Combine(_repositoryRoot, "subdir", "nested.txt"), "nested content");

        var cut = Render<FileTree>(parameters => parameters.Add(p => p.RepositoryName, "repo"));
        cut.WaitForAssertion(() => Assert.Contains("subdir", cut.Markup));

        cut.Find(".file-tree-row").Click();

        cut.WaitForAssertion(() => Assert.Contains("nested.txt", cut.Markup));
    }

    [Fact]
    public void Clicking_delete_on_a_file_opens_the_confirm_dialog_without_crashing()
    {
        File.WriteAllText(Path.Combine(_repositoryRoot, "data.bin"), "binary-ish content");

        var cut = Render<FileTree>(parameters => parameters.Add(p => p.RepositoryName, "repo"));
        cut.WaitForAssertion(() => Assert.Contains("data.bin", cut.Markup));

        // The delete button's own click has @onclick:stopPropagation, matching the real DOM shape -
        // this is the exact path that crashed the circuit before the EventCallback-bubbling fix, via
        // the pre-existing ConfirmDialogRef parameter this change also replaced.
        cut.Find("button[title=Delete]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Permanently delete 'data.bin'", cut.Markup));
    }

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
            throw new NotSupportedException("Not exercised by these tests - only opening the confirm dialog is, not confirming it.");

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
