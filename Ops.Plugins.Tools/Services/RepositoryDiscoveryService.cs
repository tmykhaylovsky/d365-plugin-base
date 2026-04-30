using Ops.Plugins.Tools.Models;

namespace Ops.Plugins.Tools.Services;

public sealed class RepositoryDiscoveryService
{
    private static readonly string[] IgnoredDirectories = ["bin", "obj", ".git", ".vs", "packages", "node_modules"];
    private static readonly string[] PluginSignals =
    [
        "Microsoft.CrmSdk.CoreAssemblies",
        "Microsoft.PowerPlatform.Dataverse",
        "Microsoft.Xrm.Sdk",
        "IPlugin",
        "PluginBase",
        "RegisteredEvent"
    ];

    public IReadOnlyList<RepositoryOption> Discover(string currentRepoRoot)
    {
        var reposRoot = Directory.GetParent(currentRepoRoot)?.FullName;
        if (string.IsNullOrWhiteSpace(reposRoot) || !Directory.Exists(reposRoot))
        {
            return [];
        }

        return Directory.EnumerateDirectories(reposRoot)
            .Where(IsLikelyPluginRepository)
            .Select(path => new RepositoryOption
            {
                Name = Path.GetFileName(path),
                Path = path
            })
            .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsLikelyPluginRepository(string path)
    {
        try
        {
            return EnumerateProjectFiles(path).Any(IsLikelyPluginClassLibrary);
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateProjectFiles(string path)
    {
        var pending = new Stack<string>();
        pending.Push(path);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var projectFile in Directory.EnumerateFiles(current, "*.csproj"))
            {
                yield return projectFile;
            }

            foreach (var directory in Directory.EnumerateDirectories(current))
            {
                if (!IgnoredDirectories.Contains(Path.GetFileName(directory), StringComparer.OrdinalIgnoreCase))
                {
                    pending.Push(directory);
                }
            }
        }
    }

    private static bool IsLikelyPluginClassLibrary(string projectFile)
    {
        var projectText = File.ReadAllText(projectFile);
        if (projectText.Contains("<OutputType>Exe</OutputType>", StringComparison.OrdinalIgnoreCase) ||
            projectText.Contains("<OutputType>WinExe</OutputType>", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (PluginSignals.Any(signal => projectText.Contains(signal, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var projectDirectory = Path.GetDirectoryName(projectFile);
        if (projectDirectory is null)
        {
            return false;
        }

        return Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Take(200)
            .Any(file => PluginSignals.Any(signal => File.ReadAllText(file).Contains(signal, StringComparison.OrdinalIgnoreCase)));
    }
}
