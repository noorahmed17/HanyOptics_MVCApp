using Microsoft.AspNetCore.Identity;
namespace HanyOptics.DataAccess.Identity;
public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
}
