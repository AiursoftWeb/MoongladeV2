using System.ComponentModel.DataAnnotations;
using Aiursoft.MoongladeV2.Entities;

namespace Aiursoft.MoongladeV2.Models.HomeViewModels;

public class AddShareViewModel
{
    public string? TargetUserId { get; set; }
    
    public string? TargetRoleId { get; set; }
    
    [Required]
    public SharePermission Permission { get; set; }
}
