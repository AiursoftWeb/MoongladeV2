using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;
using Aiursoft.MoongladeV2.Attributes;

namespace Aiursoft.MoongladeV2.Models.HomeViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    // ReSharper disable once UnusedMember.Global
    [Obsolete("This constructor is only used for framework!", true)]
    public IndexViewModel()
    {
        PageTitle = "Post Editor";
    }

    public IndexViewModel(string? articleTitle = null)
    {
        if (string.IsNullOrWhiteSpace(articleTitle))
        {
            articleTitle = "Untitled Post";
        }
        PageTitle = $"{articleTitle} - Post Editor";
    }

    [Required(ErrorMessage = "Please input your markdown content!")]
    [NoBadWords(ErrorMessage = "The post content contains sensitive words.")]
    public string InputMarkdown { get; set; } = """
                                                # New post

                                                Start writing your post here.

                                                ## Notes

                                                - Draft the main idea.
                                                - Add links, images, and code snippets as needed.

                                                """;

    public string OutputHtml { get; set; } = string.Empty;

    [Required(ErrorMessage = "Something went wrong, please try again later.")]
    public Guid DocumentId { get; set; } = Guid.NewGuid();

    public bool IsEditing { get; init; }

    [MaxLength(100)]
    [NoBadWords(ErrorMessage = "The post title contains sensitive words.")]
    public string? Title { get; set; }

    /// <summary>
    /// Indicates whether the document is publicly accessible.
    /// </summary>
    public bool IsPublic { get; set; }

    /// <summary>
    /// The public link to view this document.
    /// </summary>
    public string? PublicLink { get; set; }


    public bool SavedSuccessfully { get; set; }

    public bool HasInternalShares { get; set; }
}
