using Aiursoft.Canon;
using Aiursoft.Dotlang.Shared;
using Aiursoft.GptClient.Services;
using Aiursoft.MoongladeV2.Configuration;
using Aiursoft.Scanner.Abstractions;
using Microsoft.Extensions.Options;

namespace Aiursoft.MoongladeV2.Services;

/// <summary>
/// Translates text using Dotlang's <see cref="OllamaBasedTranslatorEngine"/>.
/// Settings (endpoint, model, token) are read from <see cref="GlobalSettingsService"/>
/// at call time so admin changes take effect immediately without restart.
/// </summary>
public class DocumentTranslationService(
    GlobalSettingsService settingsService,
    MarkdownShredder shredder,
    RetryEngine retryEngine,
    ILogger<OllamaBasedTranslatorEngine> engineLogger,
    ChatClient chatClient) : IScopedDependency
{
    public async Task<string> TranslateAsync(string text, string targetCulture)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var options = Options.Create(new TranslateOptions
        {
            OllamaInstance = await settingsService.GetSettingValueAsync(SettingsMap.OpenAiChatEndpoint),
            OllamaModel    = await settingsService.GetSettingValueAsync(SettingsMap.OpenAiModel),
            OllamaToken    = await settingsService.GetSettingValueAsync(SettingsMap.OpenAiApiToken)
        });

        var engine = new OllamaBasedTranslatorEngine(options, retryEngine, engineLogger, chatClient, shredder);
        return await engine.TranslateAsync(text, targetCulture);
    }
}
