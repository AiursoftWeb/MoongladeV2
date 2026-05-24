using Aiursoft.Canon;
using Aiursoft.Dotlang.Shared;
using Aiursoft.GptClient.Services;
using Aiursoft.MoongladeV2.Configuration;
using Aiursoft.Scanner.Abstractions;
using Microsoft.Extensions.Options;

namespace Aiursoft.MoongladeV2.Services;

/// <summary>
/// Translates Markdown text using Dotlang's <see cref="OllamaBasedTranslatorEngine"/>,
/// which automatically shreds code blocks, retries on failure, and preserves Markdown syntax.
/// Settings are read from <see cref="GlobalSettingsService"/> at call time so admin
/// changes take effect immediately.
/// </summary>
public class DocumentTranslationService(
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
        return await engine.TranslateAsync(text, targetLanguage);
    }
}
