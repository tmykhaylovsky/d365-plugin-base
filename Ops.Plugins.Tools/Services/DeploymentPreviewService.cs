using System.Reflection;
using Ops.Plugins.Tools.Models;

namespace Ops.Plugins.Tools.Services;

public sealed class DeploymentPreviewService
{
    private readonly string _repoRoot;

    public DeploymentPreviewService(string repoRoot) => _repoRoot = repoRoot;

    public DeploymentPreview Create(IDictionary<string, string> values)
    {
        var pluginFile = values.TryGetValue("PluginFile", out var configuredFile) && !string.IsNullOrWhiteSpace(configuredFile)
            ? configuredFile
            : values.TryGetValue("Assembly", out var assemblyPath) && !string.IsNullOrWhiteSpace(assemblyPath)
                ? assemblyPath
                : "Ops.Plugins/bin/Debug/net462/Ops.Plugins.dll";

        var fullPath = Path.IsPathRooted(pluginFile) ? pluginFile : Path.Combine(_repoRoot, pluginFile.Replace('/', Path.DirectorySeparatorChar));
        var exists = File.Exists(fullPath);
        var assemblyName = values.TryGetValue("AssemblyName", out var configuredName) && !string.IsNullOrWhiteSpace(configuredName)
            ? configuredName
            : TryReadAssemblyName(fullPath) ?? Path.GetFileNameWithoutExtension(fullPath);

        var target = values.TryGetValue("PluginAssemblyId", out var id) && !string.IsNullOrWhiteSpace(id)
            ? id
            : "resolved by assembly name during run";

        return new DeploymentPreview
        {
            PluginFilePath = fullPath,
            PluginFileExists = exists,
            AssemblyName = assemblyName,
            TargetSummary = target
        };
    }

    private static string? TryReadAssemblyName(string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            return null;
        }

        try
        {
            return AssemblyName.GetAssemblyName(fullPath).Name;
        }
        catch
        {
            return null;
        }
    }
}
