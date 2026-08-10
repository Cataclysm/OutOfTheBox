// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Domain.Repositories;
using OutOfTheBox.Presentation.Dashboard;
using OutOfTheBox.Presentation.Dashboard.CodePreview;
using Bunit;
using Microsoft.AspNetCore.Hosting;
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

    // Just enough of each magic number for FilePreviewDialog's sniff to recognize the format - same
    // "prefix is enough, no need for a well-formed file" reasoning as PngMagicBytes above.
    private static readonly byte[] PdfMagicBytes = "%PDF-1.4\n"u8.ToArray();
    private static readonly byte[] WavMagicBytes = [(byte)'R', (byte)'I', (byte)'F', (byte)'F', 0x24, 0x00, 0x00, 0x00, (byte)'W', (byte)'A', (byte)'V', (byte)'E'];
    private static readonly byte[] Mp4MagicBytes = [0x00, 0x00, 0x00, 0x20, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m'];

    private readonly string _repositoryRoot = Directory.CreateTempSubdirectory("filetree-tests-").FullName;
    private readonly FakeRepositoryManager _repositoryManager = new();

    public FileTreeComponentTests()
    {
        // Dialog-open JS interop is fire-and-forget for these tests' purposes - only the resulting
        // markup matters, not that a real <dialog> actually became modal (bUnit has no browser
        // behind it for that to mean anything).
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IWebHostEnvironment>(new TestWebHostEnvironment());
        Services.AddSingleton<IRepositoryFileBrowser>(new FakeRepositoryFileBrowser(_repositoryRoot));
        Services.AddSingleton<IRepositoryManager>(_repositoryManager);
        Services.AddScoped<ICodePreviewInterop, CodePreviewInterop>();
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
    public void Word_wrap_checkbox_and_editor_start_from_the_saved_client_side_preference()
    {
        File.WriteAllText(Path.Combine(_repositoryRoot, "readme.txt"), "hello from the preview test");
        JSInterop.Setup<bool>("outOfTheBoxCodePreview.getWordWrapPreference").SetResult(false);

        var cut = Render<FileTree>(parameters => parameters.Add(p => p.RepositoryName, "repo"));
        cut.WaitForAssertion(() => Assert.Contains("readme.txt", cut.Markup));

        cut.Find(".file-tree-row").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.False(cut.Find(".code-preview-controls input[type=checkbox]").HasAttribute("checked"));
            var render = JSInterop.VerifyInvoke("outOfTheBoxCodePreview.render");
            Assert.False((bool)render.Arguments[2]!);
        });
    }

    [Fact]
    public void Toggling_word_wrap_updates_the_live_editor_and_saves_the_new_preference()
    {
        File.WriteAllText(Path.Combine(_repositoryRoot, "readme.txt"), "hello from the preview test");
        JSInterop.Setup<bool>("outOfTheBoxCodePreview.getWordWrapPreference").SetResult(true);

        var cut = Render<FileTree>(parameters => parameters.Add(p => p.RepositoryName, "repo"));
        cut.WaitForAssertion(() => Assert.Contains("readme.txt", cut.Markup));

        cut.Find(".file-tree-row").Click();
        cut.WaitForAssertion(() => Assert.True(cut.Find(".code-preview-controls input[type=checkbox]").HasAttribute("checked")));

        cut.Find(".code-preview-controls input[type=checkbox]").Change(false);

        cut.WaitForAssertion(() =>
        {
            var setWordWrap = JSInterop.VerifyInvoke("outOfTheBoxCodePreview.setWordWrap");
            Assert.False((bool)setWordWrap.Arguments[1]!);
            Assert.False(cut.Find(".code-preview-controls input[type=checkbox]").HasAttribute("checked"));
        });
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

    [Theory]
    [InlineData("Program.cs", "text/x-csharp")]
    [InlineData("data.json", "application/json")]
    [InlineData("app.csproj", "application/xml")]
    [InlineData("index.html", "text/html")]
    [InlineData("styles.css", "text/css")]
    [InlineData("values.yml", "text/x-yaml")]
    [InlineData("Dockerfile", "text/x-dockerfile")]
    public void A_recognized_code_file_previews_with_the_matching_CodeMirror_mime_type(string fileName, string expectedMime)
    {
        File.WriteAllText(Path.Combine(_repositoryRoot, fileName), "content");

        var cut = Render<FileTree>(parameters => parameters.Add(p => p.RepositoryName, "repo"));
        cut.WaitForAssertion(() => Assert.Contains(fileName, cut.Markup));

        cut.Find(".file-tree-row").Click();

        cut.WaitForAssertion(() => Assert.Contains($"data-mime=\"{expectedMime}\"", cut.Markup));
    }

    [Fact]
    public void An_unrecognized_extension_still_previews_as_code_without_a_mime_type()
    {
        File.WriteAllText(Path.Combine(_repositoryRoot, "notes.log"), "some log content");

        var cut = Render<FileTree>(parameters => parameters.Add(p => p.RepositoryName, "repo"));
        cut.WaitForAssertion(() => Assert.Contains("notes.log", cut.Markup));

        cut.Find(".file-tree-row").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("class=\"file-preview-code\"", cut.Markup);
            Assert.Contains("some log content", cut.Markup);
        });
    }

    [Fact]
    public void Clicking_a_PDF_file_row_opens_the_preview_dialog_with_an_iframe()
    {
        File.WriteAllBytes(Path.Combine(_repositoryRoot, "doc.pdf"), PdfMagicBytes);

        var cut = Render<FileTree>(parameters => parameters.Add(p => p.RepositoryName, "repo"));
        cut.WaitForAssertion(() => Assert.Contains("doc.pdf", cut.Markup));

        cut.Find(".file-tree-row").Click();

        cut.WaitForAssertion(() => Assert.Contains("<iframe", cut.Markup));
    }

    [Fact]
    public void Clicking_an_audio_file_row_opens_the_preview_dialog_with_an_audio_tag()
    {
        File.WriteAllBytes(Path.Combine(_repositoryRoot, "sound.wav"), WavMagicBytes);

        var cut = Render<FileTree>(parameters => parameters.Add(p => p.RepositoryName, "repo"));
        cut.WaitForAssertion(() => Assert.Contains("sound.wav", cut.Markup));

        cut.Find(".file-tree-row").Click();

        cut.WaitForAssertion(() => Assert.Contains("<audio", cut.Markup));
    }

    [Fact]
    public void Clicking_a_video_file_row_opens_the_preview_dialog_with_a_video_tag()
    {
        File.WriteAllBytes(Path.Combine(_repositoryRoot, "clip.mp4"), Mp4MagicBytes);

        var cut = Render<FileTree>(parameters => parameters.Add(p => p.RepositoryName, "repo"));
        cut.WaitForAssertion(() => Assert.Contains("clip.mp4", cut.Markup));

        cut.Find(".file-tree-row").Click();

        cut.WaitForAssertion(() => Assert.Contains("<video", cut.Markup));
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

    [Fact]
    public void Dirty_files_only_filter_hides_clean_files_and_auto_expands_folders_containing_a_dirty_one()
    {
        Directory.CreateDirectory(Path.Combine(_repositoryRoot, "subdir"));
        File.WriteAllText(Path.Combine(_repositoryRoot, "clean.txt"), "clean");
        File.WriteAllText(Path.Combine(_repositoryRoot, "subdir", "dirty.txt"), "dirty");
        _repositoryManager.DirtyPaths = ["subdir/dirty.txt"];

        var cut = Render<FileTree>(parameters => parameters.Add(p => p.RepositoryName, "repo"));
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("clean.txt", cut.Markup);
            Assert.Contains("subdir", cut.Markup);
        });

        cut.Find("input[type=checkbox]").Change(true);

        cut.WaitForAssertion(() =>
        {
            // subdir auto-expands (it contains a dirty file) without a manual click, and its own
            // dirty child is now visible - the whole point of the filter over just hiding rows.
            Assert.Contains("dirty.txt", cut.Markup);
            Assert.DoesNotContain("clean.txt", cut.Markup);
        });
    }

    [Fact]
    public void Dirty_files_only_filter_reveals_every_file_under_a_wholesale_untracked_directory()
    {
        // git status --porcelain reports an entirely untracked directory as one "?? dir/" line, not
        // one line per file inside it - RepositoryManager.ListDirtyFilePathsAsync passes that
        // wholesale entry straight through, so every file/folder nested under it must still be
        // treated as dirty-relevant, not just the directory's own top-level entry.
        Directory.CreateDirectory(Path.Combine(_repositoryRoot, "untracked", "nested"));
        File.WriteAllText(Path.Combine(_repositoryRoot, "untracked", "shallow.txt"), "new");
        File.WriteAllText(Path.Combine(_repositoryRoot, "untracked", "nested", "deep.txt"), "new");
        _repositoryManager.DirtyPaths = ["untracked/"];

        var cut = Render<FileTree>(parameters => parameters.Add(p => p.RepositoryName, "repo"));
        cut.WaitForAssertion(() => Assert.Contains("untracked", cut.Markup));

        cut.Find("input[type=checkbox]").Change(true);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("shallow.txt", cut.Markup);
            Assert.Contains("nested", cut.Markup);
            Assert.Contains("deep.txt", cut.Markup);
        });
    }

    /// <inheritdoc />
    public new void Dispose()
    {
        DeleteWithRetry(_repositoryRoot);
        base.Dispose();
    }

    // The same transient-lock race RecursiveDelete (src/OutOfTheBox.Infrastructure) works around for
    // production repository deletes, hit here often enough in practice (AV/indexer handle release
    // lag on a temp directory dozens of these tests write/rename/delete files under in quick
    // succession) to be worth a small retry rather than an occasional flaky full-suite run. Not
    // reusing RecursiveDelete itself - it's internal to Infrastructure with no InternalsVisibleTo to
    // this project, and pulling in a whole extra project reference for one retry loop isn't worth it
    // for test cleanup.
    private static void DeleteWithRetry(string path)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(200 * attempt);
            }
        }
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

    // Only ListDirtyFilePathsAsync is exercised by these tests (FileTree's "Dirty files only"
    // filter) - every other member throws, the same "not exercised, say so" precedent
    // FakeRepositoryFileBrowser already established, so a future test accidentally depending on one
    // of them fails loudly instead of silently returning a meaningless default.
    private sealed class FakeRepositoryManager : IRepositoryManager
    {
        public IReadOnlyList<string> DirtyPaths { get; set; } = [];

        public Task<IReadOnlyList<string>> ListDirtyFilePathsAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult(DirtyPaths);

        public Task<IReadOnlyList<RepositorySummary>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RepositoryActionResult> CloneAsync(string url, string name, string? branch, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RepositoryActionResult> DeleteAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RepositoryGitActionResult> PullAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RepositoryGitActionResult> PushAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RepositoryGitActionResult> ForcePushAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RepositoryGitActionResult> FetchAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RepositoryGitActionResult> CleanAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RepositoryGitActionResult> RenameAsync(string name, string newName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> GetCloneSourceUrlAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<RepositoryBranch>> ListBranchesAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RepositoryGitActionResult> SwitchBranchAsync(string name, string branch, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListRemoteBranchesAsync(string url, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CommitSummary>> ListCommitsAsync(string name, int skip, int take, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RepositoryGitActionResult> CheckoutCommitAsync(string name, string hash, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CommitDetail?> GetCommitDetailAsync(string name, string hash, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> GetCommitFileDiffAsync(string name, string hash, string relativePath, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
