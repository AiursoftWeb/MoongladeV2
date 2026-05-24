using Aiursoft.Canon;
using Aiursoft.GptClient.Abstractions;
using Aiursoft.GptClient.Services;
using Aiursoft.MoongladeV2.Configuration;
using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.MoongladeV2.Services;

/// <summary>
/// Generates a plain-text abstract for a Markdown document in a specific language
/// using an OpenAI-compatible chat completions endpoint.
/// </summary>
public class AbstractGenerationService(
    GlobalSettingsService settingsService,
    RetryEngine retryEngine,
    ChatClient chatClient,
    ILogger<AbstractGenerationService> logger) : IScopedDependency
{
    private const int MaxAbstractLength = 500;

    private static string BuildPrompt(string content, string language) =>
        $"""
         I just finished writing a blog post and need a summary for it in {language}.

         The summary must:
         - Be written in {language}
         - Be concise, around 300-500 characters
         - Capture the main points and key takeaways
         - NOT use Markdown — output plain text only
         - NOT break into multiple paragraphs — keep it as ONE paragraph
         - Output ONLY the summary text — no explanations, no extra text

         Here is the article:

         =====================
         {content}
         =====================
         """;

    public virtual async Task<string> GenerateAbstractAsync(string content, string language)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        var endpoint = await settingsService.GetSettingValueAsync(SettingsMap.OpenAiChatEndpoint);
        var model    = await settingsService.GetSettingValueAsync(SettingsMap.OpenAiModel);
        var token    = await settingsService.GetSettingValueAsync(SettingsMap.OpenAiApiToken);

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model))
        {
            logger.LogWarning("AbstractGenerationService: endpoint or model not configured.");
            return string.Empty;
        }

        // Truncate content to stay within token limits
        var truncatedContent = content.Length > 8000 ? content[^8000..] : content;

        var message = BuildPrompt(truncatedContent, language);
        var request = new OpenAiRequestModel
        {
            Model  = model,
            Stream = false,
            Messages =
            [
                new MessagesItem { Role = "user", Content = message }
            ]
        };

        logger.LogInformation("AbstractGenerationService: generating abstract to {Language}.", language);

        var abstractText = await retryEngine.RunWithRetry(async _ =>
        {
            var result = await chatClient.AskModel(request, endpoint, token, CancellationToken.None);
            var answer = result.GetAnswerPart().Trim();
            if (string.IsNullOrWhiteSpace(answer))
            {
                throw new InvalidOperationException("LLM returned empty abstract.");
            }
            return answer;
        }, attempts: 3);

        // Trim to max length
        if (abstractText.Length > MaxAbstractLength)
            abstractText = abstractText[..MaxAbstractLength] + "...";

        return abstractText;
    }
}
