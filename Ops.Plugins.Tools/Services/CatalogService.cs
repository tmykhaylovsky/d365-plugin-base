using System.Text.Json;
using Ops.Plugins.Tools.Models;

namespace Ops.Plugins.Tools.Services;

public sealed class CatalogService
{
    private static readonly string[] CategoryOrder = ["Setup", "Model", "Deployment", "Registration", "Maintenance"];
    private readonly string _repoRoot;

    public CatalogService(string repoRoot) => _repoRoot = repoRoot;

    public IReadOnlyList<CatalogAction> LoadActions()
    {
        var path = Path.Combine(_repoRoot, "Scripts", "script-catalog.json");
        if (!File.Exists(path))
        {
            return [];
        }

        var catalog = JsonSerializer.Deserialize<ScriptCatalog>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) ?? throw new InvalidOperationException("Script catalog is empty.");

        Validate(catalog);

        return catalog.Scripts
            .Where(IsAvailable)
            .OrderBy(a => Array.IndexOf(CategoryOrder, a.Category) < 0 ? int.MaxValue : Array.IndexOf(CategoryOrder, a.Category))
            .ThenBy(a => a.DisplayOrder)
            .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void Validate(ScriptCatalog catalog)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in catalog.Scripts)
        {
            Require(action.Id, "id");
            Require(action.ActionKind, $"{action.Id}.actionKind");
            Require(action.Title, $"{action.Id}.title");
            Require(action.Category, $"{action.Id}.category");
            Require(action.DangerLevel, $"{action.Id}.dangerLevel");

            if (!ids.Add(action.Id))
            {
                throw new InvalidOperationException($"Duplicate catalog action id: {action.Id}");
            }

            if (action.ActionKind.Equals("script", StringComparison.OrdinalIgnoreCase))
            {
                ValidateScriptPath(action.Script, action.Id);
            }
            else if (!action.ActionKind.Equals("builtin", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unsupported actionKind for {action.Id}: {action.ActionKind}");
            }

            foreach (var parameter in action.Parameters)
            {
                Require(parameter.Name, $"{action.Id}.parameters.name");
                Require(parameter.Type, $"{action.Id}.{parameter.Name}.type");
            }
        }
    }

    private bool IsAvailable(CatalogAction action)
    {
        if (!action.ActionKind.Equals("script", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var scriptPath = ResolveScriptPath(action.Script!);
        return File.Exists(scriptPath);
    }

    private void ValidateScriptPath(string? script, string id)
    {
        Require(script, $"{id}.script");
        if (Path.IsPathRooted(script!) || script!.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Catalog action {id} uses an unsafe script path: {script}");
        }

        var fullPath = ResolveScriptPath(script);
        var scriptsRoot = Path.GetFullPath(Path.Combine(_repoRoot, "Scripts")) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(scriptsRoot, StringComparison.OrdinalIgnoreCase) || !fullPath.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Catalog action {id} must point to a .ps1 file under Scripts/: {script}");
        }
    }

    public string ResolveScriptPath(string script) => Path.GetFullPath(Path.Combine(_repoRoot, script.Replace('/', Path.DirectorySeparatorChar)));

    private static void Require(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required catalog field: {field}");
        }
    }
}
