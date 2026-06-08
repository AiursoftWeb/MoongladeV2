using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.MoongladeV2.Entities;

public class MarkdownDocument
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(100)]
    public string? Title { get; set; }

    [MaxLength(65535)]
    public string? Content { get; set; }

    public DateTime CreationTime { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Updated whenever Title or Content changes. Used to detect stale embeddings and translations.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [StringLength(64)]
    public required string UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    [NotNull]
    public User? User { get; set; }

    /// <summary>
    /// Whether the document is public for everyone to view.
    /// </summary>
    public bool IsPublic { get; set; }

    // ── Blog metadata ──────────────────────────────────────────────────────────

    /// <summary>
    /// URL-friendly slug, e.g. "my-first-post". Must be unique when set.
    /// </summary>
    [MaxLength(200)]
    public string? Slug { get; set; }

    /// <summary>
    /// Comma-separated tags, e.g. "dotnet,azure,ai".
    /// </summary>
    [MaxLength(500)]
    public string? Tags { get; set; }

    /// <summary>
    /// When true the post is pinned/featured at the top of the blog.
    /// </summary>
    public bool IsFeatured { get; set; }

    /// <summary>
    /// Optional cover image URL shown in post cards and the post header.
    /// </summary>
    [MaxLength(500)]
    public string? HeroImageUrl { get; set; }

    // ── AI / vector search ─────────────────────────────────────────────────────

    /// <summary>
    /// BCP-47 language code of the original content, e.g. "zh-CN", "en-US".
    /// Auto-detected by DetectSourceCultureJob. Null until detected —
    /// downstream jobs skip documents with null SourceCulture.
    /// </summary>
    [MaxLength(10)]
    public string? SourceCulture { get; set; }

    /// <summary>
    /// Serialized float[] embedding vector (4 bytes × N dims, little-endian).
    /// Null until the embedding background job processes this document.
    /// </summary>
    public byte[]? Embedding { get; set; }

    /// <summary>
    /// When the current <see cref="Embedding"/> was generated.
    /// The embedding job re-runs when <see cref="UpdatedAt"/> is newer than this value.
    /// </summary>
    public DateTime LastEmbeddedAt { get; set; } = DateTime.MinValue;

    // ── Navigation ─────────────────────────────────────────────────────────────

    [InverseProperty(nameof(DocumentShare.Document))]
    public IEnumerable<DocumentShare> DocumentShares { get; init; } = new List<DocumentShare>();

    [InverseProperty(nameof(LocalizedDocument.Document))]
    public ICollection<LocalizedDocument> LocalizedDocuments { get; init; } = new List<LocalizedDocument>();

    [InverseProperty(nameof(Comment.Document))]
    public ICollection<Comment> Comments { get; init; } = new List<Comment>();
}
