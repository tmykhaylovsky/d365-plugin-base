namespace Ops.Plugins.Tools.Models;

public sealed class RepositoryOption
{
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;

    public override string ToString() => Name;
}
