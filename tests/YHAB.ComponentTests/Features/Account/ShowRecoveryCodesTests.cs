using System.Diagnostics.CodeAnalysis;
using Bogus;
using Bunit;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;
using YHAB.Features.Account.Components;

namespace YHAB.ComponentTests.Features.Account;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class ShowRecoveryCodesTests
{
    [Fact]
    public async Task CodesRetainOrderAndTextAndAreReplacedWhenRegeneratedAsync()
    {
        await using var context = new BunitContext();
        var faker = new Faker { Random = new Randomizer(8062) };
        string[] codes = [faker.Random.Replace("####-####"), "<code>&private", faker.Random.Replace("####-####")];
        var component = context.Render<ShowRecoveryCodes>(parameters => parameters
            .AddCascadingValue<HttpContext>(new DefaultHttpContext())
            .Add(recovery => recovery.RecoveryCodes, codes)
            .Add(recovery => recovery.StatusMessage, "Recovery codes regenerated."));

        component.FindAll(".recovery-code").Select(element => element.TextContent).ShouldBe(codes);
        component.FindAll(".recovery-code code").ShouldBeEmpty();
        component.Find("[data-kind='success']").TextContent.ShouldBe("Recovery codes regenerated.");

        string[] replacement = [faker.Random.Replace("####-####")];
        component.Render(parameters => parameters.Add(recovery => recovery.RecoveryCodes, replacement));

        component.FindAll(".recovery-code").Select(element => element.TextContent).ShouldBe(replacement);
    }

    [Fact]
    public async Task EmptyCodesDoNotInventARecoveryCodeOrSuccessMessageAsync()
    {
        await using var context = new BunitContext();

        var component = context.Render<ShowRecoveryCodes>(parameters => parameters
            .AddCascadingValue<HttpContext>(new DefaultHttpContext())
            .Add(recovery => recovery.RecoveryCodes, Array.Empty<string>()));

        component.FindAll(".recovery-code").ShouldBeEmpty();
        component.FindAll("[data-kind='success']").ShouldBeEmpty();
    }
}
