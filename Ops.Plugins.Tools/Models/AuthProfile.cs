namespace Ops.Plugins.Tools.Models;

public sealed class AuthProfile
{
    public int Index { get; init; }
    public bool IsActive { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string FriendlyName { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string User { get; init; } = string.Empty;
    public string Cloud { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;

    public string DisplayName
    {
        get
        {
            var label = FirstNonEmpty(FriendlyName, Name, Url, $"Profile {Index}");
            return IsActive ? $"{label} (active)" : label;
        }
    }

    public string Summary => string.Join(" | ", new[] { Url, User }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }
}
