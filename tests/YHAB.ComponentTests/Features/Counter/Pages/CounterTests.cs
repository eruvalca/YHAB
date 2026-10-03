using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Shouldly;
using Xunit;
using CounterPage = YHAB.UI.Features.Counter.Pages.Counter;

namespace YHAB.ComponentTests.Features.Counter.Pages;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class CounterTests
{
    [Theory]
    [InlineData("Static", false)]
    [InlineData("Server", true)]
    [InlineData("WebAssembly", true)]
    public async Task RenderShowsInitialCountAndIncrementButtonAsync(string renderer, bool interactive)
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.ComponentFactories.AddStub<FluentProviders>();
        context.Renderer.SetRendererInfo(new RendererInfo(renderer, interactive));

        var component = context.Render<CounterPage>();

        component.Find("h1").TextContent.ShouldBe("Counter");
        component.Find("[role='status']").TextContent.ShouldBe("Current count: 0");
        component.Find("fluent-button").TextContent.Trim().ShouldBe("Click me");
        component.Find("fluent-button").GetAttribute("appearance").ShouldBe("primary");
        component.Find("fluent-button").HasAttribute("disabled").ShouldBe(!interactive);
    }

    [Theory]
    [InlineData(1, "Current count: 1")]
    [InlineData(2, "Current count: 2")]
    [InlineData(5, "Current count: 5")]
    public async Task ClickingIncrementButtonUpdatesDisplayedCountAsync(int clicks, string expectedStatus)
    {
        await using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.ComponentFactories.AddStub<FluentProviders>();
        context.Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var component = context.Render<CounterPage>();

        for (var click = 0; click < clicks; click++)
        {
            await component.Find("fluent-button").ClickAsync();
        }

        await component.WaitForAssertionAsync(() => component.Find("[role='status']").TextContent.ShouldBe(expectedStatus));
    }

    [Fact]
    public async Task IndependentContextsKeepCounterStateIsolatedAsync()
    {
        await using var firstContext = new BunitContext();
        await using var secondContext = new BunitContext();
        firstContext.Services.AddFluentUIComponents();
        secondContext.Services.AddFluentUIComponents();
        firstContext.ComponentFactories.AddStub<FluentProviders>();
        secondContext.ComponentFactories.AddStub<FluentProviders>();
        firstContext.Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        secondContext.Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var first = firstContext.Render<CounterPage>();
        var second = secondContext.Render<CounterPage>();

        await first.Find("fluent-button").ClickAsync();

        await first.WaitForAssertionAsync(() => first.Find("[role='status']").TextContent.ShouldBe("Current count: 1"));
        second.Find("[role='status']").TextContent.ShouldBe("Current count: 0");
    }
}
