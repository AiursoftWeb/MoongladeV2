using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.MoongladeV2.Models.CustomPageViewModels;

public class EditViewModel : UiStackLayoutViewModel, IValidatableObject
{
    public EditViewModel()
    {
        PageTitle = "Custom Page Editor";
    }

    public Guid? Id { get; set; }

    [Required]
    [MaxLength(128)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    [RegularExpression("[a-z0-9]+(?:-[a-z0-9]+)*",
        ErrorMessage = "Use only lowercase English letters, numbers, and single hyphens.")]
    public string Slug { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string MetaDescription { get; set; } = string.Empty;

    [MaxLength(65535)]
    public string? HtmlContent { get; set; }

    [MaxLength(65535)]
    public string? CssContent { get; set; }

    public bool HideSidebar { get; set; } = true;

    public bool IsPublished { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CssContent?.Contains("</style", StringComparison.OrdinalIgnoreCase) == true)
        {
            yield return new ValidationResult(
                "CSS cannot contain an HTML style closing tag.",
                [nameof(CssContent)]);
        }
    }
}
