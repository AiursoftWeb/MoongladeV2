using System.Text;
using Aiursoft.MoongladeV2.Configuration;
using Aiursoft.Scanner.Abstractions;
using Newtonsoft.Json;

namespace Aiursoft.MoongladeV2.Services;

/// <summary>
/// Translates Markdown text into a target BCP-47 culture using an OpenAI-compatible
/// chat completions endpoint (Ollama, OpenAI, etc.).
/// Settings are read from <see cref="GlobalSettingsService"/> at call time so admin
/// changes take effect immediately without a restart.
/// </summary>
public class DocumentTranslationService(
    GlobalSettingsService settingsService,
    IHttpClientFactory httpClientFactory,
    ILogger<DocumentTranslationService> logger) : IScopedDependency
{
    private const int TranslateTimeoutSeconds = 180;

    private static readonly string SystemPrompt =
        "You are a professional translator. " +
        "Translate the provided Markdown document into the requested language. " +
        "Preserve ALL Markdown syntax (headings, bold, italic, code blocks, links, lists) exactly — only translate the human-readable text. " +
        "Return ONLY the translated Markdown, no explanations or extra text.";

    public async Task<string> TranslateAsync(string text, string targetCulture)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var endpoint = await settingsService.GetSettingValueAsync(SettingsMap.OpenAiChatEndpoint);
        var model    = await settingsService.GetSettingValueAsync(SettingsMap.OpenAiModel);
        var token    = await settingsService.GetSettingValueAsync(SettingsMap.OpenAiApiToken);

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model))
        {
            logger.LogWarning("DocumentTranslationService: OpenAiChatEndpoint or OpenAiModel is not configured.");
            return text;
        }

        var requestBody = new
        {
            model,
            stream = false,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new
                {
                    role = "user",
                    content = $"Translate the following Markdown into {targetCulture}:\n\n{text}"
                }
            }
        };

        var http    = httpClientFactory.CreateClient();
        var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };

        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TranslateTimeoutSeconds));
        var response = await http.SendAsync(request, cts.Token);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ChatResponse>(cts.Token);
        var translated = result?.Choices?.FirstOrDefault()?.Message?.Content;

        if (string.IsNullOrWhiteSpace(translated))
        {
            logger.LogWarning("DocumentTranslationService: Empty response from model '{Model}'.", model);
            return text;
        }

        return translated.Trim();
    }

    // ── Response DTOs ──────────────────────────────────────────────────────────

    private class ChatResponse
    {
        [JsonProperty("choices")]
        public List<Choice>? Choices { get; set; }
    }

    private class Choice
    {
        [JsonProperty("message")]
        public ChatMessage? Message { get; set; }
    }

    private class ChatMessage
    {
        [JsonProperty("content")]
        public string? Content { get; set; }
    }
}
