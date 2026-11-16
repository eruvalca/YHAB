using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using YHAB.Testing;

namespace YHAB.PlaywrightTests;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
[Collection<BrowserAppHostDefinition>]
public sealed class PasskeyBrowserTests(ITestOutputHelper output)
{
    // Password managers can return credential-like objects whose toJSON() omits
    // clientExtensionResults, so both fakes reproduce that shape. The posted
    // payload then proves the component serializes the credential itself.
    private const string PasswordManagerCredentialScript = """
        (() => {
            const bytes = (...values) => new Uint8Array(values).buffer;
            const credential = response => ({
                id: 'AQID',
                rawId: bytes(1, 2, 3),
                type: 'public-key',
                authenticatorAttachment: 'platform',
                response,
                toJSON() {
                    return {
                        id: this.id,
                        rawId: this.rawId,
                        type: this.type,
                        authenticatorAttachment: this.authenticatorAttachment,
                        response,
                    };
                },
            });

            CredentialsContainer.prototype.create = async () => credential({
                clientDataJSON: bytes(10, 11, 12),
                attestationObject: bytes(13, 14, 15, 16),
                getTransports: () => ['internal'],
            });
            CredentialsContainer.prototype.get = async options => {
                if (options?.mediation === 'conditional') {
                    throw new DOMException('Conditional mediation is disabled in this test.', 'NotAllowedError');
                }
                return credential({
                    clientDataJSON: bytes(20, 21, 22),
                    authenticatorData: bytes(23, 24, 25),
                    signature: bytes(26, 27, 28),
                    userHandle: bytes(29, 30),
                });
            };
            PublicKeyCredential.isConditionalMediationAvailable = async () => false;
        })();
        """;

    [Fact]
    [Trait("Category", "BrowserSmoke")]
    public async Task PasskeyCommandsPostCompleteCredentialJsonAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        await using var builder = await TestAppHost.CreateAsync(timeout.Token);
        await using var app = await builder.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("yhab", timeout.Token);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(new()
        {
            BaseURL = app.GetEndpoint("yhab", "https").ToString(),
            IgnoreHTTPSErrors = true,
            // The app's service worker handles navigations, which bypasses page routes.
            ServiceWorkers = ServiceWorkerPolicy.Block,
        });
        await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = true });
        var page = await context.NewPageAsync();
        try
        {
            await page.AddInitScriptAsync(PasswordManagerCredentialScript);

            await BudgetWorkflowTests.RegisterAndLoginAsync(page);
            await page.GotoAsync("/Account/Manage/Passkeys");
            var attestationJson = await CaptureSubmissionAsync(
                page,
                new Regex(@"/Account/Manage/Passkeys(?=$|\?)", RegexOptions.None, TimeSpan.FromSeconds(5)),
                () => page.GetByRole(AriaRole.Button, new() { Name = "Add a new passkey", Exact = true }).ClickAsync(),
                "Input.CredentialJson");
            var attestation = ParseCredentialJson(attestationJson);
            AssertCommonCredentialShape(attestation);
            attestation.GetProperty("authenticatorAttachment").GetString().ShouldBe("platform");
            var attestationResponse = attestation.GetProperty("response");
            attestationResponse.GetProperty("clientDataJSON").GetString().ShouldBe("CgsM");
            attestationResponse.GetProperty("attestationObject").GetString().ShouldBe("DQ4PEA");
            var transports = attestationResponse.GetProperty("transports").EnumerateArray().ToArray();
            transports.Length.ShouldBe(1);
            transports[0].GetString().ShouldBe("internal");
            attestationResponse.TryGetProperty("signature", out _).ShouldBeFalse();

            await page.GotoAsync("/Account/Login");
            await page.GetByLabel("Email", new() { Exact = true }).FillAsync("passkey@example.test");
            // The login page posts through an enhanced EditForm, whose Blazor submit
            // handling runs first, so invoke the submitter the passkey button uses.
            var assertionJson = await CaptureSubmissionAsync(
                page,
                new Regex(@"/Account/Login(?=$|\?)", RegexOptions.None, TimeSpan.FromSeconds(5)),
                () => page.EvaluateAsync("() => document.querySelector('passkey-submit').obtainAndSubmitCredential()"),
                "Input.Passkey.CredentialJson");
            var assertion = ParseCredentialJson(assertionJson);
            AssertCommonCredentialShape(assertion);
            var assertionResponse = assertion.GetProperty("response");
            assertionResponse.GetProperty("clientDataJSON").GetString().ShouldBe("FBUW");
            assertionResponse.GetProperty("authenticatorData").GetString().ShouldBe("FxgZ");
            assertionResponse.GetProperty("signature").GetString().ShouldBe("Ghsc");
            assertionResponse.GetProperty("userHandle").GetString().ShouldBe("HR4");
            assertionResponse.TryGetProperty("attestationObject", out _).ShouldBeFalse();
        }
        finally
        {
            var artifacts = Path.Combine(AppContext.BaseDirectory, "TestResults", $"passkey-{Guid.NewGuid():N}");
            await BrowserArtifacts.CaptureAsync(page, context, artifacts, output.WriteLine);
        }
    }

    private static async Task<string> CaptureSubmissionAsync(IPage page, Regex path, Func<Task> submit, string fieldName)
    {
        var posted = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync(path, async route =>
        {
            if (!string.Equals(route.Request.Method, "POST", StringComparison.Ordinal))
            {
                await route.ContinueAsync();
                return;
            }

            posted.TrySetResult(route.Request.PostData);
            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "text/html",
                Body = "<!DOCTYPE html><html><body>Captured passkey submission.</body></html>",
            });
        });

        await submit();
        var postData = await posted.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await page.Locator("body").Filter(new() { HasText = "Captured passkey submission." }).WaitForAsync();
        await page.UnrouteAllAsync();
        return ReadFormField(postData, fieldName);
    }

    private static string ReadFormField(string? postData, string fieldName)
    {
        var form = postData.ShouldNotBeNull();
        return form.Split('&')
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair.Length == 2 && string.Equals(WebUtility.UrlDecode(pair[0]), fieldName, StringComparison.Ordinal))
            .Select(pair => WebUtility.UrlDecode(pair[1]))
            .ShouldHaveSingleItem();
    }

    private static JsonElement ParseCredentialJson(string credentialJson)
    {
        using var document = JsonDocument.Parse(credentialJson);
        return document.RootElement.Clone();
    }

    private static void AssertCommonCredentialShape(JsonElement credential)
    {
        credential.GetProperty("id").GetString().ShouldBe("AQID");
        credential.GetProperty("rawId").GetString().ShouldBe("AQID");
        credential.GetProperty("type").GetString().ShouldBe("public-key");
        credential.TryGetProperty("clientExtensionResults", out var extensionResults)
            .ShouldBeTrue("The posted credential JSON must include clientExtensionResults even when the credential omits it.");
        extensionResults.ValueKind.ShouldBe(JsonValueKind.Object);
    }
}
