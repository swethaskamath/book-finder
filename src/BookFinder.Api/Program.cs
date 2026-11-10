using BookFinder.Api.Models;
using BookFinder.Api.Services;
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);
const string UserAgent = "BookFinder/1.0 (personal book recommender)";

builder.Services.AddHttpClient<OpenLibraryClient>(c =>
{
    c.BaseAddress = new Uri("https://openlibrary.org/");
    c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    c.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient("gutendex", c =>
{
    c.BaseAddress = new Uri("https://gutendex.com/");
    c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    c.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient("archive", c =>
{
    c.BaseAddress = new Uri("https://archive.org/");
    c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    c.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient("downloads", c =>
{
    c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    c.Timeout = TimeSpan.FromMinutes(5);
});
builder.Services.AddSingleton<PreferencesStore>();
builder.Services.AddScoped<RecommendationService>();
builder.Services.AddScoped<FreeSourceService>();
builder.Services.AddScoped<LibraryService>();
builder.Services.AddProblemDetails();

var app = builder.Build();
// Upstream book sites being down is not our bug: report 502 with a readable message.
app.UseExceptionHandler(e => e.Run(async ctx =>
{
    var ex = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var upstream = ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException;
    ctx.Response.StatusCode = upstream ? 502 : 500;
    await ctx.Response.WriteAsJsonAsync(new
    {
        message = upstream
            ? "Couldn't reach Open Library / Project Gutenberg / Internet Archive. Check your internet connection and try again."
            : "Unexpected server error.",
    });
}));
app.UseDefaultFiles();
app.UseStaticFiles(); // serves the built Angular app from wwwroot in production

var api = app.MapGroup("/api");

// ---- preferences
api.MapGet("/preferences", (PreferencesStore s) => s.GetAsync());

api.MapPost("/preferences/books", async (TitleRequest r, OpenLibraryClient ol, PreferencesStore s, CancellationToken ct) =>
{
    var doc = (await ol.SearchAsync(q: r.Title, limit: 1, ct: ct)).FirstOrDefault();
    if (doc is null) return Results.NotFound(new { message = $"'{r.Title}' not found on Open Library." });
    var book = new LikedBook(doc.Key, doc.Title, string.Join(", ", doc.AuthorName ?? []), (doc.Subject ?? []).Take(40).ToList());
    return Results.Ok(await s.UpdateAsync(p => { p.LikedBooks.RemoveAll(b => b.Key == book.Key); p.LikedBooks.Add(book); }));
});
api.MapDelete("/preferences/books", (string key, PreferencesStore s) =>
    s.UpdateAsync(p => p.LikedBooks.RemoveAll(b => b.Key == key)));

foreach (var (route, pick) in new (string, Func<Preferences, List<string>>)[]
         { ("authors", p => p.Authors), ("subjects", p => p.Subjects), ("seen", p => p.Seen) })
{
    api.MapPost($"/preferences/{route}", (ValueRequest r, PreferencesStore s) => string.IsNullOrWhiteSpace(r.Value)
        ? Task.FromResult<IResult>(Results.BadRequest())
        : s.UpdateAsync(p => { var l = pick(p); if (!l.Contains(r.Value.Trim())) l.Add(r.Value.Trim()); }).ContinueWith(t => Results.Ok(t.Result)));
    api.MapDelete($"/preferences/{route}", (string value, PreferencesStore s) => s.UpdateAsync(p => pick(p).Remove(value)));
}

// ---- recommendations & free copies
api.MapGet("/recommendations", (RecommendationService r, CancellationToken ct, int count = 20) =>
    r.RecommendAsync(Math.Clamp(count, 1, 50), ct));

api.MapGet("/free", (string title, string? author, FreeSourceService f, CancellationToken ct) => f.FindAsync(title, author, ct));

// ---- local library
api.MapGet("/library", (LibraryService l) => l.List());
api.MapPost("/library", async (DownloadRequest r, LibraryService l, CancellationToken ct) =>
{
    try { return Results.Ok(await l.DownloadAsync(r, ct)); }
    catch (ArgumentException e) { return Results.BadRequest(new { message = e.Message }); }
    catch (HttpRequestException e) { return Results.Problem($"Download failed: {e.Message}", statusCode: 502); }
});
api.MapGet("/library/{fileName}", (string fileName, LibraryService l) =>
{
    var path = l.Resolve(fileName);
    if (path is null) return Results.NotFound();
    new FileExtensionContentTypeProvider().TryGetContentType(path, out var type);
    return Results.File(path, type ?? "application/octet-stream", fileName);
});

app.MapFallbackToFile("index.html");
app.Run();

public partial class Program;
