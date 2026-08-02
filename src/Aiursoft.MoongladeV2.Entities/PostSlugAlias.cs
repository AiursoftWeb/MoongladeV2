using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aiursoft.MoongladeV2.Entities;

public class PostSlugAlias
{
    [Key]
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    [ForeignKey(nameof(DocumentId))]
    public MarkdownDocument Document { get; set; } = null!;

    public DateTime PublishedDate { get; set; }

    [MaxLength(200)]
    public required string Slug { get; set; }

    public DateTime RetiredAt { get; set; }

    public DateTime ExpiresAt { get; set; }
}
