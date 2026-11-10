using System.Net.Http.Json;
using System.Text.Json;
using BookFinder.Api.Models;

namespace BookFinder.Api.Services;

public class OpenLibraryClient(HttpClient http)
{
    public static readonly JsonSerializerOptions SnakeCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private const string Fields =
        "key,title,author_name,subject,first_publish_year,ratings_average,ratings_count,ebook_access,ia,cover_i";

    private record SearchResponse(List<OpenLibraryDoc>? Docs);

    public async Task<List<OpenLibraryDoc>> SearchAsync(
        string? q = null, string? author = null, string? sort = null, int limit = 20, CancellationToken ct = default)
    {
        var qs = new List<string> { $"limit={limit}", $"fields={Fields}" };
        if (q is not null) qs.Add("q=" + Uri.EscapeDataString(q));
        if (author is not null) qs.Add("author=" + Uri.EscapeDataString(author));
        if (sort is not null) qs.Add("sort=" + sort);
        var res = await http.GetFromJsonAsync<SearchResponse>("search.json?" + string.Join('&', qs), SnakeCase, ct);
        return res?.Docs ?? [];
    }

    public Task<List<OpenLibraryDoc>> BySubjectAsync(string subject, int limit, CancellationToken ct) =>
        SearchAsync(q: $"subject:\"{subject}\"", sort: "rating", limit: limit, ct: ct);

    public Task<List<OpenLibraryDoc>> ByAuthorAsync(string author, int limit, CancellationToken ct) =>
        SearchAsync(author: author, sort: "rating", limit: limit, ct: ct);

    public static string? CoverUrl(OpenLibraryDoc d) =>
        d.CoverI is int id ? $"https://covers.openlibrary.org/b/id/{id}-M.jpg" : null;
}
