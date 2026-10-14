# Tests

Read this guide before selecting, writing, or running tests, including validation
of changes made only in production code. Keep the root build, formatting, C#,
outcome, and cancellation requirements in force. Read the applicable production
guides from the root routing table for the behavior under test; a test directory
does not replace the instructions for the code it exercises. UI/bUnit/Playwright
work also reads the [UI guide](../src/YHAB.UI/AGENTS.md), account tests read the
[account guide](../src/YHAB/Features/Account/AGENTS.md) and its server ancestor,
and persistence/readiness tests read the [server guide](../src/YHAB/AGENTS.md).
Before Aspire/browser orchestration, read the [Aspire guide](../src/YHAB.AppHost/AGENTS.md).
Layer-specific setup and commands remain in the [test README](README.md).

- Run commands from the repository root so `global.json` selects the .NET SDK
  and native Microsoft.Testing.Platform (MTP) runner. Unit and bUnit tests need no
  external processes. Integration tests need Docker; Aspire integration and
  Playwright tests also need Aspire tooling, and Playwright needs its Chromium
  binary. See `tests/README.md` for setup and layer-specific commands.
- Use `YHAB.UnitTests` for server logic and `YHAB.ComponentTests` for
  Blazor rendering and interactions with bUnit. Run relevant tests while making
  changes, then run both projects before completing code changes. Use
  `YHAB.IntegrationTests` with Testcontainers for in-process persistence,
  migrations, SQL constraints and focused service/dependency behavior. Use
  `YHAB.AspireIntegrationTests` for the actual AppHost graph, migration startup,
  service discovery, readiness, and cross-process HTTP behavior. Use
  `YHAB.PlaywrightTests` for real browser navigation, Fluent web components,
  responsive layout, and interactive workflows. Run affected infrastructure suites;
  the full solution test command runs all five projects and requires their tools.
  Keep the full solution build requirement above.
- Use Shouldly for general assertions in all test projects. Keep xUnit for
  test discovery and execution; use `xunit.v3.core.mtp-v2` without the xUnit
  assertion package. Do not add xUnit `Assert` or FluentAssertions; `RS0030`
  enforces their API bans. bUnit's `MarkupMatches` is allowed for semantic HTML
  assertions when its markup-specific failure diagnostics are useful. Using
  `CompareTo(...).ShouldBeEmpty()` remains valid. Prefer focused DOM assertions
  when the entire markup structure is not the contract, and use
  `WaitForAssertionAsync` for assertions that depend on asynchronous rendering.
  Preserve exact exception checks with
  `Should.Throw<T>(action).ShouldBeOfType<T>()` when derived types must fail.
- Keep compiled architecture checks in `YHAB.UnitTests/Architecture`; see
  [architecture boundaries](README.md#architecture-boundaries) for their scope
  and focused Debug command. Include referenced types when selecting forbidden
  dependencies and require nonempty subject selections. Review intentional
  boundary changes with their rule and rationale rather than skipping checks.
  After changing project-reference policy, run
  `pwsh ./scripts/Test-ArchitectureProjectReferences.ps1` as well as the compiled
  checks; it exercises evaluated imports/conditions and the build failure hook.
- `dotnet test` restores and builds by default. Use `--no-build` only after the
  relevant code and tests have been built with the same configuration and no
  subsequent source changes. Keep coverage and reports opt-in for routine runs.
- For focused checks, use xUnit's MTP `--filter-class` or `--filter-method`
  options directly, without a `--` separator. Do not use VSTest's `--filter`
  expressions. Confirm that the selected test names and count match the intended
  scope; zero discovered or executed tests do not establish successful validation.
- Preserve `all` / `conservative` / `1x` parallel settings. Give each test its own
  mutable state; bUnit tests create and dispose a fresh `BunitContext` with
  `await using`. Shared fixtures must support concurrent access, and any opt-out
  from parallel execution needs a documented reason.
- Reuse `ConfigureAccount`, `CaptureLogs`, and the account form helpers when
  testing account components. Exercise rendered events; use `SetFormValue` and
  `SetInputValue` only to supply posted values normally provided by static SSR
  form mapping. Do not invoke private handlers or mutate cached private state
  just to increase coverage.
- Seed Bogus per instance with `UseSeed` or a locally assigned `Randomizer`;
  never set global `Randomizer.Seed`. Keep explicit values for boundaries,
  encoded tokens, and expected results.
- bUnit verifies component behavior, not real JavaScript, browser layout, or
  server/WebAssembly render-mode transitions. Those need browser validation;
  database persistence needs integration validation. Do not claim these are
  covered by the headless unit/component suites.
- Infrastructure tests own their resources. Testcontainers must use disposable
  containers and dynamic ports. Aspire/browser tests use `YHAB.Testing` to
  remove development volume mounts, retain migration/readiness dependencies,
  and use the testing builder's random ports. Dispose builders, apps, containers,
  browsers and contexts even on failure. Never target development databases or
  disable the resource reaper. Playwright uses the library API with Shouldly to
  preserve our MTP runner; wait for observable UI state instead of fixed sleeps.
  Missing prerequisites must fail with an actionable error, not silently skip.
- Report the commands run and actual passed, failed, and skipped counts. If
  validation is blocked, state the blocker rather than claiming success. See
  [tests/README.md](README.md) for runner details, reports, and conventions.

```powershell
# Run the relevant project during development.
dotnet test --project tests/YHAB.UnitTests/YHAB.UnitTests.csproj
dotnet test --project tests/YHAB.ComponentTests/YHAB.ComponentTests.csproj

# Example focused check.
dotnet test --project tests/YHAB.ComponentTests/YHAB.ComponentTests.csproj --filter-class "YHAB.ComponentTests.Features.Counter.Pages.CounterTests"

# Run all five projects when infrastructure/browser prerequisites are available.
dotnet test --solution YHAB.slnx
```
