# Testing

All five test projects target .NET 10 and use xUnit with native
Microsoft.Testing.Platform (MTP) integration. Open the repository root so both
the CLI and editor find `global.json` and `YHAB.slnx`.

| Project | Scope |
| --- | --- |
| `YHAB.UnitTests` | Budget arithmetic, monthly carryover, cash/credit overspending, payment reserves, targets, recurrence, transaction validation, reconciliation, plus Identity and service defaults. |
| `YHAB.ComponentTests` | Calculator input events, month-specific assignments, invalid-input feedback, delayed workspace loading and interactive-only recurring posting, account workflows, navigation, error request IDs, and the shared UI's `Counter`, using bUnit. |
| `YHAB.IntegrationTests` | Real PostgreSQL: migrations and legacy data upgrades, Identity, private plan isolation, cross-plan foreign keys, optimistic concurrency, category merges, editable recurring occurrence dates, durable undo/redo, and deletion of owned ledger data. |
| `YHAB.AspireIntegrationTests` | The real AppHost, database/migration startup dependencies, readiness, resource endpoints, and cross-process HTTP behavior. |
| `YHAB.PlaywrightTests` | Real Chromium: Fluent controls, desktop/mobile navigation, persistent themes, drawer behavior, document continuity, manual plan/purchase/undo/report/reconciliation workflows, and API authentication/antiforgery/revision guards. |

`YHAB.Testing` is a shared support library, not a test project. It configures
isolated AppHost builders for the Aspire and Playwright projects.

The linked fixture under `tests/Scenarios` supplies identical synthetic household
data to unit, persistence, and browser tests without adding AppHost dependencies
to headless projects. The [three-month validation report](../docs/budgeting-validation.md)
records its independent expected totals, growing dataset checkpoints, layer
boundaries, results, and findings. Persistence checkpoints use disposable bulk
fixtures; browser entries use the authenticated API.

The household browser regression holds the WebAssembly runtime binary download
until the first populated workspace works through an interactive server circuit.
It then releases the download, waits for Auto's resource-cache marker before
navigating away, and verifies cached WebAssembly rendering later in
the walkthrough. It also checks the initial SSR HTML and retains the uncaught
browser-error assertion. The workspace's `data-renderer` attribute makes the
rendering path observable without visible implementation details. The bUnit
workspace tests check delayed data loading and recurring commands, not the actual
SSR-to-interactive handoff or SignalR transport.

## Supported stack

Existing stack selected September 26, 2026; infrastructure/browser additions
verified October 2, 2026:

| Component | Version |
| --- | --- |
| .NET SDK | 10.0.401 |
| bunit (component project only) | 2.11.3 |
| xunit.v3.core.mtp-v2 | 4.0.1 |
| Shouldly | 4.3.0 |
| NSubstitute (account tests) | 6.2.0 |
| Bogus (locally seeded fake data) | 35.6.5 |
| Microsoft.Testing.Platform / Platform.MSBuild | 2.4.1 |
| Microsoft.Testing.Extensions.TrxReport | 2.4.1 |
| Microsoft.Testing.Extensions.CodeCoverage | 18.11.2 |
| xunit.analyzers | 2.1.0 |
| Aspire.Hosting.Testing (matches AppHost) | 13.6.0 |
| Testcontainers.PostgreSql | 4.15.0 |
| Microsoft.Playwright | 1.63.0 |
| C# Dev Kit (stable baseline) | 3.40.210 |

Package versions are centralized in `Directory.Packages.props`. Explicit MTP
runtime and MSBuild references advance xUnit's transitive 2.4.0 dependencies to
2.4.1. Test package references use `PrivateAssets="all"`. All test projects
reference xUnit's core MTP package, which supplies the framework and runner
without `xunit.v3.assert`; xUnit analyzers remain included by the root build props.

The root build props recognize the five test-project name suffixes before
evaluating shared analyzer references. Tests inherit nullable analysis, code style
rules, and warnings-as-errors. xUnit test classes are public and sealed; their
type-level CA1515 suppression documents the discovery requirement. The server
grants unit, component, and focused integration tests access to its internal types.
Aspire/browser tests interact across process boundaries instead of accessing server DI.
The component project uses the Razor SDK and references both `YHAB.UI` and the
server project. The server also grants `DynamicProxyGenAssembly2` internal access
so NSubstitute can proxy Identity dependencies closed over the internal
`ApplicationUser` type.

`global.json` selects native .NET 10 MTP mode. Do not add VSTest packages
(`Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`) or the legacy
`TestingPlatformDotnetTestSupport` bridge to these projects. bUnit provides the
component renderer and comparison tools; xUnit and MTP provide discovery and
execution, and Shouldly is the exclusive assertion library.

Use Bogus for realistic generated fixture values with a per-instance seed
(`UseSeed` or a locally assigned `Randomizer`). Do not set the global
`Randomizer.Seed`: test methods and theory rows can run in parallel.
Keep explicit literals for boundaries, encoded tokens, and expected results.

Account tests use `context.ConfigureAccount()` and `context.CaptureLogs<TComponent>()`
from `BunitAccountExtensions`. The resulting `AccountTestContext` holds each test's
HTTP context and Identity dependencies. `ComponentFormExtensions` supplies
`SetFormValue` and `SetInputValue` where static SSR form mapping needs to be simulated;
`LoggerTestExtensions.GetLoggedEventIds()` inspects captured logging calls. Keep
these receiver-focused helpers in the account test namespace and use a fresh,
asynchronously disposed `BunitContext` for every test.

## Build and run

Run these commands from the repository root:

```powershell
dotnet build YHAB.slnx
dotnet test --project tests/YHAB.UnitTests/YHAB.UnitTests.csproj
dotnet test --project tests/YHAB.ComponentTests/YHAB.ComponentTests.csproj

# Install after building; repeat after updating Microsoft.Playwright.
pwsh ./tests/YHAB.PlaywrightTests/bin/Debug/net10.0/playwright.ps1 install chromium
dotnet test --project tests/YHAB.IntegrationTests/YHAB.IntegrationTests.csproj
dotnet test --project tests/YHAB.AspireIntegrationTests/YHAB.AspireIntegrationTests.csproj
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj

# All five projects; requires the infrastructure/browser prerequisites below.
dotnet test --solution YHAB.slnx --no-build --report-trx --coverage --coverage-output-format cobertura --results-directory TestResults
```

TRX and coverage are opt-in. Reports go into the ignored `TestResults` directory;
there is no coverage percentage gate. Use MTP/xUnit filtering options rather than
VSTest flags. This deliberately unmatched filter must fail instead of silently
passing an empty run:

```powershell
dotnet test --project tests/YHAB.UnitTests/YHAB.UnitTests.csproj --no-build --filter-method NoSuchTestMustNotExist
```

MTP reports exit code 8 (zero tests); the outer `dotnet test` command returns a
nonzero exit code.

## Configuration

### Choosing an integration layer

Use **Testcontainers** when the subject is server code plus one real dependency:
PostgreSQL SQL translation, migrations, constraints, transactions, Identity stores,
or a service whose dependency injection you need to control. The initial test
applies real migrations, writes an Identity user, reads it in a fresh scope, and
checks normalized duplicate-name handling. Each test owns a disposable container
with dynamic ports, using `postgres:18.3` to match Aspire 13.6.0. Keep the resource
reaper enabled; do not use development connection strings or container reuse.
For future in-process HTTP tests, `WebApplicationFactory` can be combined with
these containers. This project does not verify AppHost wiring or browser behavior.

Use **Aspire integration tests** when the subject is the assembled application:
resource references, connection injection, migrations before web startup, readiness,
service discovery and HTTP contracts across process boundaries. These use
`Aspire.Hosting.Testing` 13.6.0, matching the AppHost rather than independently
upgrading its testing package. The initial test waits for the real web health
check and migration completion, then checks health, liveness, Home and Login.
Aspire starts actual processes, so test DI does not replace services in the web
process. Configure resources before `BuildAsync`; retain startup dependencies.

Use **Playwright** for browser-observable behavior: Fluent web components and
icons, responsive navigation, focus, forms, cookies and render-mode transitions.
Navigation tests run Home → Register → Home at desktop and mobile widths, check
that the drawer closes, verify theme persistence, check JavaScript errors/overflow,
and assert unchanged `performance.timeOrigin`. Budget workflow tests register a
disposable user, create a plan, evaluate amount expressions, assign money, enter a
purchase, reload, undo with the keyboard, redo, verify reports, and reconcile.
They also check API session, antiforgery, and stale-revision rejection. These tests
do not establish passkey ceremonies or every account workflow.

Browser layout checks verify that Fluent dropdown triggers have no nested native
border/padding, category links retain their native styling, desktop toolbar
controls align, and phone pages do not overflow. Theme checks navigate between
SSR pages after selecting light, dark, and System preferences, verify the resolved
color scheme, and emulate OS changes in System mode. A reload alone does not
exercise Blazor's replacement of body attributes during enhanced navigation.
Opening account, transaction, and reconciliation editors must focus a visible
heading; browser checks also reopen a reconciled entry from its register row.
The reconciliation-editor component cases cover locks on either transfer side,
the displayed clearing state, and saving an unlocked cleared transfer.

Both Aspire and Playwright tests use `YHAB.Testing.TestAppHost`, a small shared
library that removes container volume/bind mounts and enforces session lifetimes
on the real AppHost model. Aspire's testing builder disables the dashboard and
randomizes proxied ports by default. Each test disposes its builder and application,
including on failure; optional pgAdmin remains unstarted. No manually running
development AppHost or CLI start/stop is required for these test-managed runs.
Do not start the development AppHost or rebuild shared outputs while an
Aspire/browser test run is active; this can interrupt its processes or replace
fingerprinted WebAssembly assets. Build first, then run the infrastructure suites.
Readiness uses Aspire notifications with bounded cancellation, not HTTP polling
or fixed sleeps. Use a new browser context for each test; no authentication state
is shared. Only these local HTTPS contexts ignore development certificate errors.

Prerequisites: a running Linux-container Docker engine and available images for
all three projects; Aspire CLI/bundle and the .NET development HTTPS certificate
for Aspire/browser tests; the package-matched Chromium binary for Playwright.
On Linux, install Playwright OS dependencies with `playwright.ps1 install --with-deps chromium`
in the build agent image/setup. Missing dependencies fail tests rather than
silently skipping them. Unit/bUnit projects still need none of this infrastructure.

Playwright uses `Microsoft.Playwright` directly with our xUnit core MTP runner and
Shouldly. Do not add a runner integration that brings VSTest or xUnit assertions
back into the solution. Locator actions auto-wait; await observable readiness
with locators before asserting values with Shouldly. Do not use `WaitForTimeout`.
Each navigation case attempts to retain `page.png` and `trace.zip` independently
in a unique `bin/<configuration>/net10.0/TestResults/navigation-*` directory.
Artifact capture is best effort: failures are reported through xUnit test output
and do not replace the original test failure or prevent the other capture attempt.
`BrowserArtifactsTests` checks closed-page and unwritable-directory failures;
its disposable outputs use `TestResults/artifact-capture-*`. Inspect the trace
with the generated `playwright.ps1 show-trace <path>` command. These artifacts are
ignored; do not commit traces containing cookies or future test account data.
Keep the existing parallel settings: test processes, ports, containers and browser
contexts must be isolated. On constrained CI agents, bound test-module concurrency
with `--max-parallel-test-modules 1`; this does not change test discovery or skip suites.

References (reviewed October 2, 2026): [Aspire testing overview](https://aspire.dev/testing/overview/),
[AppHost lifecycle and isolation](https://aspire.dev/testing/manage-app-host/),
[Testcontainers PostgreSQL](https://dotnet.testcontainers.org/modules/postgres/),
and [Playwright .NET library](https://playwright.dev/dotnet/docs/library).

### Assertions

Use Shouldly for every assertion in all test projects. The stable version is
centrally pinned to 4.3.0 (verified against NuGet's live version feed on September
26, 2026); the newer 5.0 releases are previews. Import `Shouldly` in test files and
use APIs such as `ShouldBe`, `ShouldBeTrue`, `ShouldBeEmpty`,
`ShouldHaveSingleItem`, and `Should.Throw<T>`. xUnit attributes such as `[Fact]`,
`[Theory]`, and `[InlineData]` continue to define tests.

Do not add the xUnit assertion package, FluentAssertions, or use bUnit assertion
helpers such as `MarkupMatches`. For semantic HTML checks, obtain differences
with `component.CompareTo(expectedMarkup)` and assert `differences.ShouldBeEmpty()`.
This preserves bUnit's semantic comparison instead of comparing raw HTML strings.
Use Shouldly inside `WaitForAssertionAsync` when awaiting a render.

Preserve the strength of existing checks during migrations: single-item checks
must still verify cardinality, and exact exception-type checks use
`Should.Throw<T>(action).ShouldBeOfType<T>()` so derived exceptions do not pass.

### Runner settings

Each project's `testconfig.json` uses xUnit's MTP configuration section:

- `failWarns: true` fails tests that produce xUnit warnings.
- `parallelMode: "all"` permits independent tests, including methods and theory
  rows within the same class, to run in parallel (requires xUnit v3 4.0+).
- `parallelAlgorithm: "conservative"` starts another test when an execution slot
  becomes available, bounding the number of active tests.
- `maxParallelThreads: "1x"` sets that limit to the logical processor count,
  adapting to the developer machine or build agent.
- `preEnumerateTheories: true` discovers each theory data row separately.

MTP automatically copies and renames the file to
`<AssemblyName>.testconfig.json` beside each executable. No manual copy item or
separate `xunit.runner.json` is needed.

### Writing tests for parallel execution

Give each test its own mutable state and resources. Shared fixtures must support
concurrent access. In `all` mode, putting tests in the same class or named
collection does not serialize them. Tests that require exclusive access must
explicitly opt out, for example with `[Fact(DisableParallelism = true)]` or a
`[CollectionDefinition("Exclusive", DisableParallelization = true)]` applied to
the relevant collection. Keep these exceptions narrow and document the shared
resource that requires them.

The default aims to use all available processors for CPU-bound unit tests while
limiting scheduling and memory overhead. If a future suite spends substantial
time awaiting I/O, measure before increasing the multiplier or choosing
`aggressive`; aggressive scheduling changes timing and timeout behavior.

MTP's `--max-parallel-test-modules` controls parallel execution of test modules
separately and defaults to the processor count. With multiple test projects,
budget module concurrency together with each project's xUnit concurrency.

### Writing component tests

Use `BunitContext` and `Render<TComponent>()` (the bUnit 2 APIs). Create a fresh
context inside each test with `await using`; do not share a context through static
state or class/collection fixtures. This keeps the renderer, services, component
state, and JS interop setup isolated while methods and theory rows run in parallel.
Avoid changing bUnit's static defaults from individual tests.

Assert rendered DOM values with Shouldly, or use `CompareTo` followed by
`ShouldBeEmpty` for semantic markup. Dispatch UI events with helpers such as
`ClickAsync`, then use `WaitForAssertionAsync` for Shouldly assertions
that depend on a render; awaiting an event handler alone does not guarantee its
render cycle has finished. Register component dependencies in `context.Services`
before rendering and configure required calls on `context.JSInterop` (strict by
default). Keep tests in C# files; any future Razor test helpers must follow the
repository's matching code-behind policy.

bUnit tests run in process without starting Aspire, a web server, or a browser.
They verify component behavior, not browser layout, real JavaScript execution, or
server/WebAssembly render-mode transitions; those need browser-level tests.

Tests rendering Fluent components register `AddFluentUIComponents()` in their
own context. `ConfigureAccount` also registers these services and strictly stubs
the Fluent navigation module's initialize/dispose calls; the shell navigation
tests configure the same calls locally and verify native links remain keyboard
reachable without opting out of enhanced navigation. Counter tests stub the
page's `FluentProviders` to isolate counter behavior from overlay JS, dispatch
clicks on the rendered `fluent-button` host and assert the displayed count. They set bUnit's
`RendererInfo` explicitly and check that the button is disabled in static
prerendering and enabled in Server and WebAssembly rendering. Navigation text
assertions trim library-rendered whitespace while retaining checks for encoded
identity text, conditional account links, and the logout POST/return URL.
These JS stubs do not validate the web components or the static mobile drawer;
check those in a browser through Aspire, including Auto's WebAssembly path.
Navigation checks must cover unchanged `performance.timeOrigin` and no document
requests between Home and Register, mobile drawer close/reopen across repeated
navigation and browser history, scroll reset/restoration on long pages, and
normal Identity POST/cookie round trips.

## Visual Studio Code

Install and enable the recommended **C# Dev Kit** and **C#** extensions from
`.vscode/extensions.json`. C# Dev Kit 3.40.210 requires VS Code 1.101.0 or newer;
the tested C# extension 2.160.4 requires 1.106.0 or newer. Use a current stable
VS Code and the stable extension channel. Extension recommendations do not pin
versions.

1. Open the repository root and let C# Dev Kit load `YHAB.slnx`.
2. Build the solution and open the **Testing** view. Allow C# Dev Kit to finish
   loading the solution and discovering the tests; an empty view during startup
   is temporary. The validated stable version discovers tests automatically and
   did not expose a **Test: Refresh Tests** command in this workspace.
3. Expand the project, namespace, class, and theories to see individual cases.
4. Use Test Explorer or editor gutter actions to run or debug tests. Set
   breakpoints in the test and its production code (`IdentityRedirectManager` or
   `Counter.razor.cs`) to step through both.
5. Use **Run Tests with Coverage** to show coverage in the editor.

Keep automatic discovery and build-on-refresh/run enabled (the defaults). Keep
experimental source-only discovery (`dotnet.testWindow.discoverTestsFromSource`)
disabled. No `launch.json` or custom test protocol setting is required. In
particular, do not add the historical
`dotnet.testWindow.useTestingPlatformProtocol`: it is absent from the supported
stable extension's manifest.

## Coverage expansion (September 28, 2026)

The suite now has **413 passing cases**: 184 unit tests and 229 component tests,
up from 235 cases. The 178 additions cover account management, authentication
revalidation, endpoint responses, shared component contracts, navigation, and
validation boundaries. They also verify failure paths do not write account data,
refresh sessions, send email, or generate credentials unexpectedly.

Validation completed with zero build warnings/errors and zero failed/skipped tests:

```powershell
dotnet build YHAB.slnx --no-incremental
dotnet test --solution YHAB.slnx --no-build --report-trx --coverage --coverage-output-format cobertura --results-directory TestResults/final
```

Coverage below merges both projects' Cobertura reports by distinct source filename
and line number, counting a line covered when either project executes it. It
measures `YHAB`, `YHAB.UI`, and `YHAB.ServiceDefaults`; it is line coverage,
not a claim of complete branch or browser coverage. The collector configuration
was not changed to exclude uncovered files.

| Measured scope | Before | After |
| --- | --- | --- |
| Handwritten application code, excluding generated files, migrations, and startup | 918 / 1,480 (62.03%) | 1,449 / 1,480 (97.91%) |
| All instrumented production code, including those categories | 1,100 / 2,954 (37.24%) | 1,794 / 2,954 (60.73%) |

The 31 remaining application lines comprise 20 defensive guard lines whose states
are prevented by initialization or absent forms, three OTLP exporter configuration
lines, and eight trivial lines (no-op email methods, layout/content slots, the
database-context constructor, and the fixed revalidation interval). Tests do not
mutate cached private state or invoke private event handlers merely to cover them.
Generated logging/union code, migrations, startup, build-tool execution, actual
WebAuthn JavaScript, database persistence, and live external providers remain
outside this unit/component testing scope.

Endpoint tests invoke mapped delegates against in-memory HTTP contexts with
substituted dependencies; they never start their application or send a network
request. Revalidation tests invoke the framework's protected validation hook
directly, avoiding its 30-minute background timer. bUnit tests use actual form
events; the existing form helper supplies posted values only where bUnit does not
run static SSR form mapping. For example, an empty phone string fails validation,
while an explicitly null posted phone value removes the saved number.

## Initial test scope

The account outcome suite exercises the real sign-in, passkey, registration,
email-change, and two-factor services with substituted Identity dependencies.
It verifies each outcome, payloads, operation ordering, short-circuited failures,
credential/token decoding, and the passkey limit. Component tests exercise the
real pages and services to check validation, redirects, account-privacy responses,
partial-failure messages, and recovery-code rendering. Every test owns its mutable
state; no database, Aspire process, browser, or external provider is required.

The following initial-scope descriptions and acceptance records document the
original test setup before the account outcome suite was added.

The 16 server unit cases cover null/empty/relative destinations, absolute destinations within
the application, rejection of external destinations, query replacement and
encoding, current-page redirects, and status-cookie values and attributes.
Tests use an in-memory recording `NavigationManager` and `DefaultHttpContext`;
they do not start the web server, Aspire, a browser, or a database.

The five component cases exercise the real `Counter`: initial markup, count updates
after one/two/five clicks, and independent state in separate contexts.

## Acceptance record

Verified on September 26, 2026, on Windows x64 with .NET SDK 10.0.401,
VS Code **1.139.1**, C# Dev Kit **3.40.210**, and C# **2.160.4**.
The initial acceptance checks below used `parallelMode: "collections"` and xUnit
assertions; the subsequent parallel configuration and Shouldly migration are
documented separately. Source line references in historical editor checks refer
to the files as they existed during those checks.

| Requirement | Evidence |
| --- | --- |
| Full solution builds | `dotnet build YHAB.slnx`: 0 warnings, 0 errors. |
| Project execution | `dotnet test --project tests/YHAB.UnitTests/YHAB.UnitTests.csproj`: 16 passed, 0 failed, 0 skipped. |
| Solution execution and reports | The solution command above passed all 16 cases; TRX counters and Cobertura XML were parsed successfully. |
| Exact dependencies and analyzers | `project.assets.json` resolved the versions above; MSBuild `ResolveReferences` included xUnit and all shared analyzers. Nullable and warnings-as-errors remained enabled. |
| Configuration deployment | Output contained `YHAB.UnitTests.testconfig.json` with the configured xUnit options. |
| Empty selection fails | The unmatched `--filter-method` command returned nonzero; MTP reported exit code 8. |
| Editor discovery | C# Dev Kit discovered all 16 cases, including facts and individual theory rows. Discovery was automatic; no manual refresh command was exposed. |
| Editor execution | Run All and class execution passed 16/16; an individual fact and the null-input theory row each passed 1/1. The fact was run from its editor gutter. |
| Editor debugging | Debug Test paused at `IdentityRedirectManagerTests.cs:102` and `IdentityRedirectManager.cs:29`, then completed. Temporary breakpoints were removed. |
| Build on run and failure display | Temporarily changing the current-page assertion produced 15/16 passing and an `Assert.Equal` failure without a manual build. The assertion was restored. |
| Editor coverage | Run Tests with Coverage rebuilt the restored source, passed 16/16, populated Test Coverage, and displayed covered-line decorations in the redirect manager. |
| Reload | After Developer: Reload Window and solution initialization, discovery returned automatically and Run All passed 16/16 again. |

After enabling `all` / `conservative` / `1x`, `dotnet build YHAB.slnx` passed
with 0 warnings and 0 errors. Running
`dotnet test --project tests/YHAB.UnitTests/YHAB.UnitTests.csproj --no-build`
passed all 16 tests (0 failed, 0 skipped). The deployed
`YHAB.UnitTests.testconfig.json` contained the updated parallel settings.

The source assertions were reviewed against the requested behavior matrix:

| Requirement | Evidence |
| --- | --- |
| Null, empty, and relative destinations | `RedirectToRelativeDestinationNavigatesOnce` (4 rows). |
| Absolute destinations inside the application | `RedirectToAbsoluteDestinationWithinApplicationConvertsToRelativePath` (3 rows). |
| Rejected external absolute destinations | `RedirectToAbsoluteDestinationOutsideApplicationThrowsWithoutNavigating` (4 rows). |
| Query replacement and encoding | `RedirectToQueryParametersReplacesExistingQueryAndEncodesValues`. |
| Empty replacement query | `RedirectToEmptyQueryParametersRemovesExistingQueryAndFragment`. |
| Current-page redirect | `RedirectToCurrentPageRemovesQueryAndFragment`. |
| Status-cookie contents and attributes | `RedirectToWithStatusWritesStatusCookieAndNavigates` and `RedirectToCurrentPageWithStatusWritesStatusCookieAndRemovesQueryAndFragment`. |

### Component project acceptance

The bUnit addition was verified on September 26, 2026 with the same SDK and editor
versions above, using `all` / `conservative` / `1x` in both projects.

| Requirement | Evidence |
| --- | --- |
| Current stable bUnit | NuGet's live version feed and the published release identify 2.11.3; the restored assets resolve that exact version. |
| Full build | `dotnet build YHAB.slnx --no-incremental`: 0 warnings, 0 errors. |
| Direct component execution | `dotnet test --project tests/YHAB.ComponentTests/YHAB.ComponentTests.csproj --no-build`: 5 passed, 0 failed, 0 skipped. |
| Solution execution and reports | The solution coverage command passed 21/21; separate TRX and Cobertura reports were generated for both projects. The component TRX contains five passing results, and Cobertura includes `Counter.razor` and `Counter.razor.cs`. |
| Inherited checks and deployed settings | MSBuild reports `IsTestProject=true`, nullable enabled, warnings-as-errors enabled, and xUnit/Meziantou/Sonar/Roslynator analyzers. The output contains `YHAB.ComponentTests.testconfig.json` with the parallel settings above. |
| Empty selection fails | The component project's unmatched `--filter-method NoSuchTestMustNotExist` returned nonzero; MTP reported exit code 8. |
| Editor discovery and execution | C# Dev Kit automatically discovered both projects, facts, and all three component theory rows. Run All passed 21/21; running only the two-click theory row passed 1/1. |
| Editor debugging | Debug Test for `IndependentContextsKeepCounterStateIsolatedAsync` hit breakpoints at `CounterTests.cs:52` and `Counter.razor.cs:9`, then passed 1/1. Both temporary breakpoints were removed. |
| Editor gutter execution | Run Test from the gutter beside `IndependentContextsKeepCounterStateIsolatedAsync` passed 1/1. |
| Editor coverage | Run Tests with Coverage passed 21/21, populated Test Coverage, and displayed covered-line decorations on `Counter.razor.cs`. |

| Component behavior | Exact test evidence |
| --- | --- |
| Initial markup, status, and button | `CounterTests.RenderShowsInitialCountAndIncrementButtonAsync` uses semantic markup comparison. |
| One, two, and five clicks update the displayed status | `CounterTests.ClickingIncrementButtonUpdatesDisplayedCountAsync` (three theory rows) dispatches real Blazor click events and awaits the rendered assertion. |
| Component state is isolated | `CounterTests.IndependentContextsKeepCounterStateIsolatedAsync` verifies clicking in one context leaves the other at zero. |

### Shouldly migration acceptance

Verified on September 26, 2026: `dotnet build YHAB.slnx` completed with
0 warnings and 0 errors, and `dotnet test --solution YHAB.slnx --no-build`
completed with 21 passed, 0 failed, and 0 skipped. Both projects resolve Shouldly
4.3.0 and `xunit.v3.core.mtp-v2` 4.0.1, retain xUnit analyzers 2.1.0, and no longer
resolve `xunit.v3.assert`. All existing assertion calls were migrated to Shouldly,
including the component's semantic markup difference check. The parallel and MTP
runner settings are unchanged.

## References

- [Shouldly 4.3.0](https://www.nuget.org/packages/Shouldly/4.3.0)
- [Shouldly assertion documentation](https://docs.shouldly.org/)
- [xUnit framework and assertion package separation](https://xunit.net/docs/nuget-packages-v3)
- [bUnit project setup](https://bunit.dev/docs/getting-started/create-test-project.html)
- [bUnit 2.11.3 release](https://github.com/bUnit-dev/bUnit/releases/tag/v2.11.3)
- [bUnit context and rendering](https://bunit.dev/api/Bunit.BunitContext.html)
- [bUnit event dispatch and asynchronous rendering](https://bunit.dev/docs/interaction/trigger-event-handlers.html)
- [xUnit MTP setup](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform)
- [xUnit testconfig.json](https://xunit.net/docs/config-testconfig-json)
- [xUnit parallel execution and opt-outs](https://xunit.net/docs/running-tests-in-parallel)
- [.NET 10 dotnet test integration](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-dotnet-test)
- [MTP test-module concurrency](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-mtp#options)
- [Microsoft code coverage extension](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-extensions-code-coverage)
- [VS Code C# testing](https://code.visualstudio.com/docs/csharp/testing)
- [C# Dev Kit 3.40.210 manifest](https://ms-dotnettools.gallery.vsassets.io/_apis/public/gallery/publisher/ms-dotnettools/extension/csdevkit/3.40.210/assetbyname/Microsoft.VisualStudio.Code.Manifest)
