namespace BookFinder.Api.Models;

public record LikedBook(string Key, string Title, string Author, List<string> Subjects);

public class Preferences
{
    public List<LikedBook> LikedBooks { get; set; } = [];
    public List<string> Authors { get; set; } = [];
    public List<string> Subjects { get; set; } = [];
    /// <summary>Titles already read/downloaded or dismissed — never recommended again.</summary>
    public List<string> Seen { get; set; } = [];
}

/// <summary>Subset of an Open Library search.json document.</summary>
public class OpenLibraryDoc
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public List<string>? AuthorName { get; set; }
    public List<string>? Subject { get; set; }
    public int? FirstPublishYear { get; set; }
    public double? RatingsAverage { get; set; }
    public int? RatingsCount { get; set; }
    public string? EbookAccess { get; set; }
    public List<string>? Ia { get; set; }
    public int? CoverI { get; set; }
}

public record Recommendation(
    string Key, string Title, string Authors, int? Year, string? CoverUrl,
    bool FreePublicDomain, double Score, List<string> Reasons);

public record FreeCopy(string Source, string Title, string Author, Dictionary<string, string> Links);

public record FindResult(List<FreeCopy> Copies, string? BorrowUrl);

public record DownloadRequest(string Url, string Title, string Format);

public record LibraryItem(string FileName, long SizeBytes, DateTime DownloadedUtc);

public record TitleRequest(string Title);
public record ValueRequest(string Value);
