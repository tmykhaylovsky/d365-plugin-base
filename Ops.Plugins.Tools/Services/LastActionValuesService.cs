using System.Text.Json;

namespace Ops.Plugins.Tools.Services;

public sealed class LastActionValuesService
{
    private readonly string _path;

    public LastActionValuesService(string repoRoot) => _path = Path.Combine(repoRoot, ".local", "tool-last-values.json");

    public Dictionary<string, string> Load(string actionId)
    {
        var allValues = LoadAll();
        return allValues.TryGetValue(actionId, out var values)
            ? new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public void Save(string actionId, Dictionary<string, string> values)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var allValues = LoadAll();
        allValues[actionId] = values;
        File.WriteAllText(_path, JsonSerializer.Serialize(allValues, new JsonSerializerOptions { WriteIndented = true }));
    }

    private Dictionary<string, Dictionary<string, string>> LoadAll()
    {
        if (!File.Exists(_path))
        {
            return new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        }

        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(_path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
    }
}
