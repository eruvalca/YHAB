using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using Microsoft.Playwright;
using Shouldly;
using Xunit;

namespace YHAB.PlaywrightTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public test classes for discovery.")]
public sealed class BrowserArtifactsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ArtifactFailuresPreserveOriginalExceptionAndAttemptBothCaptures(bool blockDirectory)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync();
        await context.Tracing.StartAsync(new() { Snapshots = true });
        var page = await context.NewPageAsync();
        await page.SetContentAsync("<h1>Artifact regression check</h1>");
        var root = Path.Combine(AppContext.BaseDirectory, "TestResults", $"artifact-capture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var artifacts = Path.Combine(root, "output");
        if (blockDirectory)
        {
            // A file in place of the output directory fails consistently across OSes.
            await File.WriteAllTextAsync(artifacts, "Not a directory", TestContext.Current.CancellationToken);
        }
        else
        {
            // Screenshot capture must fail while the context can still export its trace.
            await page.CloseAsync();
        }

        var messages = new List<string>();
        var original = new InvalidOperationException("Original navigation failure");
        var actual = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            try
            {
                throw original;
            }
            finally
            {
                await BrowserArtifacts.CaptureAsync(page, context, artifacts, messages.Add);
            }
        });

        actual.ShouldBeSameAs(original);
        messages.ShouldContain(message => message.Contains("page.png", StringComparison.Ordinal));
        if (blockDirectory)
        {
            messages.Count.ShouldBe(2);
            messages.ShouldContain(message => message.Contains("trace.zip", StringComparison.Ordinal));
        }
        else
        {
            messages.Count.ShouldBe(1);
            await using var trace = await ZipFile.OpenReadAsync(Path.Combine(artifacts, "trace.zip"), TestContext.Current.CancellationToken);
            trace.Entries.ShouldContain(entry => entry.FullName.EndsWith(".trace", StringComparison.Ordinal));
        }
    }
}
