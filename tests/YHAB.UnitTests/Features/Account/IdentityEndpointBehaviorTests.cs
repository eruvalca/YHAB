using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Json;
using Bogus;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Shouldly;
using Xunit;
using YHAB.Data;
using YHAB.Features.Account.Endpoints;

namespace YHAB.UnitTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class IdentityEndpointBehaviorTests
{
    [Fact]
    public async Task ExternalLoginChallengePreservesProviderAndEncodesReturnUrlUnderPathBaseAsync()
    {
        await using var context = EndpointContext.Create();
        const string ReturnUrl = "/events?search=café &page=2";
        var properties = new AuthenticationProperties();
        string? callback = null;
        context.Identity.SignIn.ConfigureExternalAuthenticationProperties("Example", Arg.Any<string>(), null)
            .Returns(call => { callback = call.ArgAt<string>(1); return properties; });
        context.Http.Request.PathBase = "/app";
        context.SetForm(("provider", "Example"), ("returnUrl", ReturnUrl));

        await context.InvokeAsync("/Account/PerformExternalLogin");

        var uri = new Uri(new Uri("https://yhab.test"), callback.ShouldNotBeNull());
        uri.AbsolutePath.ShouldBe("/app/Account/ExternalLogin");
        var query = QueryHelpers.ParseQuery(uri.Query);
        query["ReturnUrl"].ToString().ShouldBe(ReturnUrl);
        query["Action"].ToString().ShouldBe("LoginCallback");
        await context.Authentication.Received(1).ChallengeAsync(context.Http, "Example", properties);
    }

    [Fact]
    public async Task LinkingLoginClearsExternalCookieAndBindsChallengeToCurrentUserAsync()
    {
        await using var context = EndpointContext.Create();
        context.Identity.Users.GetUserId(context.Http.User).Returns("current-user");
        context.Http.Request.PathBase = "/app";
        context.SetForm(("provider", "Example"));
        var properties = new AuthenticationProperties();
        context.Identity.SignIn.ConfigureExternalAuthenticationProperties(
            "Example", "/app/Account/Manage/ExternalLogins?Action=LinkLoginCallback", "current-user").Returns(properties);

        await context.InvokeAsync("/Account/Manage/LinkExternalLogin");

        Received.InOrder(() =>
        {
            _ = context.Authentication.SignOutAsync(context.Http, IdentityConstants.ExternalScheme, null);
            context.Identity.SignIn.ConfigureExternalAuthenticationProperties(
                "Example", "/app/Account/Manage/ExternalLogins?Action=LinkLoginCallback", "current-user");
            _ = context.Authentication.ChallengeAsync(context.Http, "Example", properties);
        });
    }

    [Theory]
    [InlineData("events?view=mine", "/app/events?view=mine")]
    [InlineData("", "/app/")]
    public async Task LogoutSignsOutAndRedirectsWithinApplicationAsync(string destination, string expectedLocation)
    {
        await using var context = EndpointContext.Create();
        context.Http.Request.PathBase = "/app";
        context.SetForm(("returnUrl", destination));

        await context.InvokeAsync("/Account/Logout");

        await context.Identity.SignIn.Received(1).SignOutAsync();
        context.Http.Response.StatusCode.ShouldBe(StatusCodes.Status302Found);
        context.Http.Response.Headers.Location.ToString().ShouldBe(expectedLocation);
    }

    [Theory]
    [InlineData(null, "User")]
    [InlineData("member@example.test", "member@example.test")]
    public async Task PasskeyCreationUsesCurrentUserAndReturnsOptionsJsonAsync(string? username, string expectedName)
    {
        await using var context = EndpointContext.Create();
        var user = CreateUser();
        context.Identity.Users.GetUserAsync(context.Http.User).Returns(user);
        context.Identity.Users.GetUserIdAsync(user).Returns(user.Id);
        context.Identity.Users.GetUserNameAsync(user).Returns(username);
        const string Json = "{\"challenge\":\"creation-challenge\"}";
        context.Identity.SignIn.MakePasskeyCreationOptionsAsync(Arg.Any<PasskeyUserEntity>()).Returns(Json);

        await context.InvokeAsync("/Account/PasskeyCreationOptions");

        await context.Antiforgery.Received(1).ValidateRequestAsync(context.Http);
        await context.Identity.SignIn.Received(1).MakePasskeyCreationOptionsAsync(Arg.Is<PasskeyUserEntity>(
            entity => entity.Id == user.Id && entity.Name == expectedName && entity.DisplayName == expectedName));
        context.Http.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        context.Http.Response.ContentType.ShouldStartWith("application/json");
        (await context.ReadBodyAsync()).ShouldBe(Json);
    }

    [Fact]
    public async Task MissingPasskeyUserReturnsNotFoundWithoutCreatingOptionsAsync()
    {
        await using var context = EndpointContext.Create();
        context.Identity.Users.GetUserAsync(context.Http.User).Returns((ApplicationUser?)null);
        context.Identity.Users.GetUserId(context.Http.User).Returns("deleted-user");

        await context.InvokeAsync("/Account/PasskeyCreationOptions");

        context.Http.Response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        JsonSerializer.Deserialize<string>(await context.ReadBodyAsync()).ShouldBe("Unable to load user with ID 'deleted-user'.");
        await context.Identity.SignIn.DidNotReceiveWithAnyArgs().MakePasskeyCreationOptionsAsync(default!);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("missing@example.test", false)]
    [InlineData("member@example.test", true)]
    public async Task PasskeyRequestSupportsDiscoverableAndNamedCredentialsAsync(string? username, bool found)
    {
        await using var context = EndpointContext.Create();
        var user = found ? CreateUser() : null;
        context.Identity.Users.FindByNameAsync(Arg.Any<string>()).Returns(user);
        const string Json = "{\"challenge\":\"request-challenge\"}";
        context.Identity.SignIn.MakePasskeyRequestOptionsAsync(user).Returns(Json);
        context.Http.Request.QueryString = username is null ? QueryString.Empty : QueryString.Create("username", username);

        await context.InvokeAsync("/Account/PasskeyRequestOptions");

        await context.Antiforgery.Received(1).ValidateRequestAsync(context.Http);
        await context.Identity.SignIn.Received(1).MakePasskeyRequestOptionsAsync(user);
        if (string.IsNullOrEmpty(username))
        {
            await context.Identity.Users.DidNotReceiveWithAnyArgs().FindByNameAsync(default!);
        }
        else
        {
            await context.Identity.Users.Received(1).FindByNameAsync(username);
        }
        context.Http.Response.ContentType.ShouldStartWith("application/json");
        (await context.ReadBodyAsync()).ShouldBe(Json);
    }

    [Theory]
    [InlineData("/Account/PasskeyCreationOptions")]
    [InlineData("/Account/PasskeyRequestOptions")]
    public async Task RejectedAntiforgeryStopsPasskeyLookupAndGenerationAsync(string path)
    {
        await using var context = EndpointContext.Create();
        var failure = new AntiforgeryValidationException("Invalid antiforgery token");
        context.Antiforgery.ValidateRequestAsync(context.Http).Returns(Task.FromException(failure));
        context.Http.Request.QueryString = QueryString.Create("username", "member@example.test");

        (await Should.ThrowAsync<AntiforgeryValidationException>(() => context.InvokeAsync(path))).ShouldBeSameAs(failure);

        await context.Identity.Users.DidNotReceiveWithAnyArgs().GetUserAsync(default!);
        await context.Identity.Users.DidNotReceiveWithAnyArgs().FindByNameAsync(default!);
        await context.Identity.SignIn.DidNotReceiveWithAnyArgs().MakePasskeyCreationOptionsAsync(default!);
        await context.Identity.SignIn.DidNotReceiveWithAnyArgs().MakePasskeyRequestOptionsAsync(default);
    }

    [Fact]
    public async Task PersonalDataDownloadIncludesIdentityAndExternalKeysButExcludesInternalSecretsAsync()
    {
        await using var context = EndpointContext.Create();
        var user = CreateUser();
        user.PhoneNumber = null;
        user.PasswordHash = "password-hash-must-not-leak";
        user.SecurityStamp = "security-stamp-must-not-leak";
        user.ConcurrencyStamp = "concurrency-stamp-must-not-leak";
        context.Identity.Users.GetUserAsync(context.Http.User).Returns(user);
        context.Identity.Users.GetUserIdAsync(user).Returns(user.Id);
        context.Identity.Users.GetLoginsAsync(user).Returns([
            new UserLoginInfo("First", "first-key", "First Provider"),
            new UserLoginInfo("Second", "second-key", "Second Provider")]);
        context.Identity.Users.GetAuthenticatorKeyAsync(user).Returns("authenticator-key");

        await context.InvokeAsync("/Account/Manage/DownloadPersonalData");

        context.Http.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        context.Http.Response.ContentType.ShouldBe("application/json");
        context.Http.Response.Headers.ContentDisposition.ToString().ShouldContain("attachment;");
        context.Http.Response.Headers.ContentDisposition.ToString().ShouldContain("PersonalData.json");
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(await context.ReadBodyAsync()).ShouldNotBeNull();
        data.ShouldBe(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Id"] = user.Id,
            ["UserName"] = user.UserName!,
            ["Email"] = user.Email!,
            ["EmailConfirmed"] = "False",
            ["PhoneNumber"] = "null",
            ["PhoneNumberConfirmed"] = "False",
            ["TwoFactorEnabled"] = "False",
            ["First external login provider key"] = "first-key",
            ["Second external login provider key"] = "second-key",
            ["Authenticator Key"] = "authenticator-key",
        });
    }

    [Fact]
    public async Task MissingDownloadUserReturnsNotFoundWithoutReadingPersonalDataAsync()
    {
        await using var context = EndpointContext.Create();
        context.Identity.Users.GetUserAsync(context.Http.User).Returns((ApplicationUser?)null);
        context.Identity.Users.GetUserId(context.Http.User).Returns("deleted-user");

        await context.InvokeAsync("/Account/Manage/DownloadPersonalData");

        context.Http.Response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        JsonSerializer.Deserialize<string>(await context.ReadBodyAsync()).ShouldBe("Unable to load user with ID 'deleted-user'.");
        context.Http.Response.Headers.ContentDisposition.ShouldBeEmpty();
        await context.Identity.Users.DidNotReceiveWithAnyArgs().GetLoginsAsync(default!);
        await context.Identity.Users.DidNotReceiveWithAnyArgs().GetAuthenticatorKeyAsync(default!);
    }

    private static ApplicationUser CreateUser() => new Faker<ApplicationUser>().UseSeed(814)
        .RuleFor(u => u.Id, f => f.Random.Guid().ToString())
        .RuleFor(u => u.UserName, f => f.Internet.UserName())
        .RuleFor(u => u.Email, f => f.Internet.Email(provider: "example.test"))
        .Generate();

    private sealed class EndpointContext(WebApplication app, IdentityTestContext identity,
        IAuthenticationService authentication, IAntiforgery antiforgery) : IAsyncDisposable
    {
        public IdentityTestContext Identity { get; } = identity;
        public IAuthenticationService Authentication { get; } = authentication;
        public IAntiforgery Antiforgery { get; } = antiforgery;
        public DefaultHttpContext Http { get; } = new() { RequestServices = app.Services };
        private readonly MemoryStream _response = new();

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "The returned context owns and disposes the Identity context and unstarted application.")]
        public static EndpointContext Create()
        {
            var identity = IdentityTestContext.Create();
            var authentication = Substitute.For<IAuthenticationService>();
            var antiforgery = Substitute.For<IAntiforgery>();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(IdentityEndpointBehaviorTests).Assembly.GetName().Name,
            });
            builder.Services.AddSingleton(identity.Users);
            builder.Services.AddSingleton(identity.SignIn);
            builder.Services.AddSingleton(authentication);
            builder.Services.AddSingleton(antiforgery);
            builder.Services.AddSingleton(Substitute.For<AuthenticationStateProvider>());
            var app = builder.Build();
            app.MapAdditionalIdentityEndpoints();
            return new(app, identity, authentication, antiforgery);
        }

        public void SetForm(params (string Name, string Value)[] fields)
        {
            Http.Request.ContentType = "application/x-www-form-urlencoded";
            Http.Request.ContentLength = 1;
            Http.Request.Form = new FormCollection(fields.ToDictionary(f => f.Name, f => new StringValues(f.Value), StringComparer.Ordinal));
        }

        public Task InvokeAsync(string path)
        {
            // Execute the mapped delegate in memory; the app is never started and no HTTP request is sent.
            Http.Request.Method = HttpMethods.Post;
            Http.Request.Path = path;
            Http.Response.Body = _response;
            var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints)
                .OfType<RouteEndpoint>().Single(e => string.Equals(e.RoutePattern.RawText, path, StringComparison.Ordinal));
            return endpoint.RequestDelegate!(Http);
        }

        public async Task<string> ReadBodyAsync()
        {
            _response.Position = 0;
            using var reader = new StreamReader(_response, leaveOpen: true);
            return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await app.DisposeAsync();
            Identity.Dispose();
            await _response.DisposeAsync();
        }
    }
}
