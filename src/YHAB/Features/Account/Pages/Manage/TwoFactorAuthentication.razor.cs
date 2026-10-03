using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using YHAB.Data;

namespace YHAB.Features.Account.Pages.Manage;

public sealed partial class TwoFactorAuthentication
{
    private bool _canTrack;
    private bool _hasAuthenticator;
    private int _recoveryCodesLeft;
    private bool _is2faEnabled;
    private bool _isMachineRemembered;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        var user = await UserManager.GetUserAsync(HttpContext.User);
        if (user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        _canTrack = HttpContext.Features.Get<ITrackingConsentFeature>()?.CanTrack ?? true;
        _hasAuthenticator = await UserManager.GetAuthenticatorKeyAsync(user) is not null;
        _is2faEnabled = await UserManager.GetTwoFactorEnabledAsync(user);
        _isMachineRemembered = await SignInManager.IsTwoFactorClientRememberedAsync(user);
        _recoveryCodesLeft = await UserManager.CountRecoveryCodesAsync(user);
    }

    private async Task OnSubmitForgetBrowserAsync()
    {
        await SignInManager.ForgetTwoFactorClientAsync();

        RedirectManager.RedirectToCurrentPageWithStatus(
            "The current browser has been forgotten. When you login again from this browser you will be prompted for your 2fa code.",
            HttpContext);
    }
}
