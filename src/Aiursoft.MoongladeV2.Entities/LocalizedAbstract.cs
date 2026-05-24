using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Entities;

/// <summary>
/// AI-generated abstract for a <see cref="MarkdownDocument"/> in a specific BCP-47 culture.
/// One row per (DocumentId × Culture) pair.
/// </summary>
[Index(nameof(DocumentId), nameof(Culture), IsUnique = true)]
public class LocalizedAbstract
{
    [Key]
    public int Id { get; set; }

    public Guid DocumentId { get; set; }

    [ForeignKey(nameof(DocumentId))]
    public MarkdownDocument Document { get; set; } = null!;

    [MaxLength(20)]
    public required string Culture { get; set; }

    [MaxLength(1024)]
    public string Abstract { get; set; } = string.Empty;

    /// <summary>
    /// Compared against <see cref="MarkdownDocument.UpdatedAt"/> to decide if regeneration is needed.
    /// </summary>
    public DateTime LastGeneratedAt { get; set; } = DateTime.UtcNow;
}
