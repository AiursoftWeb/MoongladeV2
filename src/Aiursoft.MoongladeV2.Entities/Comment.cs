using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aiursoft.MoongladeV2.Entities;

public class Comment
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The document (blog post) this comment belongs to.
    /// </summary>
    public Guid DocumentId { get; set; }

    /// <summary>
    /// The user who wrote this comment.
    /// </summary>
    [MaxLength(450)]
    public required string UserId { get; set; }

    /// <summary>
    /// Null = root comment on a post. Non-null = reply to a parent comment.
    /// </summary>
    public Guid? ParentCommentId { get; set; }

    /// <summary>
    /// The comment body, in plain text or simple markdown.
    /// </summary>
    [MaxLength(1000)]
    public required string Content { get; set; }

    /// <summary>
    /// When the comment was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether the comment has been approved by an admin.
    /// </summary>
    public bool IsApproved { get; set; }

    // ── Navigation ─────────────────────────────────────────────────────────────

    [ForeignKey(nameof(DocumentId))]
    public MarkdownDocument Document { get; set; } = null!;

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    [ForeignKey(nameof(ParentCommentId))]
    public Comment? ParentComment { get; set; }

    public List<Comment> Replies { get; set; } = [];
}
