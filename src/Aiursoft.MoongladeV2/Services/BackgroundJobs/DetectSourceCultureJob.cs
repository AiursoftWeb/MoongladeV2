using System.Globalization;
using Aiursoft.Canon;
using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.GptClient.Abstractions;
using Aiursoft.GptClient.Services;
using Aiursoft.MoongladeV2.Configuration;
using Aiursoft.MoongladeV2.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Services.BackgroundJobs;

/// <summary>
/// Detects the original (source) language of each blog post from its content
/// using the configured AI Chat Endpoint. Only processes documents whose
/// <see cref="MarkdownDocument.SourceCulture"/> is null. Runs every 10 minutes
/// so new documents are classified quickly.
///
/// Downstream jobs (localization, abstract generation, embeddings) skip
/// documents with null SourceCulture — this job must run first to unlock them.
///
/// Behavior per document state:
/// - SourceCulture is null  → Detect: AI analyzes content and sets SourceCulture
/// - SourceCulture is set   → Skip (already classified)
/// </summary>
public class DetectSourceCultureJob(
    TemplateDbContext db,
    GlobalSettingsService settingsService,
    RetryEngine retryEngine,
    ChatClient chatClient,
    ILogger<DetectSourceCultureJob> logger) : IBackgroundJob
{
    private const int ContentSampleLength = 500;

    private static readonly HashSet<string> ValidCultures = CultureInfo
        .GetCultures(CultureTypes.AllCultures)
        .Select(c => c.Name)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public string Name => "Detect Source Culture";

    public string Description =>
        "Detects the original (source) language of each blog post from its content using AI. " +
        "Only processes documents whose SourceCulture is null. " +
        "Runs every 10 minutes to ensure new documents are quickly classified. " +
        "Downstream jobs (localization, abstract generation) skip documents with null SourceCulture — " +
        "this job must run first to unlock them.";

    public async Task ExecuteAsync()
    {
        if (!await settingsService.IsAiLocalizationEnabledAsync())
        {
            logger.LogInformation("DetectSourceCultureJob: AI endpoint not configured. Skipping.");
            return;
        }

        var lastId = Guid.Empty;
        var total = 0;

        while (true)
        {
            var currentLastId = lastId;

            var pending = await db.MarkdownDocuments
                .Where(d => d.SourceCulture == null &&
                            d.Id.CompareTo(currentLastId) > 0)
                .OrderBy(d => d.Id)
                .Take(20)
                .ToListAsync();

            if (pending.Count == 0) break;

            foreach (var doc in pending)
            {
                var detected = await DetectCultureAsync(doc);
                if (detected != null)
                {
                    doc.SourceCulture = detected;
                    total++;
                    await db.SaveChangesAsync();
                }
            }

            lastId = pending.Max(d => d.Id);
        }

        logger.LogInformation("DetectSourceCultureJob: done. Detected {Count} language(s).", total);
    }

    private async Task<string?> DetectCultureAsync(MarkdownDocument doc)
    {
        try
        {
            var text = (doc.Content ?? doc.Title ?? string.Empty);
            if (text.Length > ContentSampleLength)
                text = text[..ContentSampleLength];

            if (string.IsNullOrWhiteSpace(text)) return null;

            var endpoint = await settingsService.GetSettingValueAsync(SettingsMap.OpenAiChatEndpoint);
            var model    = await settingsService.GetSettingValueAsync(SettingsMap.OpenAiModel);
            var token    = await settingsService.GetSettingValueAsync(SettingsMap.OpenAiApiToken);

            var result = await retryEngine.RunWithRetry(async _ =>
            {
                var response = await chatClient.AskModel(new OpenAiRequestModel
                {
                    Model  = model,
                    Stream = false,
                    Messages =
                    [
                        new MessagesItem
                        {
                            Role    = "user",
                            Content = $"Detect the language of this text. Reply with ONLY a BCP-47 code like \"en-US\", \"zh-CN\", \"ja-JP\", etc. No other text.\n\n{text}"
                        }
                    ]
                }, endpoint, token, CancellationToken.None);

                return response.GetAnswerPart().Trim();
            }, attempts: 3);

            if (string.IsNullOrWhiteSpace(result)) return null;

            // Normalize: strip quotes, dots, extra whitespace
            result = result.Trim('"', '.', ' ', '\n', '\r');

            if (ValidCultures.Contains(result))
            {
                logger.LogInformation(
                    "DetectSourceCultureJob: '{Title}' → {Culture}.", doc.Title, result);
                return result;
            }

            logger.LogWarning(
                "DetectSourceCultureJob: AI returned invalid culture '{Result}' for '{Title}'.",
                result, doc.Title);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "DetectSourceCultureJob: failed to detect culture for '{Title}'.", doc.Title);
            return null;
        }
    }
}
