// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Presentation.Dashboard.CodePreview;

/// <summary>
/// Maps a file name to the CodeMirror MIME/mode identifier its content should be highlighted as -
/// deliberately extension/exact-name based (unlike <see cref="FilePreviewDialog"/>'s own
/// binary/text/image classification, which is content-sniffed) since there is no reliable way to
/// sniff "this text is C#" the way a magic number identifies a PNG; Rider itself makes the same
/// extension-based choice for syntax mode selection. Covers the language modes actually vendored in
/// <c>wwwroot/js/vendor/codemirror.min.js</c> - see that file's own header comment for the full list.
/// </summary>
public static class CodePreviewLanguage
{
    private static readonly Dictionary<string, string> MimeTypesByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        // C-family
        [".cs"] = "text/x-csharp",
        [".csx"] = "text/x-csharp",
        [".java"] = "text/x-java",
        [".c"] = "text/x-csrc",
        [".h"] = "text/x-csrc",
        [".cpp"] = "text/x-c++src",
        [".cc"] = "text/x-c++src",
        [".hpp"] = "text/x-c++src",

        // Data/markup
        [".json"] = "application/json",
        [".jsonc"] = "application/json",
        [".xml"] = "application/xml",
        [".csproj"] = "application/xml",
        [".props"] = "application/xml",
        [".targets"] = "application/xml",
        [".xaml"] = "application/xml",
        [".resx"] = "application/xml",
        [".nuspec"] = "application/xml",
        [".config"] = "application/xml",
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".cshtml"] = "text/html",
        [".razor"] = "text/html",
        [".css"] = "text/css",
        [".scss"] = "text/x-scss",
        [".less"] = "text/x-less",
        [".yml"] = "text/x-yaml",
        [".yaml"] = "text/x-yaml",
        [".toml"] = "text/x-toml",

        // Web scripting
        [".js"] = "text/javascript",
        [".mjs"] = "text/javascript",
        [".cjs"] = "text/javascript",
        [".jsx"] = "text/javascript",
        [".ts"] = "application/typescript",
        [".tsx"] = "application/typescript",
        [".php"] = "application/x-httpd-php",

        // Shell/scripting
        [".sh"] = "application/x-sh",
        [".bash"] = "application/x-sh",
        [".ps1"] = "application/x-powershell",
        [".psm1"] = "application/x-powershell",
        [".psd1"] = "application/x-powershell",
        [".py"] = "text/x-python",
        [".rb"] = "text/x-ruby",

        // Other common languages found in a cloned repository
        [".go"] = "text/x-go",
        [".rs"] = "text/x-rustsrc",
        [".sql"] = "text/x-sql",
        [".diff"] = "text/x-diff",
        [".patch"] = "text/x-diff",
        [".ini"] = "text/x-ini",
        [".cfg"] = "text/x-ini",
        [".conf"] = "text/x-ini",
        [".properties"] = "text/x-properties",

        // .md/.markdown are listed for completeness but unreachable in practice - FilePreviewDialog
        // renders those as formatted HTML via Markdig instead of a code view.
        [".md"] = "text/x-markdown",
        [".markdown"] = "text/x-markdown",
    };

    // Exact-name matches for extensionless files a .NET/git repository commonly contains.
    private static readonly Dictionary<string, string> MimeTypesByFileName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dockerfile"] = "text/x-dockerfile",
        [".gitignore"] = "text/x-ini",
        [".gitattributes"] = "text/x-ini",
        [".env"] = "text/x-ini",
        [".dockerignore"] = "text/x-ini",
        [".editorconfig"] = "text/x-ini",
    };

    /// <summary>
    /// The CodeMirror MIME/mode identifier for <paramref name="fileName"/>, or <see langword="null"/>
    /// if none is recognized - CodeMirror still renders unrecognized text with line numbers and
    /// generic (indentation-based) folding, just without color highlighting.
    /// </summary>
    public static string? GetMimeType(string fileName)
    {
        if (MimeTypesByFileName.TryGetValue(fileName, out var byName))
        {
            return byName;
        }

        var extension = Path.GetExtension(fileName);
        return extension.Length > 0 && MimeTypesByExtension.TryGetValue(extension, out var byExtension) ? byExtension : null;
    }
}
