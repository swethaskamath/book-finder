using System.Text.RegularExpressions;
using BookFinder.Api.Models;

namespace BookFinder.Api.Services;

public partial class LibraryService(IConfiguration config, IHttpClientFactory factory, PreferencesStore prefs)
{
    private readonly string _dir = config.LibraryDir();
    private static readonly HashSet<string> Formats = ["epub", "pdf", "txt"];

    public async Task<LibraryItem> DownloadAsync(DownloadRequest req, CancellationToken ct)
    {
        if (!FreeSourceService.IsAllowedDownloadUrl(req.Url))
            throw new ArgumentException("Only Project Gutenberg and Internet Archive downloads are allowed.");
        if (!Formats.Contains(req.Format))
            throw new ArgumentException("Format must be epub, pdf or txt.");

        Directory.CreateDirectory(_dir);
        var name = $"{SafeName(req.Title)}.{req.Format}";
        var path = Path.Combine(_dir, name);

        using var res = await factory.CreateClient("downloads").GetAsync(req.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        await using (var file = File.Create(path))
            await res.Content.CopyToAsync(file, ct);

        await prefs.UpdateAsync(p => { if (!p.Seen.Contains(req.Title)) p.Seen.Add(req.Title); });
        var info = new FileInfo(path);
        return new LibraryItem(name, info.Length, info.LastWriteTimeUtc);
    }

    public List<LibraryItem> List() =>
        !Directory.Exists(_dir) ? [] :
        new DirectoryInfo(_dir).GetFiles()
            .Where(f => Formats.Contains(f.Extension.TrimStart('.')))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new LibraryItem(f.Name, f.Length, f.LastWriteTimeUtc)).ToList();

    /// <summary>Resolves a file name inside the library dir, rejecting path traversal.</summary>
    public string? Resolve(string fileName)
    {
        if (fileName != Path.GetFileName(fileName)) return null;
        var path = Path.Combine(_dir, fileName);
        return File.Exists(path) ? path : null;
    }

    public static string SafeName(string title)
    {
        var s = Unsafe().Replace(title, "_").Trim('_');
        return (s.Length > 80 ? s[..80] : s) is { Length: > 0 } r ? r : "book";
    }

    [GeneratedRegex("[^A-Za-z0-9]+")]
    private static partial Regex Unsafe();
}
