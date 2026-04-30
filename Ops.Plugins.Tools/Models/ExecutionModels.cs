namespace Ops.Plugins.Tools.Models;

public sealed class CommandResult
{
    public int ExitCode { get; init; }
    public TimeSpan Duration { get; init; }
    public bool Cancelled { get; init; }
}

public sealed class DeploymentPreview
{
    public string PluginFilePath { get; init; } = string.Empty;
    public string AssemblyName { get; init; } = string.Empty;
    public string TargetSummary { get; init; } = string.Empty;
    public bool PluginFileExists { get; init; }
}
