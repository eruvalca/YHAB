using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using Shouldly;
using Xunit;
using YHAB.Features.Account.Services;

namespace YHAB.UnitTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery in CLI and IDE runners.")]
public sealed class IdentityRedirectManagerTests
{
    private const string ApplicationBaseUri = "https://yhab.example/app/";
    private const string CurrentUri = ApplicationBaseUri + "Account/Manage?tab=profile#details";

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("Account/Login", "Account/Login")]
    [InlineData("Account/Login?returnUrl=%2Fevents", "Account/Login?returnUrl=%2Fevents")]
    public void RedirectToRelativeDestinationNavigatesOnce(string? destination, string expectedDestination)
    {
        var navigation = RecordingNavigationManager.Create();
        var manager = new IdentityRedirectManager(navigation);

        manager.RedirectTo(destination);

        navigation.Destinations.ShouldHaveSingleItem().ShouldBe(expectedDestination);
    }

    [Theory]
    [InlineData(ApplicationBaseUri, "")]
    [InlineData(ApplicationBaseUri + "Account/Login", "Account/Login")]
    [InlineData(ApplicationBaseUri + "Account/Login?returnUrl=%2Fevents#form", "Account/Login?returnUrl=%2Fevents#form")]
    public void RedirectToAbsoluteDestinationWithinApplicationConvertsToRelativePath(
        string destination, string expectedDestination)
    {
        var navigation = RecordingNavigationManager.Create();
        var manager = new IdentityRedirectManager(navigation);

        manager.RedirectTo(destination);

        navigation.Destinations.ShouldHaveSingleItem().ShouldBe(expectedDestination);
    }

    [Theory]
    [InlineData("https://other.example/Account/Login")]
    [InlineData("https://yhab.example/Account/Login")]
    [InlineData("https://yhab.example/application/Account/Login")]
    [InlineData("http://yhab.example/app/Account/Login")]
    public void RedirectToAbsoluteDestinationOutsideApplicationThrowsWithoutNavigating(string destination)
    {
        var navigation = RecordingNavigationManager.Create();
        var manager = new IdentityRedirectManager(navigation);

        Should.Throw<ArgumentException>(() => manager.RedirectTo(destination))
            .ShouldBeOfType<ArgumentException>();

        navigation.Destinations.ShouldBeEmpty();
    }

    [Fact]
    public void RedirectToQueryParametersReplacesExistingQueryAndEncodesValues()
    {
        var navigation = RecordingNavigationManager.Create();
        var manager = new IdentityRedirectManager(navigation);
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["returnUrl"] = "/events?search=a&b=two words",
            ["page"] = 2,
            ["unused"] = null,
        };

        manager.RedirectTo("Account/Login?old=value#form", parameters);

        var destination = navigation.Destinations.ShouldHaveSingleItem();
        destination.ShouldBe("Account/Login?returnUrl=%2Fevents%3Fsearch%3Da%26b%3Dtwo%20words&page=2");
        var query = QueryHelpers.ParseQuery(new Uri(new Uri(ApplicationBaseUri), destination).Query);
        query.Count.ShouldBe(2);
        query["returnUrl"].ToString().ShouldBe("/events?search=a&b=two words");
        query["page"].ToString().ShouldBe("2");
    }

    [Fact]
    public void RedirectToEmptyQueryParametersRemovesExistingQueryAndFragment()
    {
        var navigation = RecordingNavigationManager.Create();
        var manager = new IdentityRedirectManager(navigation);

        manager.RedirectTo("Account/Login?old=value#form", new Dictionary<string, object?>(StringComparer.Ordinal));

        navigation.Destinations.ShouldHaveSingleItem().ShouldBe("Account/Login");
    }

    [Fact]
    public void RedirectToCurrentPageRemovesQueryAndFragment()
    {
        var navigation = RecordingNavigationManager.Create();
        var manager = new IdentityRedirectManager(navigation);

        manager.RedirectToCurrentPage();

        navigation.Destinations.ShouldHaveSingleItem().ShouldBe("Account/Manage");
    }

    [Fact]
    public void RedirectToWithStatusWritesStatusCookieAndNavigates()
    {
        var navigation = RecordingNavigationManager.Create();
        var manager = new IdentityRedirectManager(navigation);
        var context = new DefaultHttpContext();
        const string Message = "Profile saved: A&B + café";

        manager.RedirectToWithStatus("Account/Manage", Message, context);

        navigation.Destinations.ShouldHaveSingleItem().ShouldBe("Account/Manage");
        AssertStatusCookie(context, Message);
    }

    [Fact]
    public void RedirectToCurrentPageWithStatusWritesStatusCookieAndRemovesQueryAndFragment()
    {
        var navigation = RecordingNavigationManager.Create();
        var manager = new IdentityRedirectManager(navigation);
        var context = new DefaultHttpContext();
        const string Message = "Settings updated.";

        manager.RedirectToCurrentPageWithStatus(Message, context);

        navigation.Destinations.ShouldHaveSingleItem().ShouldBe("Account/Manage");
        AssertStatusCookie(context, Message);
    }

    private static void AssertStatusCookie(HttpContext context, string expectedMessage)
    {
        var cookie = SetCookieHeaderValue.Parse(context.Response.Headers.SetCookie.ShouldHaveSingleItem()!);

        cookie.Name.ToString().ShouldBe("Identity.StatusMessage");
        Uri.UnescapeDataString(cookie.Value.ToString()).ShouldBe(expectedMessage);
        cookie.HttpOnly.ShouldBeTrue();
        cookie.SameSite.ShouldBe(Microsoft.Net.Http.Headers.SameSiteMode.Strict);
        cookie.MaxAge.ShouldBe(TimeSpan.FromSeconds(5));
        cookie.Path.ToString().ShouldBe("/");
    }

    private sealed class RecordingNavigationManager : NavigationManager
    {
        public List<string> Destinations { get; } = [];

        public static RecordingNavigationManager Create()
        {
            var navigation = new RecordingNavigationManager();
            navigation.Initialize(ApplicationBaseUri, CurrentUri);
            return navigation;
        }

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Destinations.Add(uri);
        }
    }
}
