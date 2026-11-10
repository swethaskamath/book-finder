using System.Text.Json;
using BookFinder.Api.Models;

namespace BookFinder.Api.Services;

/// <summary>Single-user preferences persisted as a JSON file.</summary>
public class PreferencesStore(IConfiguration config)
{
    private readonly string _path = Path.Combine(config.DataDir(), "preferences.json");
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<Preferences> GetAsync()
    {
        await _lock.WaitAsync();
        try { return await ReadAsync(); }
        finally { _lock.Release(); }
    }

    public async Task<Preferences> UpdateAsync(Action<Preferences> change)
    {
        await _lock.WaitAsync();
        try
        {
            var p = await ReadAsync();
            change(p);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(p, Json));
            return p;
        }
        finally { _lock.Release(); }
    }

    private async Task<Preferences> ReadAsync() =>
        File.Exists(_path)
            ? JsonSerializer.Deserialize<Preferences>(await File.ReadAllTextAsync(_path), Json) ?? new()
            : new();
}

public static class ConfigExtensions
{
    public static string DataDir(this IConfiguration c) =>
        c["BookFinder:DataDir"] ?? Path.Combine(AppContext.BaseDirectory, "data");

    public static string LibraryDir(this IConfiguration c) =>
        c["BookFinder:LibraryDir"] ?? Path.Combine(c.DataDir(), "library");
}
