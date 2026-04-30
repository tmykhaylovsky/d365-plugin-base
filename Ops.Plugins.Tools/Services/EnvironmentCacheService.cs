using System.Text.Json;
using Ops.Plugins.Tools.Models;

namespace Ops.Plugins.Tools.Services;

public sealed class EnvironmentCacheService
{
    private readonly string _path;

    public EnvironmentCacheService(string repoRoot) => _path = Path.Combine(repoRoot, ".local", "environments.json");

    public IReadOnlyList<EnvironmentEntry> Load()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        var entries = JsonSerializer.Deserialize<List<EnvironmentEntry>>(File.ReadAllText(_path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return entries?
            .Where(e => !string.IsNullOrWhiteSpace(e.Url))
            .OrderByDescending(e => e.LastUsedUtc)
            .ThenBy(e => e.Name)
            .ToList() ?? [];
    }

    public void Save(EnvironmentEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Url))
        {
            throw new InvalidOperationException("Environment URL is required.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var entries = Load().ToList();
        entries.RemoveAll(e => e.Url.Equals(entry.Url, StringComparison.OrdinalIgnoreCase));
        entry.LastUsedUtc = DateTimeOffset.UtcNow;
        entries.Insert(0, entry);
        File.WriteAllText(_path, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
    }
}
