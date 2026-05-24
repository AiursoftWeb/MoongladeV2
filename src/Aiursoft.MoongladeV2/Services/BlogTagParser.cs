namespace Aiursoft.MoongladeV2.Services;

public static class BlogTagParser
{
    private static readonly char[] Separators = [',', ';', '|', '，', '、'];

    public static IReadOnlyList<string> ParseTags(string? rawTags)
    {
        if (string.IsNullOrWhiteSpace(rawTags))
        {
            return [];
        }

        return rawTags
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool ContainsTag(string? rawTags, string expectedTag)
    {
        if (string.IsNullOrWhiteSpace(expectedTag))
        {
            return false;
        }

        return ParseTags(rawTags)
            .Any(tag => string.Equals(tag, expectedTag.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
