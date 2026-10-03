using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;
using YHAB.Features.Account.Pages.Manage;

namespace YHAB.ComponentTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class PersonalDataTests
{
    [Fact]
    public async Task AuthenticatedUserCanChooseDownloadOrDeletionWithoutMutatingAccountAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();
        account.Authenticate();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Account/Manage/PersonalData");

        var component = account.Render<PersonalData>(context);

        await account.Users.Received(1).GetUserAsync(account.Http.User);
        component.Find("form").GetAttribute("action").ShouldBe("Account/Manage/DownloadPersonalData");
        component.Find("form").GetAttribute("method").ShouldBe("post");
        component.Find("a").GetAttribute("href").ShouldBe("Account/Manage/DeletePersonalData");
        navigation.Uri.ShouldBe("http://localhost/Account/Manage/PersonalData");
        await account.Users.DidNotReceiveWithAnyArgs().DeleteAsync(default!);
    }

    [Fact]
    public async Task MissingUserRedirectsToInvalidUserWithoutMutatingAccountAsync()
    {
        await using var context = new BunitContext();
        var account = context.ConfigureAccount();

        account.Render<PersonalData>(context);

        context.Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/Account/InvalidUser");
        account.StatusCookie.ShouldContain("Unable to load user");
        await account.Users.Received(1).GetUserAsync(account.Http.User);
        await account.Users.DidNotReceiveWithAnyArgs().DeleteAsync(default!);
    }
}
