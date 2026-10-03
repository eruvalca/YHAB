using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Identity;

namespace YHAB.Data;

// Add profile data for application users by adding properties to the ApplicationUser class
[SuppressMessage("Major Code Smell", "S2094:Classes should not be empty",
    Justification = "Provides the application's Identity/EF user type while inheriting its current members from IdentityUser.")]
internal sealed class ApplicationUser : IdentityUser
{
}

