using System.Net;
using System.Net.Http.Json;
using System.Text;
using BookFinder.Api.Models;
using BookFinder.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace BookFinder.Api.Tests;

public class RankingTests
{
    private static OpenLibraryDoc Doc(string key, string title, string author, string[] subjects,
        double rating = 0, int count = 0, string? access = null) =>
        new() { Key = key, Title = title, AuthorName = [author], Subject = [.. subjects],
                RatingsAverage = rating, RatingsCount = count, EbookAccess = access };

    [Fact]
    public void Profile_weights_explicit_subjects_and_drops_noise()
    {
        var p = new Preferences
        {
            Subjects = ["Time travel"],
            LikedBooks = [new("/w/1", "Dune", "Frank Herbert", ["Fiction", "Science fiction", "Ecology"])],
        };
        var profile = RecommendationService.BuildProfile(p);
        Assert.Equal(5, profile["time travel"]);
        Assert.Equal(1, profile["science fiction"]);
        Assert.False(profile.ContainsKey("fiction"));
    }

    [Fact]
    public void Rank_excludes_liked_and_seen_and_prefers_better_matches()
    {
        var p = new Preferences
        {
            Authors = ["Ursula K. Le Guin"],
            Subjects = ["Time travel"],
            Seen = ["Neuromancer"],
            LikedBooks = [new("/w/1", "Dune", "Frank Herbert", ["Science fiction"])],
        };
        var profile = RecommendationService.BuildProfile(p);
        var cands = new List<(OpenLibraryDoc, string)>
        {
            (Doc("/w/1", "Dune", "Frank Herbert", ["Science fiction"]), "subject: science fiction"),
            (Doc("/w/2", "Neuromancer", "William Gibson", ["Science fiction"]), "subject: science fiction"),
            (Doc("/w/3", "Some Romance", "A. Writer", ["Romance"], 4.5, 1000), "subject: science fiction"),
            (Doc("/w/4", "The Time Machine", "H. G. Wells", ["Science fiction", "Time travel"], 3.9, 150, "public"), "subject: time travel"),
            (Doc("/w/5", "The Left Hand of Darkness", "Ursula K. Le Guin", ["Science fiction"], 4.1, 300), "author: Ursula K. Le Guin"),
            (Doc("/w/6", "The Time Machine", "H. G. Wells", ["Time travel"]), "subject: time travel"), // duplicate edition
        };
        var recs = RecommendationService.Rank(p, profile, cands, 10);
        var titles = recs.Select(r => r.Title).ToList();

        Assert.DoesNotContain("Dune", titles);
        Assert.DoesNotContain("Neuromancer", titles);
        Assert.Single(titles, t => t == "The Time Machine");
        Assert.Equal("The Left Hand of Darkness", titles[0]);
        Assert.Equal("Some Romance", titles[^1]);
        Assert.True(recs.Single(r => r.Title == "The Time Machine").FreePublicDomain);
    }

    [Theory]
    [InlineData("https://www.gutenberg.org/ebooks/84.epub3.images", true)]
    [InlineData("https://archive.org/download/x/x.epub", true)]
    [InlineData("https://ia800.us.archive.org/x.pdf", true)]
    [InlineData("http://www.gutenberg.org/x.epub", false)]
    [InlineData("https://evilarchive.org/x.epub", false)]
    [InlineData("https://z-library.sk/book.epub", false)]
    [InlineData("https://169.254.169.254/latest", false)]
    public void Download_host_allowlist(string url, bool allowed) =>
        Assert.Equal(allowed, FreeSourceService.IsAllowedDownloadUrl(url));

    [Fact]
    public void SafeName_strips_path_characters() =>
        Assert.Equal("etc_passwd", LibraryService.SafeName("../../etc/passwd"));
}

/// <summary>End-to-end through the HTTP API, with all outbound calls answered by a fake handler.</summary>
public class ApiTests : IClassFixture<ApiTests.Factory>
{
    private readonly HttpClient _client;
    public ApiTests(Factory f) => _client = f.CreateClient();

    public class Factory : WebApplicationFactory<Program>
    {
        public readonly string Dir = Directory.CreateTempSubdirectory("bookfinder-test").FullName;
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder b)
        {
            b.UseSetting("BookFinder:DataDir", Dir);
            b.ConfigureServices(s => s.ConfigureAll<HttpClientFactoryOptions>(o =>
                o.HttpMessageHandlerBuilderActions.Add(h => h.PrimaryHandler = new FakeWeb())));
        }
    }

    private class FakeWeb : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var url = Uri.UnescapeDataString(r.RequestUri!.ToString());
            if (url.Contains("subject:\"offline\"")) throw new HttpRequestException("site down");
            string body = url switch
            {
                _ when url.Contains("gutendex") && url.Contains("Frankenstein") =>
                    """{"results":[{"title":"Frankenstein","authors":[{"name":"Shelley, Mary"}],"formats":{"application/epub+zip":"https://www.gutenberg.org/84.epub","text/plain; charset=us-ascii":"https://www.gutenberg.org/84.txt"}}]}""",
                _ when url.Contains("gutendex") => """{"results":[]}""",
                _ when url.Contains("gutenberg.org/84.epub") => "EPUBDATA",
                _ when url.Contains("subject:") || url.Contains("author=") =>
                    """{"docs":[{"key":"/w/5","title":"The Left Hand of Darkness","author_name":["Ursula K. Le Guin"],"subject":["Science fiction"],"ratings_average":4.1,"ratings_count":300,"cover_i":42}]}""",
                _ when url.Contains("q=Modern Bestseller") =>
                    """{"docs":[{"key":"/w/9","title":"Modern Bestseller","ebook_access":"borrowable"}]}""",
                _ => """{"docs":[{"key":"/w/1","title":"Dune","author_name":["Frank Herbert"],"subject":["Science fiction"]}]}""",
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public async Task Full_flow_like_recommend_find_download()
    {
        (await _client.PostAsJsonAsync("/api/preferences/books", new { title = "Dune" })).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync("/api/preferences/authors", new { value = "Ursula K. Le Guin" })).EnsureSuccessStatusCode();

        var prefs = await _client.GetFromJsonAsync<Preferences>("/api/preferences");
        Assert.Equal("Dune", prefs!.LikedBooks.Single().Title);

        var recs = await _client.GetFromJsonAsync<List<Recommendation>>("/api/recommendations");
        Assert.Equal("The Left Hand of Darkness", recs!.Single().Title);
        Assert.Equal("https://covers.openlibrary.org/b/id/42-M.jpg", recs[0].CoverUrl);

        var found = await _client.GetFromJsonAsync<FindResult>("/api/free?title=Frankenstein");
        var copy = found!.Copies.First();
        Assert.Equal("Project Gutenberg", copy.Source);

        var dl = await _client.PostAsJsonAsync("/api/library", new DownloadRequest(copy.Links["epub"], copy.Title, "epub"));
        dl.EnsureSuccessStatusCode();
        Assert.Equal("EPUBDATA", await _client.GetStringAsync("/api/library/Frankenstein.epub"));

        prefs = await _client.GetFromJsonAsync<Preferences>("/api/preferences");
        Assert.Contains("Frankenstein", prefs!.Seen);
    }

    [Fact]
    public async Task Copyrighted_book_returns_borrow_link()
    {
        var found = await _client.GetFromJsonAsync<FindResult>("/api/free?title=Modern%20Bestseller");
        Assert.Empty(found!.Copies);
        Assert.Equal("https://openlibrary.org/w/9", found.BorrowUrl);
    }

    [Fact]
    public async Task Rejects_non_allowlisted_download_and_traversal()
    {
        var bad = await _client.PostAsJsonAsync("/api/library", new DownloadRequest("https://z-library.sk/x.epub", "x", "epub"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var trav = await _client.GetAsync("/api/library/..%2Fpreferences.json");
        Assert.Equal(HttpStatusCode.NotFound, trav.StatusCode);
    }

    [Fact]
    public async Task Upstream_outage_returns_502_with_message()
    {
        var c = new Factory().CreateClient();
        (await c.PostAsJsonAsync("/api/preferences/subjects", new { value = "offline" })).EnsureSuccessStatusCode();
        var res = await c.GetAsync("/api/recommendations");
        Assert.Equal(HttpStatusCode.BadGateway, res.StatusCode);
        Assert.Contains("Couldn't reach", await res.Content.ReadAsStringAsync());
    }
}
