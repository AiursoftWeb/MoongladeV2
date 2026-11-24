using System.Text.RegularExpressions;

namespace Aiursoft.MoongladeV2.Services;

public static partial class BlogExcerptHelper
{
    [GeneratedRegex(@"!\[[^\]]*]\([^)]+\)")]
    private static partial Regex MarkdownImagesRegex();

    [GeneratedRegex(@"\[(?<text>[^\]]+)]\([^)]+\)")]
    private static partial Regex MarkdownLinksRegex();

    [GeneratedRegex(@"[#>*`~\-_|]")]
    private static partial Regex MarkdownSpecialCharsRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultipleWhitespaceRegex();

    public static string BuildExcerpt(string markdown, int maxLength = 280)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var plainText = MarkdownImagesRegex().Replace(markdown, " ");
        plainText = MarkdownLinksRegex().Replace(plainText, "${text}");
        plainText = MarkdownSpecialCharsRegex().Replace(plainText, " ");
        plainText = MultipleWhitespaceRegex().Replace(plainText, " ").Trim();

        return plainText.Length <= maxLength
            ? plainText
            : $"{plainText[..maxLength]}...";
    }
}
