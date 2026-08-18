// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace OutOfTheBox.UnitTests.Presentation.Dashboard;

/// <summary>
/// Minimal <see cref="IWebHostEnvironment"/> double for bUnit tests that render
/// <see cref="OutOfTheBox.Presentation.Dashboard.Icon"/>, which reads its vendored SVGs from
/// <see cref="IWebHostEnvironment.WebRootFileProvider"/> at the same <c>_content/OutOfTheBox.Presentation/...</c>
/// path a real host serves them at. That routing prefix only exists once a real ASP.NET Core host
/// composes the Presentation RCL's static web assets into its own web root - it has no meaning on
/// disk - so this strips it and serves straight from the Presentation project's own <c>wwwroot</c>,
/// found by walking up from the test assembly to the repo root.
/// </summary>
internal sealed class TestWebHostEnvironment : IWebHostEnvironment
{
    private const string ContentPrefix = "_content/OutOfTheBox.Presentation/";

    public TestWebHostEnvironment()
    {
        WebRootPath = FindPresentationWwwroot();
        WebRootFileProvider = new PrefixStrippingFileProvider(ContentPrefix, new PhysicalFileProvider(WebRootPath));
    }

    public string ApplicationName { get; set; } = "OutOfTheBox.UnitTests";

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public string EnvironmentName { get; set; } = "Development";

    public string WebRootPath { get; set; }

    public IFileProvider WebRootFileProvider { get; set; }

    private static string FindPresentationWwwroot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OutOfTheBox.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException($"Could not locate repo root (OutOfTheBox.slnx) by walking up from {AppContext.BaseDirectory}.");
        }

        return Path.Combine(dir.FullName, "src", "OutOfTheBox.Presentation", "wwwroot");
    }

    private sealed class PrefixStrippingFileProvider(string prefix, IFileProvider inner) : IFileProvider
    {
        public IFileInfo GetFileInfo(string subpath) =>
            subpath.StartsWith(prefix, StringComparison.Ordinal)
                ? inner.GetFileInfo(subpath[prefix.Length..])
                : new NotFoundFileInfo(subpath);

        public IDirectoryContents GetDirectoryContents(string subpath) =>
            subpath.StartsWith(prefix, StringComparison.Ordinal)
                ? inner.GetDirectoryContents(subpath[prefix.Length..])
                : NotFoundDirectoryContents.Singleton;

        public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
    }
}
