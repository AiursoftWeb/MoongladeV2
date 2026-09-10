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
    /// The registered user who wrote this comment. Null for guest comments.
    /// </summary>
    [MaxLength(450)]
    public string? UserId { get; set; }

    /// <summary>
    /// The name supplied by a guest commenter. Null for registered users.
    /// </summary>
    [MaxLength(64)]
    public string? GuestName { get; set; }

    /// <summary>
    /// Null = root comment on a post. Non-null = reply to a parent comment.
    /// </summary>
    public Guid? ParentCommentId { get; set; }

    /// <summary>
    /// The comment body, in plain text or simple markdown.
    /// </summary>
    [MaxLength(65535)]
    public required string Content { get; set; }

    /// <summary>
    /// When the comment was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ── Navigation ─────────────────────────────────────────────────────────────

    [ForeignKey(nameof(DocumentId))]
    public MarkdownDocument Document { get; set; } = null!;

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [NotMapped]
    public string AuthorDisplayName => User?.DisplayName ?? GuestName ?? "Guest";

    [ForeignKey(nameof(ParentCommentId))]
    public Comment? ParentComment { get; set; }

    public List<Comment> Replies { get; set; } = [];
}
