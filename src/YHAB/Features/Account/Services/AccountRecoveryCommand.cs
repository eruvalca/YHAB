using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using YHAB.Data;
using YHAB.Features.Account.Extensions;

namespace YHAB.Features.Account.Services;

internal static class AccountRecoveryCommand
{
    public static async Task<int> ExecuteAsync(IServiceProvider services, string userId, string origin, TextWriter output, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var address)
            || !string.Equals(address.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            || address.UserInfo.Length != 0 || !string.Equals(address.AbsolutePath, "/", StringComparison.Ordinal)
            || address.Query.Length != 0 || address.Fragment.Length != 0
            || string.IsNullOrWhiteSpace(userId))
        {
            await output.WriteLineAsync("Specify an exact user ID and the application's HTTPS origin (for example https://budget.example.com).".AsMemory(), cancellationToken);
            return 2;
        }

        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IdentityCancellation>().Token = cancellationToken;
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(userId);
        if (user is null)
        {
            await output.WriteLineAsync("Account not found. No recovery link was generated.".AsMemory(), cancellationToken);
            return 1;
        }

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var link = QueryHelpers.AddQueryString(new Uri(address, "Account/ResetPassword").AbsoluteUri, "code", token.EncodeIdentityToken());
        // Deliberately use the operator's output, never application/telemetry logs.
        // Generation makes no account changes; Identity consumes the token on reset.
        await output.WriteLineAsync(link.AsMemory(), cancellationToken);
        return 0;
    }
}
