using Aiursoft.MoongladeV2.Entities;

namespace Aiursoft.MoongladeV2.Services.BackgroundJobs;

internal static class DocumentPreparationQuery
{
    internal static IQueryable<MarkdownDocument> ForPreparation(this IQueryable<MarkdownDocument> documents)
    {
        return documents.Where(d => d.IsPublic ||
            (!string.IsNullOrWhiteSpace(d.Title) && !string.IsNullOrWhiteSpace(d.Content)));
    }
}
