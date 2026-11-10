using System.Text.RegularExpressions;
using BookFinder.Api.Models;

namespace BookFinder.Api.Services;

public partial class RecommendationService(OpenLibraryClient openLibrary, PreferencesStore prefs)
{
    // Subjects so generic they say nothing about taste.
    private static readonly HashSet<string> Noise =
    [
        "fiction", "general", "accessible book", "protected daisy", "in library", "lending library",
        "english fiction", "large type books", "open library staff picks", "fiction general",
        "literature", "novel", "american fiction", "translations into english", "nyt combinedprintandebookfiction",
    ];

    public async Task<List<Recommendation>> RecommendAsync(int count, CancellationToken ct)
    {
        var p = await prefs.GetAsync();
        var profile = BuildProfile(p);
        if (profile.Count == 0 && p.Authors.Count == 0) return [];

        var candidates = new List<(OpenLibraryDoc Doc, string Reason)>();
        var topSubjects = profile.OrderByDescending(kv => kv.Value).Take(6).Select(kv => kv.Key);

        var tasks = topSubjects
            .Select(async s => (await openLibrary.BySubjectAsync(s, 40, ct)).Select(d => (d, $"subject: {s}")))
            .Concat(p.Authors.Select(async a => (await openLibrary.ByAuthorAsync(a, 15, ct)).Select(d => (d, $"author: {a}"))));
        foreach (var batch in await Task.WhenAll(tasks)) candidates.AddRange(batch);

        return Rank(p, profile, candidates, count);
    }

    /// <summary>Weighted subject profile: explicit subjects ×5, each liked book's subjects ×1.</summary>
    public static Dictionary<string, double> BuildProfile(Preferences p)
    {
        var w = new Dictionary<string, double>();
        void Add(string s, double v) { if (s.Length > 0) w[s] = w.GetValueOrDefault(s) + v; }
        foreach (var s in p.Subjects) Add(Norm(s), 5);
        foreach (var b in p.LikedBooks)
            foreach (var s in b.Subjects.Take(25).Select(Norm))
                if (!Noise.Contains(s) && s.Length < 40) Add(s, 1);
        return w;
    }

    /// <summary>Pure scoring step — no I/O, so it is unit-testable.</summary>
    public static List<Recommendation> Rank(
        Preferences p, Dictionary<string, double> profile,
        IEnumerable<(OpenLibraryDoc Doc, string Reason)> candidates, int count)
    {
        var exclude = p.LikedBooks.Select(b => Norm(b.Title)).Concat(p.Seen.Select(Norm)).ToHashSet();
        var favAuthors = p.Authors.Select(Norm).ToHashSet();

        var merged = new Dictionary<string, (OpenLibraryDoc Doc, HashSet<string> Why)>();
        foreach (var (doc, why) in candidates)
        {
            if (string.IsNullOrWhiteSpace(doc.Title) || exclude.Contains(Norm(doc.Title))) continue;
            if (!merged.TryGetValue(doc.Key, out var entry)) merged[doc.Key] = entry = (doc, []);
            entry.Why.Add(why);
        }

        var best = new Dictionary<string, Recommendation>(); // de-dupe near-identical works by title
        foreach (var (doc, why) in merged.Values)
        {
            var subjects = (doc.Subject ?? []).Select(Norm).ToHashSet();
            double score = subjects.Sum(s => profile.GetValueOrDefault(s));
            if ((doc.AuthorName ?? []).Any(a => favAuthors.Contains(Norm(a)))) score += 8;
            score += (doc.RatingsAverage ?? 0) * Math.Min(doc.RatingsCount ?? 0, 200) / 100.0; // capped quality prior
            score += 2 * why.Count;                     // matched several interests
            var free = doc.EbookAccess == "public";
            if (free) score += 1;                       // small nudge toward free-to-read

            var rec = new Recommendation(doc.Key, doc.Title, string.Join(", ", (doc.AuthorName ?? []).Take(2)),
                doc.FirstPublishYear, OpenLibraryClient.CoverUrl(doc), free, Math.Round(score, 2),
                why.Order().ToList());
            var k = Norm(doc.Title);
            if (!best.TryGetValue(k, out var cur) || cur.Score < rec.Score) best[k] = rec;
        }
        return best.Values.OrderByDescending(r => r.Score).Take(count).ToList();
    }

    public static string Norm(string s) => NonAlnum().Replace(s.ToLowerInvariant(), "").Trim();

    [GeneratedRegex("[^a-z0-9 ]")]
    private static partial Regex NonAlnum();
}
