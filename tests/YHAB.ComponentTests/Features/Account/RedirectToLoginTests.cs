using System.Diagnostics.CodeAnalysis;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using YHAB.UI.Features.Account.Components;

namespace YHAB.ComponentTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class RedirectToLoginTests
{
    [Theory]
    [InlineData("auth")]
    [InlineData("auth?returnUrl=%2Finside&query=a%26b#details")]
    public async Task RedirectPreservesCompleteOriginalUrlAsOneQueryValueAndForcesReloadAsync(string destination)
    {
        await using var context = new BunitContext();
        var navigation = context.Services.GetRequiredService<NavigationManager>().ShouldBeOfType<BunitNavigationManager>();
        navigation.NavigateTo(destination);
        var original = navigation.Uri;

        context.Render<RedirectToLogin>();

        var redirected = new Uri(navigation.Uri);
        redirected.AbsolutePath.ShouldBe("/Account/Login");
        redirected.Fragment.ShouldBeEmpty();
        var query = QueryHelpers.ParseQuery(redirected.Query);
        query.Count.ShouldBe(1);
        query["returnUrl"].ToString().ShouldBe(original);
        navigation.History.First().Options.ForceLoad.ShouldBeTrue();
    }
}
