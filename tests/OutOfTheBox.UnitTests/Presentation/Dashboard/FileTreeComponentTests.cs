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
    // Just the magic-number prefix each format needs to be recognized - FilePreviewDialog's sniff
    // never validates that the rest of the file is a well-formed image, so a full real image isn't
    // needed to exercise it.
    private static readonly byte[] PngMagicBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    // A NUL byte is the classic binary signal (the same one git itself uses) - guaranteed to fail
    // the text sniff regardless of what follows it.
    private static readonly byte[] TrueBinaryBytes = [0x00, 0x01, 0x02, 0xFF, 0xFE, 0x10];

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
        File.WriteAllBytes(Path.Combine(_repositoryRoot, "pixel.png"), PngMagicBytes);

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
    public void An_image_previews_correctly_even_with_a_text_extension()
    {
        // The actual point of content-based classification: the name lies, the bytes don't.
        File.WriteAllBytes(Path.Combine(_repositoryRoot, "not-really-text.txt"), PngMagicBytes);

        var cut = Render<FileTree>(parameters => parameters.Add(p => p.RepositoryName, "repo"));
        cut.WaitForAssertion(() => Assert.Contains("not-really-text.txt", cut.Markup));

        cut.Find(".file-tree-row").Click();

        cut.WaitForAssertion(() => Assert.Contains("<img", cut.Markup));
    }

    [Fact]
    public void Plain_text_previews_correctly_even_with_an_image_extension()
    {
        File.WriteAllText(Path.Combine(_repositoryRoot, "not-really.png"), "just plain text, not image bytes");

        var cut = Render<FileTree>(parameters => parameters.Add(p => p.RepositoryName, "repo"));
        cut.WaitForAssertion(() => Assert.Contains("not-really.png", cut.Markup));

        cut.Find(".file-tree-row").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("<img", cut.Markup);
            Assert.Contains("just plain text, not image bytes", cut.Markup);
        });
    }

    [Fact]
    public void A_Markdown_file_renders_as_formatted_HTML_with_raw_HTML_disabled()
    {
        File.WriteAllText(
            Path.Combine(_repositoryRoot, "notes.md"),
            "# Heading\n\nSome **bold** text.\n\n<script>alert(1)</script>");

        var cut = Render<FileTree>(parameters => parameters.Add(p => p.RepositoryName, "repo"));
        cut.WaitForAssertion(() => Assert.Contains("notes.md", cut.Markup));

        cut.Find(".file-tree-row").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("markdown-preview", cut.Markup);
            Assert.Contains("<h1", cut.Markup);
            Assert.Contains("<strong>bold</strong>", cut.Markup);
            // DisableHtml() must actually be in effect - a cloned repository's README is not trusted
            // content, and a real <script> element here would be a stored-XSS hole into the
            // operator's authenticated dashboard session.
            Assert.DoesNotContain("<script>", cut.Markup);
        });
    }

    [Fact]
    public void Clicking_a_truly_binary_file_row_shows_no_preview_available()
    {
        File.WriteAllBytes(Path.Combine(_repositoryRoot, "data.dat"), TrueBinaryBytes);

        var cut = Render<FileTree>(parameters => parameters.Add(p => p.RepositoryName, "repo"));
        cut.WaitForAssertion(() => Assert.Contains("data.dat", cut.Markup));

        cut.Find(".file-tree-row").Click();

        cut.WaitForAssertion(() => Assert.Contains("No preview available", cut.Markup));
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
        File.WriteAllBytes(Path.Combine(_repositoryRoot, "data.dat"), TrueBinaryBytes);

        var cut = Render<FileTree>(parameters => parameters.Add(p => p.RepositoryName, "repo"));
        cut.WaitForAssertion(() => Assert.Contains("data.dat", cut.Markup));

        // The delete button's own click has @onclick:stopPropagation, matching the real DOM shape -
        // this is the exact path that crashed the circuit before the EventCallback-bubbling fix, via
        // the pre-existing ConfirmDialogRef parameter this change also replaced.
        cut.Find("button[title=Delete]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Permanently delete 'data.dat'", cut.Markup));
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
