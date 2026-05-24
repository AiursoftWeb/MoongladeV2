using System.ComponentModel.DataAnnotations;

namespace Aiursoft.MoongladeV2.Models.ManageViewModels;

public class SwitchThemeViewModel
{
    [Required]
    public required string Theme { get; set; }
}
