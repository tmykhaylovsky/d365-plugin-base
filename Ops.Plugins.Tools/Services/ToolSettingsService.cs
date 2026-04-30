using System.Text.Json;
using Ops.Plugins.Tools.Models;

namespace Ops.Plugins.Tools.Services;

public sealed class ToolSettingsService
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PluginTools",
        "settings.json");

    public ToolSettings Load()
    {
        if (!File.Exists(_path))
        {
            return new ToolSettings();
        }

        return JsonSerializer.Deserialize<ToolSettings>(File.ReadAllText(_path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new ToolSettings();
    }

    public void Save(ToolSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
