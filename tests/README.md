# Testing

All five test projects target .NET 11 and use xUnit with native
Microsoft.Testing.Platform (MTP) integration. Open the repository root so both
the CLI and editor find `global.json` and `YHAB.slnx`.

| Project | Scope |
| --- | --- |
| `YHAB.UnitTests` | Budget arithmetic, monthly carryover, cash/credit overspending, payment reserves, targets, recurrence, transaction validation, plus Identity, service defaults, and architecture boundaries. |
| `YHAB.ComponentTests` | Calculator input events, month-specific assignments, invalid-input feedback, delayed workspace loading and interactive-only recurring posting, account workflows, navigation, framework-leakage copy, error page copy and request IDs, and passkey option gating, using bUnit. |
| `YHAB.IntegrationTests` | Real PostgreSQL: migrations and legacy data upgrades, Identity, private plan isolation, cross-plan foreign keys, optimistic concurrency, category merges, editable recurring occurrence dates, durable undo/redo, and deletion of owned ledger data. |
| `YHAB.AspireIntegrationTests` | The real AppHost, database/migration startup dependencies, readiness, resource endpoints, and cross-process HTTP behavior. |
| `YHAB.PlaywrightTests` | Real Chromium: Fluent controls, desktop/mobile navigation, persistent themes, drawer behavior, document continuity, framework-leakage routes and auth copy, axe heading-order/dark-mode contrast and input-size accessibility checks, manual plan/purchase/undo/report workflows, API authentication/antiforgery/revision guards, and passkey credential payloads. |

`YHAB.Testing` is a shared support library, not a test project. It configures
isolated AppHost builders for the Aspire and Playwright projects.

The deployment workflow checks formatting and tooling on every pull request and
`main` push. Pull-request changed paths select application build/tests,
PostgreSQL/Aspire checks, and deployment validation; documentation-only changes
skip that expensive work. Browser jobs are never path-selected or required.
Every `main` push runs non-browser validation and deploys to production; pull
requests do not deploy. The full non-performance browser suite and extended
performance cases run daily at 06:17 UTC and on demand in
[Nightly browser and performance validation](../.github/workflows/nightly-browser.yml).
`scripts/Get-ValidationScope.ps1` is used only by CI.
On its Ubuntu runner, it exports the test user's public HTTPS development
certificate, adds it to the system CA store and Chromium's NSS database, and
verifies both before testing. Only the public certificate is imported, using
NSS's `P,,` trust for this self-signed server certificate.
Creating the certificate alone does not trust it: Aspire's HTTPS readiness probes
then fail with `UntrustedRoot`, preventing every AppHost-backed test from starting.
Browser contexts accepting development certificates do not affect those probes.
They also do not bypass Chromium's service-worker certificate checks. System-only
trust lets Aspire start and ordinary pages load, but PWA worker registration
fails. Chromium 146+ defaults to `$HOME/.local/share/pki/nssdb`, retaining
`$HOME/.pki/nssdb` when that legacy database exists. The workflow handles both.
See [Chromium's Linux certificate guidance](https://chromium.googlesource.com/chromium/src/+/HEAD/docs/linux/cert_management.md).
PWA tests observe the application's own registration rejection and report it
directly instead of waiting only for a controller timeout; they do not register
a replacement worker on the application's behalf. Their browser contexts use
normal TLS validation so missing trust fails at navigation as well.

CI retains TRX results for seven days and uploads browser screenshots/traces on
failure. These diagnostics come from disposable test accounts and may include
their cookies and form data; keep them within the repository's artifact access
controls. Do not upload development/production browser profiles or Aspire
deployment/dashboard state. Local reports remain opt-in.

### Browser validation policy

Every application PR/main run retains all unit/component tests, the entire
PostgreSQL suite (including sustained competing-write/recurring-worker checks),
and Aspire startup/deployment checks. Browser coverage is deliberately opt-in:

- Changed paths, drafts, pull-request commits, `main` pushes, and merge groups
  never automatically select a browser job.
- Applying `run-full-browser` to a ready pull request runs the full Chromium
  suite excluding `Category=Performance`, without rerunning unrelated jobs. New
  commits also request it while the label remains. Removing the label stops
  future requested runs. The check is never required for merging.
- The `BrowserSmoke` trait remains available for focused local runs, not as a CI
  gate. Documentation-only changes skip application validation as before;
  unknown paths and PR file listings that reach GitHub's 3,000-file cap still
  conservatively select non-browser application validation.
- The nightly workflow runs the full non-performance suite and all
  `Category=Performance` cases daily. Failures on `main`, including failed
  performance regression assertions, create or comment on one rolling issue per
  suite. Issues link to the run and commit but do not copy diagnostic payloads;
  traces, screenshots, cookies, and form data remain access-controlled artifacts.
  Browser and performance test jobs have no issue-write permission.

Money conservation, receipts, concurrency, history rollback, migrations, owner
isolation, API guards, SSR-to-server-to-WASM handoff, PWA behavior,
desktop/mobile workflows, large-register page bounds and wide-catalog totals
retain their browser coverage through requested and nightly runs rather than
merge-blocking checks.

Only three browser cases carry `Category=Performance`: `ProcessLoadTests`,
`InspectorRetentionTests`, and
`HouseholdBrowserTests.ExtendedHouseholdEditingPreservesMoneyAndMemoryBoundsAsync`.
The ordinary household case still checks its three-month financial walkthrough,
10k/50k/100k registers and wide catalog, but omits the 120-cycle editing measurement.
The extended case repeats that correctness fixture and adds the original editing
and memory assertions; no measurement deadline or assertion is relaxed.
The full-browser composite and opt-in PR workflow exclude these three cases. The
[nightly workflow](../.github/workflows/nightly-browser.yml) runs only these cases
in its performance job, daily and on manual dispatch. A failed regression or
correctness assertion produces the same performance issue; the workflow does not
maintain a separate statistical baseline or trend history. Unfiltered local
solution runs still execute every test.

```powershell
# Native MTP/xUnit filters, after a matching Release build:
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --configuration Release --no-build --filter-trait Category=BrowserSmoke
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --configuration Release --no-build --filter-not-trait Category=Performance
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --configuration Release --no-build --filter-trait Category=Performance
```

Test-module concurrency remains bounded on each runner; do not speed up the suite
by overlapping resource-heavy stacks or measurement cases on the same machine.
NuGet caching avoids repeated package downloads, not test execution. The browser
jobs restore the Playwright Chromium download from the actions cache, keyed by
`Directory.Packages.props`, and run `install-deps --dry-run` before installing
missing operating-system packages. They stay on the host rather than in the
Playwright container because the tests start PostgreSQL containers and therefore
need Docker.

`ServiceDefaultsExtensionsTests` checks opt-in production readiness/liveness and
their default absence outside Development. `DeploymentMigrationsTests` uses Azure
SDK substitutes to check successful, failed, stopped, and canceled migration jobs;
these cases do not contact Azure. The existing AppHost startup test still exercises
local PostgreSQL and migration orchestration. `aspire publish` additionally validates
the production Bicep and Linux EF bundle. Those checks do not prove an Azure rollout;
the deployment workflow separately probes the deployed application's endpoints.

Signup regression checks verify immediate sign-in without generating confirmation
emails, while retaining unverified email state and landing on the plans page.
The shared browser registration
helper checks authenticated API access immediately after signup, clears cookies,
and checks a later password login without email confirmation. Recovery component
tests verify owner-contact guidance with either sender, including after an external
login fails to link and after an email-reset request for an unconfirmed account.
They also verify that legacy confirmation pages never expose tokens.
`AccountRecoveryCommandTests` covers invalid origins, missing accounts, and encoded
output without account writes.
`IdentityPersistenceTests.OwnerRecoveryResetsUnconfirmedAccountOnceWithoutChangingTwoFactorAsync`
uses disposable PostgreSQL and real Identity tokens to verify password replacement,
single-use reset links, security-stamp rotation, and preserved email/2FA state.

Cancellation regression checks cover required budget-client token contracts, HTTP
send/antiforgery-bootstrap cancellation, scoped Identity tokens and committed-write
follow-up, SSR request cancellation, component disposal, superseded register/month
reads, and uncertain-versus-confirmed workspace saves on timeout.
`HttpCancellationTests` hosts the real budget minimal endpoints on a test-owned
Kestrel loopback port with disposable PostgreSQL. It aborts an HTTP request while an
EF interceptor holds its query, verifies the database callback's token is canceled,
then verifies a fresh request succeeds. This checks HTTP-to-EF propagation, not the
time a proxy or PostgreSQL takes to interrupt an already-running SQL statement.
Existing background-posting checks cover cancellation releasing a database lock.
Component tests do not establish browser HTTP abort behavior or circuit-disconnect
semantics; browser workflows continue to cover server/WASM rendering and navigation.

The linked fixture under `tests/Scenarios` supplies identical synthetic household
data to unit, persistence, and browser tests without adding AppHost dependencies
to headless projects. The [three-month validation report](../docs/budgeting-validation.md)
records its independent expected totals, growing dataset checkpoints, layer
boundaries, results, and findings. Persistence checkpoints use disposable bulk
fixtures; browser entries use the authenticated API.

Foundation checks additionally cover deterministic recurrence batches, financial
ordering, sparse history patches, save/refresh recovery, and ten-year ledgers.
`WorkspaceProjectionTests` forces a write between navigation and month queries
and verifies that the combined workspace response retains one committed revision.
It also checks cached ownership, month selection, date validation and rejection
of stale writes. Component tests cover selected-month preservation, publishing
the complete response together, bounded read recovery and never resending a
committed command. Browser checks exercise the authenticated endpoint and its
no-cache response contract.
`HistoryAtomicityTests` injects EF save failures and synchronizes competing
revision claims; no production-only test hook is needed. `RegisterProjectionTests`
checks every sort cursor, transfer-side balances/status, literal search escaping,
owner checks and revision-keyed cache invalidation. `LedgerFoundationTests` seeds
10,000, 50,000 and 100,000 synthetic transactions through disposable PostgreSQL
fixtures, then exercises real queries, command services, history and competing
recurrence processors. In-process EF fixtures enable retries to match Aspire;
otherwise explicit transaction failures can be missed by integration tests.
`BudgetDatabase` owns its EF internal service provider and disposes it with the
fixture. Its stateful measurement interceptor is reused by that fixture's contexts,
not shared across tests or retained by EF's process-wide provider cache. Keep the
`ManyServiceProvidersCreatedWarning` enabled; do not suppress it to accommodate
additional measurement fixtures.

`MonthlyTransitionTests` exercises 80 simultaneous dated targets over ten years,
backdated corrections, credit reserves, and resuming without earlier transactions.
`MonthlyCheckpointTests` verifies persisted replay, invalidation on edits/undo,
stale/foreign publication, format upgrades and report-range equivalence.
It also checks SQL target aggregates, missing period-opening recovery, future
reservations and multi-year replay with different target cycles. Compare all
projection fields/rows by value: SQL decimal aggregates can spell zero as `0.0`
while equivalent in-memory arithmetic spells it as `0`.
`CheckpointPublicationTests` forces current-month edits between cold replay and
checkpoint publication without timing races. It checks exact financial results
and bounded current-month reads after publishing still-valid historical openings.
It also checks invalidation-boundary compaction, metadata changes, backdating,
undo/redo, financial resets, owner isolation, future revisions and cascading
deletion. `HistoryAtomicityTests` verifies boundary rollback with failed writes
and bounded storage under repeated edits. Run the 100k-entry measurement in
isolation.
`TargetPeriodTests` directly checks cycle boundaries; `MonthlyTransitionTests`
compares bounded funding inputs with complete-history calculations and literal
expected balances. `HistoryReadSetTests` verifies later account/category
dependencies prevent selective undo from deleting a referenced principal.
History checks exhaust all 50 retained changes, force undo/edit claims past the
same read, and inject a failed undo to verify ledger/cursor/receipt/checkpoint rollback.
`CommandIdentityTests` supplies immutable IDs and verifies repeatable transformations
and the maximum 128-occurrence/100-split batch.
`RecurringReadSetTests` checks a 1,001-occurrence history, edited actual dates,
overlapping template IDs in different plans, schedule reset/undo/redo, and bounded
identity reads. `BackgroundPostingTests` checks competing workers, empty batches,
owner isolation, stale user requests and cancellation releasing the plan lock.
`PayeePatchTests` exercises exact name matching, empty changes, validation, sparse
history formats and conflicting restores. `PayeePersistenceTests` verifies rollback
after the SQL update (including undo), receipt retries, competing renames, overlapping
IDs in another owner's plan, literal Unicode names, checkpoint retention and undo
after a recurring template advances. Compare ledger snapshots in stable identity
or sequence order; the full persistence read has no presentation-order contract.

`AccountLedgerFactsTests` exercises transfer direction, recurrence and date
exclusion, empty accounts and movement with precomputed balances.
`CheckpointPolicyTests` distinguishes financial invalidation from metadata edits,
including transaction/split ordering, old/new dates, target changes and zero-value
allocations. `AccountOperationTests` compares database aggregates with ledger
facts, checks overlapping foreign identities, verifies exact account-save
rejection and undo/redo results, and compares retained checkpoint contents with full replay.
For SQL round trips, compare every transaction and split field by value in a stable
order; decimal JSON spelling (`10` versus `10.00`) is not a financial difference.

`ReportTotalsTests` pins income, expenses, assets and debt across partial months,
opening dates, refunds, zero-net categories and closed accounts without mutating
inputs. `ReportProjectionTests` compares SQL aggregates with complete-history
calculations and independent household totals, including overlapping IDs in a
foreign plan, tracking transfers, recurring-template exclusion and a concurrent
write between report queries. `SelectiveSqlPlanTests` captures actual PostgreSQL
plans: report output cardinality is bounded by accounts/categories and requested
months, while underlying scans still process the relevant ledger history. Zero
EF entity materializations alone does not establish a bounded database scan.
The recurring SQL-plan case checks both per-plan and background discovery against
100k posted entries, then asserts exact occurrence counts, balances, revisions and
an empty worker pass. Keep row/filter bounds on the captured plans; elapsed-time
samples alone cannot detect a regression to scanning posted history.

`CommandBranchTests` covers account-side opening-date boundaries, category merging
with uncategorized income and payment-category rejection.
`SelectiveOperationScaleTests` measures every command type, retries, history, funding,
bulk operations and reports at 10k/50k/100k entries, plus catalogs up to 100 accounts,
500 categories and 120,000 allocations. Bulk payee checks include undo/redo and
history size at each threshold. EF materialization counters count entity instances,
not ordinary LINQ DTO projections; use persisted cardinality and SQL evidence for
those projections. The desktop/phone `BudgetWorkflowTests` also renames through the
static settings form, restores/reapplies through interactive history, and checks
balances and updated suggestions. `SustainedContentionTests` runs separate
service providers against one disposable database: 60-second offered-load windows
with 1/4/8 editors, competing recurring processors, and a register reader. The
paced case deliberately schedules two edits/second/editor; this timer models load,
not UI readiness. It verifies receipts for every completed edit, exact occurrence
dates/balances, history retention and no unexpected error logs. All due dates must
finish during the offered-load window, before editors stop; the quiet drain cannot
substitute for worker progress. This models replica
data contention, not independently deployed web servers or network latency.
Processors deliberately poll continuously for stress; the test does not model
the hosted worker's 30-second schedule or establish its catch-up deadline.
It samples test-process private bytes and working set once per second, with
allocation and GC-collection deltas over the load window. These include the
harness and may miss memory peaks between samples; they are not retained-heap
measurements or a long-running production soak test.

`ProcessLoadTests` exercises two actual Aspire web processes against one disposable
database, retaining the hosted workers' 30-second cadence. It discovers the local
replica endpoints and directs four authenticated writers to each; readings of the
Aspire launcher PID are not application-memory evidence, so it resolves and checks
each web executable before sampling. The process probe supports Windows and Linux.
Eight staggered writers offer two attempts/second each. A conflict is recorded as
a rejected attempt; it is not silently rebased or counted as a successful edit.
Initial month reads precede editing, as they do before the UI enables its inputs.
Accepted saves include the UI's combined balance and month refresh; refresh conflicts are
recorded separately from rejected writes. The load harness mirrors the workspace's
three-attempt read recovery, reporting recovered and exhausted refreshes separately.
The successful-display latency includes read retries and excludes exhausted
refreshes; the all-attempt latency includes both. Neither resends rejected writes.
Accepted month projections must match
the saved category amount and conserve the matching view's cash balance.
Receipts, exact assignments, recurring dates/balances and bounded history are
verified after the load. Each fixture starts with 100k posted transactions.

The default is one plan and 180 seconds of load followed by 60 seconds idle.
Set `YHAB_LOAD_PLANS=8` to measure independently edited plans and
`YHAB_LOAD_SECONDS=600` for a longer run (accepted range: 120–1800 seconds).
Run configurations in separate, sequential invocations so the runner's normal
parallelism does not overlap measurement cases. Use Release for process measurements:

```powershell
$env:YHAB_LOAD_SECONDS = '600'
$env:YHAB_LOAD_PLANS = '8'
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --configuration Release --filter-class '*ProcessLoadTests' --report-trx --results-directory TestResults/performance/process-load-eight
Remove-Item Env:YHAB_LOAD_SECONDS, Env:YHAB_LOAD_PLANS
```

Private bytes, working sets and CPU are sampled per web process every ten seconds
into the test's `process-load-*` artifacts, alongside current commit/conflict
counts. These exclude PostgreSQL, the load generator and unsampled peaks. The
independent plans currently use one authenticated owner; this is a data-partition
comparison, not a multi-user authentication or deployed multi-host benchmark.

Thresholds run sequentially within each test. For comparable observations, run
each measurement method alone, sequentially; full-suite concurrency is suitable
for correctness checks but distorts process-wide memory/timing observations.
The AppHost-backed browser scenarios use `BrowserAppHostDefinition`, which opts
out of parallel execution within the Playwright project. Each case starts a full
orchestrator, database, migration tooling and Chromium; overlapping those stacks
caused startup timeouts even with test-module concurrency limited to one, and
overlapping load/memory scenarios distort their observations. Each case still
owns and disposes fresh resources. Internal multi-process and concurrent-editor
checks remain concurrent, with all assertions and deadlines preserved.
If simultaneous database stress and browser/AppHost startup exhaust local
resources, run the solution with `--max-parallel-test-modules 1`. This serializes
test projects while preserving their `all` / `conservative` / `1x` settings;
report startup timeouts and the rerun separately instead of suppressing them.
`DatabaseProbe` records elapsed time, SQL command count/execution duration, entity
materialization counts, allocations, and end-of-operation process private bytes
and working set. SQL duration excludes subsequent reader consumption, and entity
counts exclude scalar/DTO/raw-SQL projections. Allocation deltas include background
work and probe overhead; process memory includes the test runner and earlier work.
Snapshots retained for later assertions can extend object lifetimes in the test.
None represents memory owned exclusively by a request.
EF's materialization interceptor itself adds per-entity property dictionaries and
delegates, so entity-counting runs overstate application allocation and latency.
Keep those probes for exact read-count regressions, and use
`DatabaseProbe.CommandsOnly()` for a separate timing/allocation control. An empty
materialization list in that mode means counting was disabled, not zero rows read.
`ColdReplayWithoutEntityCountingRetainsExactBalancesAsync` runs the narrow 100k
ledger, wide 10k ledger, and combined 100k/100-account/500-category/240-month shape
sequentially, asserting full month equivalence and independent cash conservation.
Optional EventPipe captures can isolate these intervals with the
`YHAB-Tests-Measurements` provider at Verbose level (5). Attach to MTP's actual test
worker, not its controller; a capture with no executed test is not validation.

The household browser test bulk-seeds its disposable AppHost at 10k/50k/100k entries,
then runs 120 paced enhanced-navigation/assignment cycles and opens a wide catalog.
Set `YHAB_BROWSER_CYCLES` to 120–1800 for a longer session; extended runs also
observe 60 seconds idle after navigation. `YHAB_MEMORY_SAMPLES` optionally names
an ignored local JSONL file containing live samples, including each Chromium
process's type/private bytes and DOM document/listener counts.
Snapshot tracing stops and its document closes before this measurement segment;
the functional walkthrough retains its trace and the last page has a screenshot.
`BrowserMemory` measures post-JS-GC heap/DOM, WebAssembly linear-memory capacity
through .NET's short-lived `localHeapViewU8()` runtime view, and private
bytes/working sets of Chromium processes reported by CDP. Linear capacity is not
live managed-object usage; process working-set sums can double-count shared pages,
and CDP can omit auxiliary processes. Diagnostic calls have bounded waits. These
observations do not establish leak freedom. The browser collection runs these
scenarios sequentially; the shared runner settings remain unchanged.
The probe records DOM counters after an initial collection and again after a
rendering opportunity and a second fixed collection, allowing weak cleanup to run.
It never loops collection until a growth assertion passes. Both observations are
retained in the optional JSONL output.
The repeated-navigation check verifies removed register controls are collectable
and DOM counts remain bounded after warm-up. Optional `YHAB_HEAP_SNAPSHOTS` names
a local diagnostic output directory for CDP snapshots at cycles 1, 12 and the final cycle,
with native allocation profiles where Chromium supplies them and brief
memory-infra allocator traces (8 MB trace-buffer bound, no screenshots);
leave it unset for routine measurements. Snapshots can contain page data and
belong in ignored local artifacts, never committed reports. The long-session DOM
growth check also captures a heap snapshot before failing when that option is set.
Allocator tracing can start a separate Chromium tracing-service process, listed
separately in memory samples. Use renderer-specific values when comparing those
runs with sessions that did not enable allocator diagnostics.
Keep the original failure and diagnostic rerun results separate; a passing rerun
does not by itself explain a different warm-up baseline or establish a memory plateau.
SQL seeding is fixture setup, not an application write API.

`InspectorRetentionTests` reuses the same `BrowserEditingSession` workload in a
test-owned persistent Chromium context to measure diagnostic retention. It derives
the executable and launch defaults from the installed Playwright browser, exposes
its debugging endpoint only on loopback, and disconnects/reconnects CDP after the
editing session. The document, workspace, .NET runtime, renderer process IDs and
financial state must survive; another real assignment must persist afterward.
The test does not reload the page or clear application caches during the control.
It uses the same cycle/sample/snapshot environment options, adds before/after
snapshots, and disposes the browser process and its temporary profile on failure
or success. Its private-memory observations distinguish diagnostic overhead from
application behavior; they are not a portable memory-drop threshold or proof of
an application memory plateau. Run this diagnostic case separately from other
performance workloads when comparing memory measurements.

Keep performance measurements descriptive, not machine-dependent pass/fail timing
thresholds. Assert exact financial totals, bounded page sizes/payloads, unchanged
unrelated records and storage effects. A projection compared with the same pure
calculator establishes consistency, not an independent mathematical oracle;
literal expectations and household conservation checks establish the latter.
The [validation report](../docs/budgeting-validation.md#foundation-hardening-and-large-ledgers)
records current line/branch coverage and its limits. Collect coverage explicitly
when changing these foundations, including compiler-generated lambdas in the
source-file totals; never equate executed lines with proven correctness.

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
verified October 2, 2026; all versions re-verified against the repository
manifests (`Directory.Packages.props`, `global.json`) and the extension baseline
on November 5, 2026; stack updated for the .NET 11 migration and follow-up
package upgrades on November 11, 2026, then migrated to Aspire 17 on
November 18, 2026:

| Component | Version |
| --- | --- |
| .NET SDK | 11.0.100 |
| bunit (component project only) | 2.15.2 |
| xunit.v3.core.mtp-v2 | 4.0.3 |
| Shouldly | 4.3.0 |
| TngTech.ArchUnitNET (unit project only) | 0.13.5 |
| NSubstitute (account tests) | 6.2.0 |
| NSubstitute.Analyzers.CSharp | 1.0.17 |
| Bogus (locally seeded fake data) | 35.6.5 |
| Microsoft.Testing.Platform / Platform.MSBuild | 2.5.3 |
| Microsoft.Testing.Extensions.TrxReport | 2.5.3 |
| Microsoft.Testing.Extensions.CodeCoverage | 18.12.0 |
| xunit.analyzers | 2.3.0 |
| Aspire.Hosting.Testing (matches AppHost) | 17.0.0 |
| Testcontainers.PostgreSql | 4.16.0 |
| Microsoft.Playwright | 1.64.1 |
| C# Dev Kit (stable baseline) | 3.40.210 |

Package versions are centralized in `Directory.Packages.props`. Explicit MTP
runtime and MSBuild references advance xUnit's transitive 2.5.1 dependencies to
2.5.3. Test package references use `PrivateAssets="all"`. All test projects
reference xUnit's core MTP package, which supplies the framework and runner
without `xunit.v3.assert`; xUnit analyzers remain included by the root build props.
NSubstitute usage is also checked by a test-only analyzer reference in the shared
build props; see [build rules](../build/README.md#analyzers) for enforcement and scope.

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

`global.json` selects native MTP mode. Do not add VSTest packages
(`Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`) or the legacy
`TestingPlatformDotnetTestSupport` bridge to these projects. bUnit provides the
component renderer and comparison tools; xUnit and MTP provide discovery and
execution, and Shouldly supplies general assertions. bUnit's `MarkupMatches`
is allowed for semantic HTML assertions as described below.

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
pwsh ./tests/YHAB.PlaywrightTests/bin/Debug/net11.0/playwright.ps1 install chromium
dotnet test --project tests/YHAB.IntegrationTests/YHAB.IntegrationTests.csproj
dotnet test --project tests/YHAB.AspireIntegrationTests/YHAB.AspireIntegrationTests.csproj
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj

# All five projects; requires the infrastructure/browser prerequisites below.
dotnet test --solution YHAB.slnx --no-build --max-parallel-test-modules 1 --report-trx --coverage --coverage-output-format cobertura --results-directory TestResults
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

### Architecture boundaries

The architecture policy has two enforcement points: MSBuild rejects forbidden
evaluated project references during builds, and ArchUnitNET rejects forbidden
compiled type dependencies during tests.

[`AssemblyDependencyTests`](YHAB.UnitTests/Architecture/AssemblyDependencyTests.cs)
loads SharedKernel, UI, and Client once. Shouldly reports the descriptions of any
violations. Subject selections must be nonempty, and forbidden targets include
referenced types from libraries not loaded as subjects. No xUnit assertion adapter
or production package reference is needed.

Both project-reference and compiled application-dependency checks enforce:

| Subject | Allowed dependencies on other YHAB assemblies | Additional restrictions |
| --- | --- | --- |
| `YHAB.SharedKernel` | None | No ASP.NET Core, JS interop, or Fluent UI types; no server infrastructure listed below. |
| `YHAB.UI` | `YHAB.SharedKernel` | No server infrastructure listed below. |
| `YHAB.Client` | `YHAB.UI`, `YHAB.SharedKernel` | No server infrastructure listed below. |

An assembly may use its own types. The application dependency rules recognize
`YHAB` and `YHAB.*` assembly names, so newly referenced application assemblies must
be deliberately allowed. The server-infrastructure restriction covers the
`Microsoft.EntityFrameworkCore`, `Microsoft.AspNetCore.Identity`, `Npgsql`, and
`Aspire` namespace trees. Shared UI can still use Blazor authorization APIs;
`Microsoft.AspNetCore.Components.Authorization` is not server Identity. These are
specific restrictions, not a complete guarantee of browser compatibility.

The build check also rejects unused references and references supplied through
imports or active conditions. Its sole exception is the existing Razor validator
at `build/YHAB.Build/YHAB.Build.csproj`, with `ReferenceOutputAssembly="false"`, in
Razor projects. This exception adds no application dependency. The target compares
normalized paths, leaves server/hosting references unrestricted, and does not impose
a NuGet package allowlist. See the [build policy](../build/README.md#architecture-checks)
and its `Test-ArchitectureProjectReferences.ps1` regression script.

[`ServerDependencyTests`](YHAB.UnitTests/Architecture/ServerDependencyTests.cs)
loads the server and enforces three additional design decisions:

- **Services own database access.** Direct use of EF Core, Npgsql, ADO.NET
  (`System.Data`), SQL Server client APIs, `ApplicationDbContext`, or
  `DbContext`-derived types visible to the compiled model belongs in
  `YHAB.Features.<Feature>.Services`/`Data`, shared `YHAB.Data`,
  `YHAB.Migrations`, or the actual `Program` entry point for startup/readiness
  wiring. Components are checked regardless of namespace; feature `Endpoints`
  namespaces are checked explicitly. Pages and HTTP handlers call services.
  Identity's `UserManager`/`SignInManager` APIs remain allowed. This does not
  require a repository layer or prohibit services from using EF directly.
- **Cross-feature dependencies use contracts.** Every top-level server feature,
  including new ones, may depend on another feature only through
  `YHAB.Features.<OtherFeature>.Contracts` and its descendants. Contracts must
  not depend on implementation types in any feature, including their own;
  exposing a service/entity through a contract would defeat the boundary.
  Introduce contracts only when a consumer needs them. There are currently no
  cross-feature contracts, so that additional check runs when they exist;
  feature discovery and each selected subject set must still be nonempty.
  Server-only contracts stay on the server. Common infrastructure and startup
  composition remain outside the feature-to-feature restriction.
- **Top-level features have no dependency cycles.** Each whole
  `YHAB.Features.<Feature>` subtree is explicitly assigned to one ArchUnitNET
  slice. Contracts count: `Account -> Budgeting -> Account` fails even if both
  edges use contracts. Cycles among internal namespaces such as a feature's
  `Models` and `Services` are allowed. Grouping is automatic, with no feature
  name allowlist to update when a feature is added.

Run only architecture checks with the repository's native MTP/xUnit syntax:

```powershell
dotnet test --project tests/YHAB.UnitTests/YHAB.UnitTests.csproj --configuration Debug --filter-class "YHAB.UnitTests.Architecture.*"
```

The normal unit suite includes them automatically. For application changes, CI
additionally runs this focused Debug check and exports its TRX report, then keeps
the Release build and functional test layers. Debug preserves dependencies that compilation
optimizations can remove. Compiled architecture violations fail tests and CI;
project-reference violations fail standalone builds with `YHABARCH001`.

For example, a shared component acquiring an EF `DbContext` dependency fails
the infrastructure rule with the dependent type in its failure description.
Move persistence behind a shared contract and keep its implementation on the
server. Do not bypass a failure with a skip, a blanket exclusion, or an empty
selection. If the dependency is intentional, review the boundary change and
update its narrow rule and rationale together.

The compiled checks see type signatures and method-body dependencies. They do not
inspect string-based reflection, runtime DI registrations, JavaScript, Razor render
modes, or database behavior. Endpoint classification follows the feature
`Endpoints` convention; an inline handler in `Program` remains inside its startup
exception and needs review. Shared infrastructure is trusted and could conceal an
indirect feature dependency, so these checks do not replace design review.
Project checks see the items evaluated for the current build/configuration, not
inactive configurations or references introduced later by custom execution targets.
They do not check `PackageReference` declarations or the entire transitive closure
of a third-party package. Compiler platform checks and runtime/component/integration
tests still matter.

Future policies, such as prohibiting persistence entities in HTTP payloads or
restricting contract types to framework-independent DTOs/interfaces, require a
separate design decision. Keep exceptions narrow and justified, and update the
documented boundary alongside its checks when intentionally changing the design.

See the [ArchUnitNET guide](https://archunitnet.readthedocs.io/en/latest/guide/)
for namespace, type, member, cycle, and diagram-based rules.

### Choosing an integration layer

Use **Testcontainers** when the subject is server code plus one real dependency:
PostgreSQL SQL translation, migrations, constraints, transactions, Identity stores,
or a service whose dependency injection you need to control. The initial test
applies real migrations, writes an Identity user, reads it in a fresh scope, and
checks normalized duplicate-name handling. Each test owns a disposable container
with dynamic ports, using `postgres:18.6` to match Aspire 17. Keep the resource
reaper enabled; do not use development connection strings or container reuse.
For future in-process HTTP tests, `WebApplicationFactory` can be combined with
these containers. This project does not verify AppHost wiring or browser behavior.

Use **Aspire integration tests** when the subject is the assembled application:
resource references, connection injection, migrations before web startup, readiness,
service discovery and HTTP contracts across process boundaries. These use
`Aspire.Hosting.Testing` 17.0.0, matching the AppHost rather than independently
upgrading its testing package. The initial test waits for the real web health
check and migration completion, then checks health, liveness, Home and Login.
Aspire starts actual processes, so test DI does not replace services in the web
process. Configure resources before `BuildAsync`; retain startup dependencies.

Use **Playwright** for browser-observable behavior: Fluent web components and
icons, responsive navigation, focus, forms, cookies and render-mode transitions.
Navigation tests run Home → Register → Home at desktop and mobile widths, check
that the drawer closes, verify theme persistence, check JavaScript errors/overflow,
and assert unchanged `performance.timeOrigin`.
Role-based locators only match what the accessibility tree exposes, and the plan
drawer renders its links inside a collapsed `<details>`. Open it first
(`OpenPlanNavigationAsync` does) before looking up one of its links by role, or the
lookup never resolves.
Keyboard and synthetic-touch reorders are gestures, not requests: Fluent only moves
a grabbed item, and a drag that misses leaves no drag row or placeholder. Wait for
the app's own gesture state (`aria-grabbed`, `.sortable-drag`) before the follow-up
action, and repeat a gesture at most once, only while the plan revision and the
saving status prove that nothing was applied.
Budget workflow tests register a
disposable user, create a plan, evaluate amount expressions, assign money, enter a
purchase, reload, undo with the keyboard, redo, and verify reports.
They also check API session, antiforgery, and stale-revision rejection. These tests
do not establish passkey ceremonies or every account workflow.

`PlanCatalogTests` exercises inline group/category creation and renaming, real
desktop drag-and-drop, phone move controls, and target editing across reloads.
At 1440 × 960 it checks that at least ten target-bearing categories fit in the
viewport, with category rows between 36 and 64 pixels tall. At phone width it
checks the collapsed plan navigation and document overflow. At desktop width with
fourteen accounts it scrolls the plan content and verifies the sidebar stays
pinned to the viewport top, keeps its accounts heading and balances visible,
scrolls its own longer account list, and brings a keyboard-focused account into
that scrollport without covering the sticky budget summary. With the site menu
expanded at 1024px, where the content wraps below the sidebar, it verifies the
sidebar stays in the page flow and a category row still receives the click a
pinned sidebar would intercept. At both 320px and
390px it also scrolls to the document bottom, verifies that sticky plan-page tabs
remain reachable with 40px targets and without horizontal overflow or covering the
sticky budget summary, then navigates to Reflect with the active-page state
updated; a component check keeps Transactions current on an account register. A
phone-sized touch
context on the WebAssembly renderer also moves categories by keyboard and by
Chromium touch input. It checks drag feedback, the optimistic order while a save
is held, and restoration after a rejected save. A rejected keyboard move checks
that focus returns to the moved category's source row rather than whatever row
occupies the destination index. A server-renderer case with delayed circuit
messages moves a category into an empty group by keyboard and verifies focus is
restored for the next keyboard move. `BudgetCatalogTests`
covers component commands, cancellation, collapse, selection, preservation of
existing category metadata, and open rename drafts across successful and
rejected reorders. `CatalogOrderingTests` covers destination
validation, ordering, and preservation of financial history; the persistence
case `ReorderingPersistsAtomicallyAndSupportsUndoRedoAsync` verifies revisions,
stale-write rejection, undo and redo against disposable PostgreSQL.

`PwaTests` checks the manifest/icon assets and install-button event handling at
desktop/mobile widths, first-install behavior, authenticated API access without
caching private responses, offline enhanced navigation and deep-link reloads,
retry after reconnection, and refusal to synthesize successful offline API GETs
or POSTs. It also installs a real replacement worker using a distinct script URL
on the test's isolated origin: an update waits, cancelling the confirmation keeps
the form, accepting reloads only that tab, and unrelated caches survive cleanup.
This avoids modifying shared build outputs during concurrent tests.
It asserts the **Add transaction** manifest shortcut and exercises
`/plans?action=add-transaction` in the browser: one accessible plan opens its
register with a single editor, several plans show a picker whose cards carry the
intent, closing and refreshing keep the editor closed, and a signed-out launch
resumes the same URL after login.
A deterministic event-ordering case loads the real app/module with a controlled
service-worker container: `updatefound` fires before `register()` resolves while
installation is still pending. Completing that installation must offer an update
only when the page already has a controller, preserving its document and inputs.

Run these checks with `dotnet test --project
tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --filter-class "*PwaTests"`.
The real-worker cases exercise the same worker used by published builds, through
the real AppHost.
Also validate a Release publish and its PWA assets. The synthetic installation
event checks our browser-event handling, not native OS installation UI. Manually
verify installation/launch, icon cropping, and upgrade behavior on target devices
and browsers (especially iOS/Safari) over a stable HTTPS deployment origin.

Browser layout checks verify that Fluent dropdown triggers have no nested native
border/padding, category links retain their native styling, desktop toolbar
controls align, and phone pages do not overflow. They also assert the stacked
budget table needs no horizontal scrolling, keeps the assignment field inside its
bounds at 390px and 320px, and keeps compact workspace controls at a 40px tap
height. Category selections must open their editor in the side pane above 1200px
and directly beneath the selected row below it, with the row still in view and
focus returned to it when the panel closes.
Editor dropdown checks include the shadow control's visible border, since its
minimum width can exceed the host without overflowing the document.
Phone editor checks run the transaction editor at 390px: the amount field must
request the numeric keyboard on Fluent's shadow control, editor fields must meet a
40px tap height, and the heading and action row must remain in the viewport while a
long form scrolls.
Budget checks use actual mouse-wheel input to reach the document bottom; locator
auto-scrolling and keyboard focus alone do not establish wheel scrolling works.
Sticky-summary checks assign the remaining money in the last category, verify
Ready to assign reaches zero while the summary stays visible, and check that
backward keyboard navigation does not place focus beneath the summary.
Desktop navigation checks verify reclaimed content width, keyboard restoration,
preference persistence through enhanced navigation and reloads, and mobile drawer
access after hiding the desktop menu.
Amount checks cover click/Tab selection, clearing to zero, replacing selected text,
preserved focus and input identity across saves, and stable formatting when zero
is cleared again. Component cases cover pending saves, rejected values, blank
normalization, no-op expressions without another save, comma-decimal keypad entry,
the sign control that replaces the missing keypad minus key, rejection of typed
negative results in positive-only fields, and text-mode fields that keep the full
keyboard. Account, transaction (including splits), move-money,
and target editors must save a typed amount when Save is clicked before the field
blurs; browser checks save the account and an edited transaction without Tab.
Component checks also verify that switching to a transfer between budget accounts
ignores hidden invalid split drafts and saves without categories. Category checks
verify that money leaving the budget excludes Ready to assign, shows the **Choose
a category** placeholder, and blocks saving until a spending category is chosen;
budget inflows still default to Ready to assign, and switching to an invalid
budget outflow moves focus to the inline message. Tracking-side transfer checks
cover both directions of the budget category requirement. Server checks reject
uncategorized budget outflows and zero amounts with specific messages.
Theme checks navigate between
SSR pages after selecting light, dark, and System preferences, verify the resolved
color scheme, and emulate OS changes in System mode. A reload alone does not
exercise Blazor's replacement of body attributes during enhanced navigation.
Opening account and transaction editors must focus a visible
heading; browser checks also reopen an entry from its register row. Editor draft
guards are checked headlessly through `EditorSurfaceTests` and the register, then
again in the browser: closing a dirty editor asks **Keep editing** or **Discard
changes**, Escape and Cancel follow the same guard, Enter saves from the payee or
amount, and a refresh keeps the draft unless a command caused the change or it
follows a save that committed without reloading. Those cases use
`EditorKeepsDraftsAndSavesWithEnterAsync`, because Fluent's debounced text binding
and the key event's DOM path are only exercised by a real browser. Wait for the
editor's heading focus before typing, and let Tab commit the field before pressing
Escape: a machine-speed escape can outrun the debounced binding that the guard
reads, while a person's hands never do. The draft-guard assertion reports the
editor, dialog, and focus state when the prompt never opens. Fluent also attaches a
text field's immediate listener from its own interop call after the editor renders,
so a fill that lands first never reaches .NET and the save writes nothing: wait for
the field to report that listener (Fluent marks the `fluent-text-input` host) before
typing, as `PlanCatalogTests.FillAsync` does. Fluent's `current-value` mirror and
the shadow control's own `value` update from the browser alone, so neither proves
the value arrived.

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
is shared. Most local HTTPS contexts ignore development certificate errors;
PWA contexts require a trusted certificate for both navigation and service workers.

Prerequisites: a running Linux-container Docker engine and available images for
all three projects; Aspire CLI/bundle and the .NET development HTTPS certificate
for Aspire/browser tests; the package-matched Chromium binary for Playwright.
On Linux, install Playwright OS dependencies with `playwright.ps1 install --with-deps chromium`
in the build agent image/setup. Also install `libnss3-tools` and trust the test
user's development certificate in both OpenSSL and Chromium NSS, using the
**Create and trust test HTTPS certificate** step in
[the deployment workflow](../.github/workflows/deploy.yml). Run the certificate
export and NSS commands as the same user that runs the tests; use `sudo` only
for package installation and the system CA store. Missing dependencies fail tests
rather than silently skipping them. Unit/bUnit projects still need none of this
infrastructure.

Playwright uses `Microsoft.Playwright` directly with our xUnit core MTP runner and
Shouldly. Do not add a runner integration that brings VSTest or xUnit assertions
back into the solution. Locator actions auto-wait; await observable readiness
with locators before asserting values with Shouldly. Do not use `WaitForTimeout`.
Plan links use enhanced navigation, so the outgoing page keeps its interactive
workspace, including shared actions such as "+ Add transaction", while the
replacement loads, and clicks are dropped until `enhancedload` fires. Wait for
that event (through `Blazor.addEventListener`) plus a destination-only locator
before interacting, rather than a bare `.workspace[data-interactive='true']`.
`InteractiveAuto` selects its runtime per document and does not migrate a running
Server component to WebAssembly (ASP.NET Core 11). A test that needs the
WebAssembly renderer must cache the bundle and then load a new document; a test
that needs Server must keep the cached bundle hash absent before each document
rather than blocking the runtime downloads, which leaves the client waiting.
Each navigation case attempts to retain `page.png` and `trace.zip` independently
in a unique `bin/<configuration>/net11.0/TestResults/navigation-*` directory.
Artifact capture is best effort: failures are reported through xUnit test output
and do not replace the original test failure or prevent the other capture attempt.
`BrowserArtifactsTests` checks closed-page and unwritable-directory failures;
its disposable outputs use `TestResults/artifact-capture-*`. Inspect the trace
with the generated `playwright.ps1 show-trace <path>` command. These artifacts are
ignored; do not commit traces containing cookies or future test account data.
Keep the existing parallel settings: test processes, ports, containers and browser
contexts must be isolated. On constrained CI agents, bound test-module concurrency
with `--max-parallel-test-modules 1`; this does not change test discovery or skip suites.
The explicit browser collection opt-out above also prevents complete AppHost
stacks from competing within the Playwright project.

References (reviewed October 2, 2026): [Aspire testing overview](https://aspire.dev/testing/overview/),
[AppHost lifecycle and isolation](https://aspire.dev/testing/manage-app-host/),
[Testcontainers PostgreSQL](https://dotnet.testcontainers.org/modules/postgres/),
and [Playwright .NET library](https://playwright.dev/dotnet/docs/library).

### Assertions

Use Shouldly for general assertions in all test projects. The stable version is
centrally pinned to 4.3.0 (verified against NuGet's live version feed on September
26, 2026); the newer 5.0 releases are previews. Import `Shouldly` in test files and
use APIs such as `ShouldBe`, `ShouldBeTrue`, `ShouldBeEmpty`,
`ShouldHaveSingleItem`, and `Should.Throw<T>`. xUnit attributes such as `[Fact]`,
`[Theory]`, and `[InlineData]` continue to define tests.

Do not add the xUnit assertion package or FluentAssertions. The test-scoped banned
API list reports their usage as `RS0030`; see the
[build policy](../build/README.md#banned-apis).

bUnit's `component.MarkupMatches(expectedMarkup)` is allowed for semantic HTML
assertions when its specialized differences and expected/actual markup make failures
easier to diagnose. `component.CompareTo(expectedMarkup).ShouldBeEmpty()` also
remains valid: bUnit performs the semantic comparison and Shouldly asserts that
there are no differences. Both preserve HTML-aware comparison; raw markup-string
equality can fail on insignificant whitespace or attribute ordering. No wholesale
migration between these two forms is required.

Prefer focused DOM assertions for text, attributes, and behavior when the entire
markup structure is not the contract. Use `WaitForAssertionAsync` around Shouldly
or `MarkupMatches` assertions that depend on an asynchronous render. See
[bUnit's markup guidance](https://bunit.dev/docs/verification/verify-markup).

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
  adapting to the developer machine or build agent. `YHAB.IntegrationTests`
  instead uses a fixed `2`: every test starts its own PostgreSQL container, and
  processor-count concurrency exhausted the Docker daemon on constrained
  machines. Keep that bound unless the container lifecycle changes.
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

Assert rendered DOM values with Shouldly; see [Assertions](#assertions) for semantic
HTML checks. Dispatch UI events with helpers such as `ClickAsync`, then use
`WaitForAssertionAsync` for assertions that depend on a render;
awaiting an event handler alone does not guarantee its
render cycle has finished. When a test must invoke a component's own callback
instead of a DOM event, such as a `FluentSelect`'s `ValueChanged` reached through
`FindComponents<T>()[0].Instance`, wrap it in `component.InvokeAsync(...)`:
bUnit's renderer rejects state changes made outside its dispatcher. Register
component dependencies in `context.Services`
before rendering and configure required calls on `context.JSInterop` (strict by
default). Keep tests in C# files; any future Razor test helpers must follow the
repository's matching code-behind policy.

bUnit tests run in process without starting Aspire, a web server, or a browser.
They verify component behavior, not browser layout, real JavaScript execution, or
server/WebAssembly render-mode transitions; those need browser-level tests.

In ASP.NET Core 11, `ValidationSummary` and `ValidationMessage` emit client-side
validation placeholder containers (`data-valmsg-summary`/`data-valmsg-for`) with a
`hidden` attribute even when no messages exist. Assert absence with `:not([hidden])`
(for example `.validation-message:not([hidden])`) rather than matching the class
alone.

Tests rendering Fluent components register `AddFluentUIComponents()` in their
own context. `ConfigureAccount` also registers these services and strictly stubs
the Fluent navigation module's initialize/dispose calls; the shell navigation
tests configure the same calls locally and verify native links remain keyboard
reachable without opting out of enhanced navigation. Account page tests render
the static pages with their Identity substitutes, verify that external-provider
markup appears only when a provider is configured, and assert the registration
destination. Passkey component tests verify the option renders hidden; the
browser module reveals it only when the browser supports passkeys and builds the
posted credential JSON, neither of which bUnit can execute. A browser case stubs
`navigator.credentials` with a password-manager-style credential whose `toJSON()`
omits `clientExtensionResults` and asserts the posted registration and assertion
payloads still carry a top-level `clientExtensionResults` property. Navigation text
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
   `Register.razor.cs`) to step through both.
5. Use **Run Tests with Coverage** to show coverage in the editor.

Keep automatic discovery and build-on-refresh/run enabled (the defaults). Keep
experimental source-only discovery (`dotnet.testWindow.discoverTestsFromSource`)
disabled. No `launch.json` or custom test protocol setting is required. In
particular, do not add the historical
`dotnet.testWindow.useTestingPlatformProtocol`: it is absent from the supported
stable extension's manifest.

## Historical acceptance records

The September 2026 acceptance runs, coverage expansion, and Shouldly migration
records are archived in the
[historical test acceptance log](../docs/test-acceptance-log.md). They record
then-current versions and counts and are not maintained as current guidance.

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
