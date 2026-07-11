using System.Text.RegularExpressions;
using Aiursoft.Canon;
using Aiursoft.Dotlang.Shared;
using Aiursoft.GptClient.Services;
using Aiursoft.MoongladeV2.Configuration;
using Aiursoft.Scanner.Abstractions;
using Microsoft.Extensions.Options;

namespace Aiursoft.MoongladeV2.Services;

public partial class DocumentTranslationService(
    GlobalSettingsService settingsService,
    MarkdownShredder shredder,
    RetryEngine retryEngine,
    ILogger<OllamaBasedTranslatorEngine> engineLogger,
    ChatClient chatClient,
    ILogger<DocumentTranslationService> logger) : IScopedDependency
{
    public virtual async Task<string> TranslateAsync(string text, string targetLanguage)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var endpoint = await settingsService.GetSettingValueAsync(SettingsMap.OpenAiChatEndpoint);
        var model    = await settingsService.GetSettingValueAsync(SettingsMap.OpenAiModel);
        var token    = await settingsService.GetSettingValueAsync(SettingsMap.OpenAiApiToken);

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model))
        {
            logger.LogWarning("DocumentTranslationService: endpoint or model not configured, returning original text.");
            return text;
        }

        var options = Options.Create(new TranslateOptions
        {
            OllamaInstance = endpoint,
            OllamaModel    = model,
            OllamaToken    = token
        });

        var engine = new OllamaBasedTranslatorEngine(options, retryEngine, engineLogger, chatClient, shredder);
        var result = await engine.TranslateAsync(text, targetLanguage);
        return CleanThinkingTraces(result);
    }

    [GeneratedRegex(@"```[\s\S]*?```")]
    private static partial Regex CodeBlockRegex();

    private static string CleanThinkingTraces(string result)
    {
        if (string.IsNullOrWhiteSpace(result))
            return result;

        var thinkEnd = result.LastIndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        if (thinkEnd >= 0)
        {
            result = result[(thinkEnd + 8)..].Trim();
        }

        var matches = CodeBlockRegex().Matches(result);
        if (matches.Count > 0)
        {
            var last = matches[^1].Value;
            var inner = last[3..^3].Trim();
            var newlineIdx = inner.IndexOf('\n');
            if (newlineIdx > 0 && newlineIdx < 20 && inner[..newlineIdx].Trim().All(c => char.IsLetterOrDigit(c) || c is '-' or '_'))
            {
                inner = inner[(newlineIdx + 1)..].Trim();
            }
            if (!string.IsNullOrWhiteSpace(inner))
                return inner;
        }

        return result.Trim();
    }
}
