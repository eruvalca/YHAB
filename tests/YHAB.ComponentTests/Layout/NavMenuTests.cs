using System.Diagnostics.CodeAnalysis;
using Bogus;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Shouldly;
using Xunit;
using YHAB.UI.Layout;

namespace YHAB.ComponentTests.Layout;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class NavMenuTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticationStateSelectsGuestLinksOrCompactAccountAndLogoutAsync(bool authenticated)
    {
        await using var context = new BunitContext();
        ConfigureFluentNavigation(context);
        var authorization = context.AddAuthorization();
        var faker = new Faker { Random = new Randomizer(7214) };
        var userName = faker.Internet.UserName() + "<admin>";
        if (authenticated)
        {
            authorization.SetAuthorized(userName);
        }

        var component = context.Render<NavMenu>();

        foreach (var link in component.FindAll("nav a"))
        {
            link.Closest("[data-enhance-nav='false']").ShouldBeNull();
            link.GetAttribute("tabindex").ShouldBe("0");
        }

        if (authenticated)
        {
            component.Find("a[href='Account/Manage']").TextContent.Trim().ShouldBe("Your account");
            component.Markup.ShouldNotContain(userName);
            component.FindAll("admin, a[href='Account/Login'], a[href='Account/Register']").ShouldBeEmpty();
            component.Find("form[action='Account/Logout']").GetAttribute("method").ShouldBe("post");
        }
        else
        {
            component.Find("a[href='Account/Login']").TextContent.Trim().ShouldBe("Login");
            component.Find("a[href='Account/Register']").TextContent.Trim().ShouldBe("Register");
            component.FindAll("form[action='Account/Logout'], a[href='Account/Manage']").ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task LogoutReturnUrlTracksNavigationAndStopsUpdatingAfterDisposalAsync()
    {
        await using var context = new BunitContext();
        ConfigureFluentNavigation(context);
        context.AddAuthorization().SetAuthorized("member");
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("events?search=a%26b#upcoming");
        var component = context.Render<NavMenu>();
        component.Find("input[name='ReturnUrl']").GetAttribute("value").ShouldBe("events?search=a%26b#upcoming");

        await component.InvokeAsync(() => navigation.NavigateTo("counter?count=2&mode=short#value"));

        component.Find("input[name='ReturnUrl']").GetAttribute("value").ShouldBe("counter?count=2&mode=short#value");
        await component.InvokeAsync(component.Instance.Dispose);

        await component.InvokeAsync(() => navigation.NavigateTo("auth"));

        component.Find("input[name='ReturnUrl']").GetAttribute("value").ShouldBe("counter?count=2&mode=short#value");
    }

    private static void ConfigureFluentNavigation(BunitContext context)
    {
        context.Services.AddFluentUIComponents();
        var module = context.JSInterop.SetupModule("./_content/Microsoft.FluentUI.AspNetCore.Components/Components/Nav/FluentNav.razor.js");
        module.SetupVoid("Microsoft.FluentUI.Blazor.Nav.Initialize", _ => true).SetVoidResult();
        module.SetupVoid("Microsoft.FluentUI.Blazor.Nav.Dispose", _ => true).SetVoidResult();
    }
}
