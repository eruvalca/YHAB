using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace YHAB.Data;

// Add profile data for application users by adding properties to the ApplicationUser class
internal sealed class ApplicationUser : IdentityUser
{
    // Shown to people who share a plan with this account. The email stays private.
    [PersonalData]
    [MaxLength(UserDisplayNames.MaximumLength)]
    public string? DisplayName { get; set; }
}

