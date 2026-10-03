using System.ComponentModel.DataAnnotations;

namespace YHAB.Features.Account.Models;

/// <summary>
/// The login form data populated by Blazor form mapping for password or passkey sign-in.
/// </summary>
internal sealed class LoginInputModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Display(Name = "Remember me?")]
    public bool RememberMe { get; set; }

    public PasskeyInputModel? Passkey { get; set; }
}
