using System.Text.RegularExpressions;
using Ops.Plugins.Tools.Models;

namespace Ops.Plugins.Tools.Services;

public static partial class AuthProfileParser
{
    public static IReadOnlyList<AuthProfile> Parse(IEnumerable<string> lines)
    {
        return lines
            .Select(ParseLine)
            .Where(profile => profile is not null)
            .Cast<AuthProfile>()
            .ToList();
    }

    private static AuthProfile? ParseLine(string line)
    {
        var match = AuthProfileLineRegex().Match(line);
        if (!match.Success || !int.TryParse(match.Groups["index"].Value, out var index))
        {
            return null;
        }

        var beforeUrl = match.Groups["beforeUrl"].Value.Trim();
        var tokens = beforeUrl.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var kind = tokens.Length > 0 ? tokens[0] : string.Empty;
        var name = tokens.Length > 1 ? string.Join(' ', tokens.Skip(1)) : string.Empty;

        return new AuthProfile
        {
            Index = index,
            IsActive = match.Groups["active"].Value == "*",
            Kind = kind,
            Name = name,
            FriendlyName = match.Groups["friendlyName"].Value.Trim(),
            Url = match.Groups["url"].Value.Trim(),
            User = match.Groups["user"].Value.Trim(),
            Cloud = match.Groups["cloud"].Value.Trim(),
            Type = match.Groups["type"].Value.Trim()
        };
    }

    [GeneratedRegex(@"^\[(?<index>\d+)\]\s+(?<active>\*)?\s*(?<beforeUrl>.*?)\s{2,}(?<friendlyName>.*?)\s{2,}(?<url>https?://\S+)\s+(?<user>\S+)\s+(?<cloud>\S+)\s+(?<type>\S+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex AuthProfileLineRegex();
}
