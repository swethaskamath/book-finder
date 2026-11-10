using System.Net.Http.Json;
using BookFinder.Api.Models;

namespace BookFinder.Api.Services;

/// <summary>
/// Finds legally free copies only: Project Gutenberg (via Gutendex) and public-domain
/// Internet Archive scans. In-copyright books get an Open Library borrow link instead.
/// </summary>
public class FreeSourceService(IHttpClientFactory factory, OpenLibraryClient openLibrary, ILogger<FreeSourceService> log)
{
    /// <summary>Hosts the server is allowed to download from (prevents using the API as an open proxy).</summary>
    public static bool IsAllowedDownloadUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps &&
        (u.Host is "gutenberg.org" or "www.gutenberg.org" or "archive.org" || u.Host.EndsWith(".archive.org"));

    public async Task<FindResult> FindAsync(string title, string? author, CancellationToken ct)
    {
        var copies = new List<FreeCopy>();
        foreach (var source in new Func<Task<List<FreeCopy>>>[] { () => Gutenberg(title, author, ct), () => InternetArchive(title, author, ct) })
        {
            try { copies.AddRange(await source()); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                log.LogWarning(e, "Free source lookup failed for {Title}", title);
            }
        }

        string? borrow = null;
        if (copies.Count == 0)
        {
            try
            {
                var doc = (await openLibrary.SearchAsync(q: title, limit: 1, ct: ct)).FirstOrDefault();
                if (doc?.EbookAccess is "borrowable" or "printdisabled")
                    borrow = $"https://openlibrary.org{doc.Key}";
            }
            catch (HttpRequestException e) { log.LogWarning(e, "Borrow lookup failed for {Title}", title); }
        }
        return new FindResult(copies, borrow);
    }

    private record GutendexResponse(List<GutendexBook>? Results);
    private record GutendexBook(string Title, List<GutendexAuthor>? Authors, Dictionary<string, string>? Formats);
    private record GutendexAuthor(string Name);

    private async Task<List<FreeCopy>> Gutenberg(string title, string? author, CancellationToken ct)
    {
        var q = Uri.EscapeDataString(author is null ? title : $"{title} {author}");
        var res = await factory.CreateClient("gutendex")
            .GetFromJsonAsync<GutendexResponse>($"books/?search={q}", OpenLibraryClient.SnakeCase, ct);
        var output = new List<FreeCopy>();
        foreach (var b in (res?.Results ?? []).Take(5))
        {
            var links = new Dictionary<string, string>();
            foreach (var (mime, url) in b.Formats ?? [])
            {
                if (mime.StartsWith("application/epub")) links.TryAdd("epub", url);
                else if (mime.StartsWith("text/plain")) links.TryAdd("txt", url);
                else if (mime == "application/pdf") links.TryAdd("pdf", url);
            }
            if (links.Count > 0)
                output.Add(new("Project Gutenberg", b.Title, string.Join(", ", (b.Authors ?? []).Select(a => a.Name)), links));
        }
        return output;
    }

    private record IaMetadata(List<IaFile>? Files);
    private record IaFile(string Name);

    private async Task<List<FreeCopy>> InternetArchive(string title, string? author, CancellationToken ct)
    {
        var output = new List<FreeCopy>();
        var ia = factory.CreateClient("archive");
        foreach (var doc in await openLibrary.SearchAsync(q: title, author: author, limit: 5, ct: ct))
        {
            if (doc.EbookAccess != "public") continue;
            foreach (var id in (doc.Ia ?? []).Take(3))
            {
                var meta = await ia.GetFromJsonAsync<IaMetadata>($"metadata/{Uri.EscapeDataString(id)}", ct);
                var links = new Dictionary<string, string>();
                foreach (var f in meta?.Files ?? [])
                {
                    var url = $"https://archive.org/download/{id}/{Uri.EscapeDataString(f.Name)}";
                    if (f.Name.EndsWith(".epub")) links.TryAdd("epub", url);
                    else if (f.Name.EndsWith(".pdf") && !f.Name.Contains("_encrypted")) links.TryAdd("pdf", url);
                    else if (f.Name.EndsWith("_djvu.txt")) links.TryAdd("txt", url);
                }
                if (links.Count == 0) continue;
                output.Add(new("Internet Archive (public domain)", doc.Title, string.Join(", ", doc.AuthorName ?? []), links));
                break;
            }
        }
        return output;
    }
}
