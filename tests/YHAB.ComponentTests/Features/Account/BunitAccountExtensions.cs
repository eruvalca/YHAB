using System.Text.Encodings.Web;
using Bunit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.FluentUI.AspNetCore.Components;
using NSubstitute;
using YHAB.Data;
using YHAB.Features.Account.Services;

namespace YHAB.ComponentTests.Features.Account;

internal static class BunitAccountExtensions
{
    extension(BunitContext context)
    {
        internal AccountTestContext ConfigureAccount()
        {
            var http = new DefaultHttpContext();
            var emails = Substitute.For<IEmailSender<ApplicationUser>>();
            http.Request.Method = HttpMethods.Post;
            var store = Substitute.For<IUserEmailStore<ApplicationUser>>();
            var options = Options.Create(new IdentityOptions());
            var users = Substitute.For<UserManager<ApplicationUser>>(
                store, options, new PasswordHasher<ApplicationUser>(),
                Array.Empty<IUserValidator<ApplicationUser>>(), Array.Empty<IPasswordValidator<ApplicationUser>>(),
                new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(),
                Substitute.For<IServiceProvider>(), NullLogger<UserManager<ApplicationUser>>.Instance);
            var signIn = Substitute.For<SignInManager<ApplicationUser>>(
                users, new HttpContextAccessor { HttpContext = http },
                Substitute.For<IUserClaimsPrincipalFactory<ApplicationUser>>(), options,
                NullLogger<SignInManager<ApplicationUser>>.Instance,
                Substitute.For<IAuthenticationSchemeProvider>(), Substitute.For<IUserConfirmation<ApplicationUser>>());
            signIn.GetExternalAuthenticationSchemesAsync().Returns(Array.Empty<AuthenticationScheme>());
            context.Services.AddLogging();
            context.Services.AddFluentUIComponents();
            var navigationModule = context.JSInterop.SetupModule("./_content/Microsoft.FluentUI.AspNetCore.Components/Components/Nav/FluentNav.razor.js");
            navigationModule.SetupVoid("Microsoft.FluentUI.Blazor.Nav.Initialize", _ => true).SetVoidResult();
            navigationModule.SetupVoid("Microsoft.FluentUI.Blazor.Nav.Dispose", _ => true).SetVoidResult();
            context.Services.AddSingleton(users);
            context.Services.AddSingleton(signIn);
            context.Services.AddSingleton<IUserStore<ApplicationUser>>(store);
            context.Services.AddSingleton(emails);
            context.Services.AddScoped<AccountSignInService>();
            context.Services.AddScoped<IdentityCancellation>();
            context.Services.AddScoped<AccountPasskeyService>();
            context.Services.AddScoped<AccountRegistrationService>();
            context.Services.AddScoped<AccountEmailChangeService>();
            context.Services.AddScoped<AccountTwoFactorService>();
            context.Services.AddSingleton(UrlEncoder.Default);
            context.Services.AddScoped<IdentityRedirectManager>();
            http.RequestServices = context.Services;
            return new(http, users, signIn, emails);
        }

        internal ILogger<TComponent> CaptureLogs<TComponent>()
        {
            var logger = Substitute.For<ILogger<TComponent>>();
            logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
            context.Services.AddSingleton(logger);
            return logger;
        }
    }
}
