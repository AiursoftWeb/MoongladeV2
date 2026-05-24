using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Entities;

/// <summary>
/// AI-translated content for a <see cref="MarkdownDocument"/> in a specific BCP-47 culture.
/// One row per (DocumentId × Culture) pair.
/// </summary>
[Index(nameof(DocumentId), nameof(Culture), IsUnique = true)]
public class LocalizedDocument
{
    [Key]
    public int Id { get; set; }

    public Guid DocumentId { get; set; }

    [ForeignKey(nameof(DocumentId))]
    public MarkdownDocument Document { get; set; } = null!;

    /// <summary>
    /// BCP-47 culture code, e.g. "en-US", "ja-JP".
    /// </summary>
    [MaxLength(20)]
    public required string Culture { get; set; }

    [MaxLength(200)]
    public string LocalizedTitle { get; set; } = string.Empty;

    [MaxLength(65535)]
    public string LocalizedContent { get; set; } = string.Empty;

    /// <summary>
    /// Set after each successful translation run.
    /// Compared against <see cref="MarkdownDocument.UpdatedAt"/> to decide if retranslation is needed.
    /// </summary>
    public DateTime LastLocalizedAt { get; set; } = DateTime.UtcNow;
}
