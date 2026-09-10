using System.ComponentModel.DataAnnotations;

namespace Aiursoft.MoongladeV2.Entities;

/// <summary>
/// A standalone, administrator-authored HTML page. Custom pages deliberately do
/// not share the blog document model: they have different publishing, routing,
/// rendering, localization and migration semantics.
/// </summary>
public class CustomPage
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(128)]
    public required string Title { get; set; }

    [MaxLength(128)]
    public required string Slug { get; set; }

    [MaxLength(256)]
    public required string MetaDescription { get; set; }

    [MaxLength(65535)]
    public required string HtmlContent { get; set; }

    [MaxLength(65535)]
    public required string CssContent { get; set; }

    /// <summary>
    /// Retained as first-class data for lossless MoongladePure migration. The
    /// current V2 public layout has no blog sidebar, so this flag is inert today.
    /// </summary>
    public bool HideSidebar { get; set; } = true;

    public bool IsPublished { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
