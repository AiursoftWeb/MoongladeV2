using Aiursoft.MoongladeV2.Entities;

namespace Aiursoft.MoongladeV2.Models.UsersViewModels;

public class UserWithRolesViewModel
{
    public required User User { get; set; }
    public required IList<string> Roles { get; set; }
}
