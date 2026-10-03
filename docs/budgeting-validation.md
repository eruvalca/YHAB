# Three-month household validation

Validation campaign: October 3, 2026. Synthetic activity covers January 1 through
March 31, 2026, including February's shorter month. The initial campaign changed
only tests and documentation. The subsequent BUG-01 investigation changes
workspace startup as described below. Earlier uncommitted budgeting fixes remain
part of the tested workspace.

## Repeat validation after the startup fix

Rerun on October 3, 2026, at approximately 01:09–01:11 America/Chicago, using
.NET SDK 10.0.401, Aspire 13.6.0, Docker 29.6.2, and the existing deterministic
household scenarios. All five projects ran together, with their existing parallel
settings and fresh disposable test resources. The full solution command passed
on its first attempt: **547 passed, 0 failed, 0 skipped**. MTP reported a test
duration of 1 minute 46 seconds. These are fresh results from one complete run,
not a consolidation of earlier passing cases or isolated retries.

| Suite | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Unit | 294 | 0 | 0 |
| Component | 237 | 0 | 0 |
| PostgreSQL integration | 8 | 0 | 0 |
| Aspire integration | 1 | 0 | 0 |
| Playwright | 7 | 0 | 0 |
| Total | 547 | 0 | 0 |

| Period checked | Calculation and PostgreSQL datasets | Browser dataset | Result |
| --- | ---: | ---: | --- |
| January, one month | 200 and 1,000 transactions | 200 transactions | Passed |
| January–February, two months | 400 and 2,000 transactions | 400 transactions | Passed |
| January–March, three months | 600 and 3,000 transactions | 600 transactions | Passed |

All 21 household unit cases, both persistence cases (each traversing all three
checkpoints), and the browser progression case passed. They rechecked the
independent financial totals below and the reconciliation, recurrence, split
validation, date-boundary, overspending, target-funding, mutation/history, and
owner/revision checks listed in the existing mechanism matrix. Browser checks
also passed register pagination/search/date filtering, reports, phone layout,
SSR, cold server interactivity, cached WebAssembly, and the uncaught-error guard.

No new application bugs were observed. **BUG-01 remains resolved** in this run.
**ENV-01 did not recur**, including when all five projects ran together; its
historical intermittent cause remains unconfirmed. This rerun changes only this
report. Existing production/test changes and real user data were preserved.

At 1,000 / 2,000 / 3,000 transactions, PostgreSQL read-plus-arithmetic checks took
28 / 53 / 28 ms, and the complete mutation probes took 2,622 / 3,891 / 2,468 ms.
Browser insertion of each successive 200-entry batch took 19.221 / 10.529 /
10.611 seconds; corresponding UI checks took 9.827 / 6.728 / 6.570 seconds.
These include test assertions and concurrent suite activity, and are observations
from this run rather than isolated benchmarks.

Evidence: `artifacts/household-repeat.log` and all five per-project TRX files
under `artifacts/household-repeat`. Browser trace:
`tests/YHAB.PlaywrightTests/bin/Debug/net10.0/TestResults/household-b3f0f65ef224405fbd8212993004cb99/trace.zip`.
Commands run from the repository root:

```powershell
dotnet test --solution YHAB.slnx --report-trx --results-directory artifacts/household-repeat
dotnet format YHAB.slnx --severity warn --no-restore
dotnet format YHAB.slnx --severity warn --verify-no-changes --no-restore
```

Documentation review checked `AGENTS.md`, the root README, `build/README.md`,
`tests/README.md`, and `docs/budgeting.md`. Their guidance remains accurate; only
this validation record needed updating.

## Scenario and independent expectations

The same household grows through three checkpoints. The household dataset adds
200 transactions each month; the amplified dataset adds 1,000. Amplification
divides the same purchase totals into more cent-exact purchases, so transaction
count changes without changing the expected financial results. This is a
correctness and volume exercise, not a concurrency load test or performance SLO.

Opening balances: checking $6,000, savings $8,000, everyday card -$600,
rewards card $0, tracked vehicle $15,000, and tracked auto loan -$12,000.
Monthly take-home pay is $8,400. Monthly assignments are $4,350, plus $600 in
January for the opening card debt.

Each month includes rent, utilities, cash and credit purchases, two split
purchases, a $50 categorized card refund, a $600 savings transfer, card payments,
a $250 loan payment, and $50 of manually entered tracking-loan interest.
Expected budget spending is independently totaled as:

| Category | Monthly spending | Monthly assignment |
| --- | ---: | ---: |
| Housing | $1,800 | $1,800 |
| Utilities | $270 | $300 |
| Groceries | $810 | $850 |
| Dining | $350 | $400 |
| Transport | $300 | $350 |
| Annual bills | $0 | $150 |
| Fun | $140 | $250 |
| Loan payments | $250 | $250 |
| Total | $3,920 | $4,350 |

The loan payment counts as budget spending because it leaves the budget for a
tracking account. Tracking-only interest changes net worth, not budget spending.
Card payments and the savings transfer do not count as additional spending.

| Checkpoint | Household / amplified entries | Checking | Savings | Ready to assign | Category availability | Net worth |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Jan 31 | 200 / 1,000 | $9,280 | $8,600 | $17,450 | $430 | $21,080 |
| Feb 28 | 400 / 2,000 | $13,160 | $9,200 | $21,500 | $860 | $25,760 |
| Mar 31 | 600 / 3,000 | $17,040 | $9,800 | $25,550 | $1,290 | $30,440 |

Both cards end each month at zero. Net income is $4,480 per month. Net worth
increases $4,680 per month because the tracking loan's principal falls by $200.
Expected values are literal reference totals, not outputs copied from the
application's budget/report calculators.

## Layers and mechanisms

The fixture in `tests/Scenarios` is linked into unit, persistence, and browser
test projects. Each run has independent mutable state.

| Requirement | Exact test method |
| --- | --- |
| Six account balances, envelopes, carryover, reserves, reports, ordering at all six checkpoints | `HouseholdProgressionTests.MonthlyLedgerMatchesIndependentTotals` |
| Reconciliation, transfer-side isolation, locked edit/delete rejection | `HouseholdProgressionTests.ReconciliationLocksOnlyTheSelectedSideAndRejectsUnapprovedAdjustments` |
| Jan 31 / Feb 28 / Mar 31 recurrence, catch-up, approval, duplicate prevention, report exclusion of templates | `HouseholdProgressionTests.RecurrenceCrossesShortMonthsWithoutDuplicateOccurrences` |
| One-cent split mismatch, future one-off rejection, inclusive report boundaries, refunds and transfer exclusion | `HouseholdProgressionTests.SplitValidationAndReportDateBoundariesRemainExact` |
| $100 cash / $70 credit overspending, next-month rollover and funding | `HouseholdProgressionTests.CashAndCreditOverspendingRollOverDifferentlyAndCanBeCovered` |
| Refill carryover, dated savings, hidden/snoozed exclusions and insufficient move denial | `HouseholdProgressionTests.TargetFundingHonorsCarryoverHiddenAndSnoozedCategories` |
| PostgreSQL round trips, money moves, undo/redo, branching, split edits, merges, reconciliation, owner/version guards | `HouseholdPersistenceTests.GrowingLedgerPreservesMathMutationsAndHistoryAtEveryMonthAsync` |
| Real authenticated HTTP writes, month navigation, register paging/search/date filters, SSR reports and mobile layout | `HouseholdBrowserTests.GrowingHouseholdLedgerMatchesBudgetRegisterAndReportsAsync` |

Persistence tests use disposable PostgreSQL containers with real migrations and
advance an injected clock through each month end. They bulk-load each cumulative
fixture checkpoint and reset fixture history, then use the actual revisioned
store for all behavior probes and reread through fresh contexts. Thus they check
3,000-row persistence and mutations, not 3,000 individual HTTP insertions.

The browser test creates an isolated Identity user and private plan in a
disposable Aspire application. All 600 household entries are submitted through
authenticated, antiforgery-protected HTTP mutations. Chromium checks the rendered
budget, register and reports at 200/400/600 entries, then checks the 600-entry
register at phone width. High-volume 1,000/2,000/3,000 checkpoints are covered by
the calculation and PostgreSQL layers. No live YNAB plans, development accounts,
or development databases are used.

## Findings log

No arithmetic or persistence defect was found in the completed calculation and PostgreSQL
checks. The independent monetary expectations passed at all six checkpoints,
including the additional rollover and target-funding cases.

| ID | Finding | Evidence / status |
| --- | --- | --- |
| BUG-01 | First opening of a 200-transaction plan produces uncaught Blazor connection errors. | Resolved. The complete persisted prerender snapshot exceeded SignalR's 32 KB incoming message limit. Workspace startup now loads its snapshot separately for each renderer. All seven browser tests pass, including cold server and cached WebAssembly checks. |
| ENV-01 | Browser validation can stall during Aspire startup in the combined run. | Three original browser cases timed out before UI checks. PostgreSQL was healthy while the web resource waited on migrations. Isolated retries and the complete follow-up browser suite passed. The intermittent startup cause remains unconfirmed; parallel settings and migration dependencies are unchanged. |

### BUG-01 reproduction and evidence

Run `HouseholdBrowserTests.GrowingHouseholdLedgerMatchesBudgetRegisterAndReportsAsync`
with the focused command below. It creates a fresh browser context, registers an
isolated user, creates six accounts and the household categories, submits the
first 200 January entries, then opens the budget workspace for the first time.
Expected: the workspace becomes interactive without uncaught page errors.
Before the fix: two Blazor connection-close errors appear. The errors are retained while
the test continues to all three checkpoints, then the final `errors.ShouldBeEmpty()`
fails in `tests/YHAB.PlaywrightTests/HouseholdBrowserTests.cs`.

In the completed trace, navigation to the first budget occurs at 34.560 seconds;
page errors occur at 35.654 and 41.240 seconds, before register navigation at
42.205 seconds. The earlier isolated trace also contains two identical errors.
This is not a teardown-only error or an arithmetic assertion failure. No data
loss was observed by these checks.

Local evidence: `artifacts/household-browser-final/YHAB.PlaywrightTests_net10.0_x64.trx`
and `tests/YHAB.PlaywrightTests/bin/Debug/net10.0/TestResults/household-bf81818b52ec4f6ca584175acd082bd9/trace.zip`.
The first reproduction is in the adjacent
`household-09cfd5c9d5604f70ac92027b09e473c5/trace.zip` directory. These ignored
artifacts may be removed by a clean; the test is the durable reproduction.

### BUG-01 investigation and fix

Temporary SignalR debug logging reproduced the failure and exposed the server
exception: `System.IO.InvalidDataException: The maximum message size of 32768B
was exceeded.` See `artifacts/circuit-diagnosis-retry.log` and the adjacent TRX
directory. All three financial checkpoints still passed, followed by the expected
browser-error assertion failure (0 passed, 1 failed, 0 skipped). The temporary
logging configuration was removed after diagnosis.

`Workspace.Snapshot` previously used `[PersistentState]`, serializing the entire
ledger into the prerender handoff. The initial server-circuit message therefore
grew with the transaction count and failed before the circuit could initialize.
This matches Microsoft's documented
[prerendered state size limitation](https://learn.microsoft.com/aspnet/core/blazor/fundamentals/signalr?view=aspnetcore-10.0#prerendered-state-size-and-signalr-message-size-limit).
The workspace now reads a fresh owner-scoped snapshot when each renderer starts.
SSR and InteractiveAuto remain enabled, and SignalR's default limit is unchanged.
The tradeoff is one additional plan read during the transition to interactivity.

Due recurring entries are posted after the interactive snapshot read completes,
rather than only on the first render. A delayed read can finish after that render;
the new component regression reproduced missed posting on both interactive
renderers before this adjustment (1 static case passed, 2 interactive cases
failed). SSR remains read-only. The component test also checks that the refreshed
snapshot's balance reaches the UI.

The browser regression now checks the SSR response, holds only the WebAssembly
runtime binary download until the first populated workspace works with the
server renderer, releases the download, waits for Auto's resource-cache marker,
and checks the WebAssembly renderer at the final checkpoint. The marker is read,
never fabricated; .NET 10 writes it after loading the runtime, as shown in
[the pinned framework source](https://github.com/dotnet/aspnetcore/blob/v10.0.12/src/Components/Web.JS/src/Services/WebRootComponentManager.ts#L144-L149).
Waiting before navigation avoids canceling background downloads. The test retains
all monetary/register/report/mobile assertions
and fails on any uncaught browser error. Holding the bootstrap JavaScript in the
first test-harness attempt prevented Auto from initializing; the corrected gate
allows bootstrap and delays only the runtime binary.

### Follow-up validation

The complete browser suite passes with the fix: 7 passed, 0 failed, 0 skipped.
`GrowingHouseholdLedgerMatchesBudgetRegisterAndReportsAsync` verifies SSR,
interactive server startup while the runtime binary is held, cached WebAssembly
startup, all 200/400/600 transaction checkpoints, and the mobile register with no
uncaught page errors. The strengthened test takes 1 minute 49 seconds in this run.
Local evidence is `artifacts/circuit-browser-final/YHAB.PlaywrightTests_net10.0_x64.trx`
and `tests/YHAB.PlaywrightTests/bin/Debug/net10.0/TestResults/household-0890fe5b5df541d8a1c624b97e3ee591/trace.zip`.

`WorkspaceTests.DelayedSnapshotPostsDueRecurringOnlyWhenInteractiveAsync` adds
three component cases for Static, Server, and WebAssembly renderer information.
They verify the pending-read state, no premature commands, interactive posting
with the correct revision/date, refreshed displayed balance, and no SSR writes.
These are lifecycle checks; the browser test proves the actual renderer handoff.

| Follow-up suite | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Unit | 294 | 0 | 0 |
| Component | 237 | 0 | 0 |
| Playwright | 7 | 0 | 0 |
| Total rerun | 538 | 0 | 0 |

The prior 8 PostgreSQL and 1 Aspire integration cases were not rerun for this UI
startup fix. They remain historical campaign evidence, not part of the 538-test
follow-up result. No persistent data model, migrations, financial calculations,
or infrastructure graph changed in this follow-up. Existing uncommitted work was
preserved. A diagnostic-only environment override initially prevented startup
because the connection string was missing; that override was removed before the
successful reproduction and does not form part of the fix.

Commands run from the repository root after the final code changes:

```powershell
dotnet build YHAB.slnx
dotnet test --project tests/YHAB.UnitTests/YHAB.UnitTests.csproj --no-build --report-trx --results-directory artifacts/circuit-unit
dotnet test --project tests/YHAB.ComponentTests/YHAB.ComponentTests.csproj --no-build --report-trx --results-directory artifacts/circuit-component
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --no-build --report-trx --results-directory artifacts/circuit-browser-final
dotnet format YHAB.slnx --severity warn --no-restore
dotnet format YHAB.slnx --severity warn --verify-no-changes --no-restore
```

The build has zero warnings/errors. Documentation was reviewed against
`AGENTS.md`, the root README, the build guide, and the test/feature guides. The
feature and test guides now explain separate snapshot reads, recurring-posting
timing, and cold/warm browser coverage. Setup and build conventions remain accurate.

### ENV-01 investigation

ENV-01 originates before the scenario's UI work, at `app.StartAsync` in
`tests/YHAB.PlaywrightTests/BudgetWorkflowTests.cs` and
`tests/YHAB.PlaywrightTests/HouseholdBrowserTests.cs`; shared app setup is in
`tests/YHAB.Testing/TestAppHost.cs`. Preserve resource startup logs if it recurs.
Run the focused commands below to distinguish startup failures from assertions
about application behavior. The new household case bounds startup separately
from its longer transaction-entry deadline.

Two test-harness issues were corrected without changing production behavior:
the initial overspending witness incorrectly treated a mixed cash/card category
as cash-only, and the first browser pagination selector also matched spans inside
Fluent buttons. The final overspending witness uses separate cash-only and card
categories; pagination targets the direct status span and checks that the next
page actually displays different data.

## Original campaign execution evidence

The initial focused run passed 15 unit cases and two PostgreSQL cases. Six more
unit cases cover overspending and target funding. Each PostgreSQL case traverses
all three monthly checkpoints; the final rerun also pins redo values and every
successful persisted revision increment.

The first full solution attempt ran 544 tests: 538 passed, six failed, zero
skipped. Three failures were the corrected overspending test expectation; the
other three were browser startup timeouts described above. A subsequent complete
unit run passed all 294 tests. The updated persistence scenarios passed 2/2.
The original regression run passed 234/234 component, 8/8 PostgreSQL, and 1/1
Aspire integration tests. The isolated household browser run completed all three
data checkpoints and phone-layout assertions, then failed only the accumulated
browser-error assertion (1 executed, 0 passed, 1 failed, 0 skipped). This is an
diagnostic failure for BUG-01 before the follow-up fix, not a passing browser suite.
The two existing budget browser cases passed on their separate retry (2 passed,
0 failed, 0 skipped), supporting classification of their earlier failures as
startup reliability problems rather than failed budget assertions.

Original campaign results for each distinct test case, combining the unchanged passing
cases with their relevant reruns:

| Layer | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Unit | 294 | 0 | 0 |
| Component | 234 | 0 | 0 |
| PostgreSQL integration | 8 | 0 | 0 |
| Aspire integration | 1 | 0 | 0 |
| Playwright | 6 | 1 | 0 |
| Total | 543 | 1 | 0 |

This is a consolidated result, not a claim that the original full-solution
command passed. Of 24 newly added scenario cases, 23 passed and the browser case
identified BUG-01. The unit checks and the two persistence tests verify all six
transaction-count checkpoints; one browser case traverses three checkpoints.

Final `dotnet build YHAB.slnx` succeeded with zero warnings and errors. Formatting
application and `dotnet format YHAB.slnx --severity warn --verify-no-changes --no-restore`
both exited zero. Documentation was reviewed against the repository instructions;
the existing test and feature guides now link this report. Setup, build rules,
agent conventions and production behavior required no changes for this campaign.

Observed PostgreSQL read-plus-arithmetic timings at 1,000 / 2,000 / 3,000 entries
were 55 / 83 / 100 ms. The complete mutation probe sequence took 6,045 / 6,817 /
4,239 ms respectively. These single-run measurements include database and test
assertion work, run alongside another test case, and are not benchmarks or
per-operation latency promises.

The completed browser walkthrough submitted each successive 200-entry batch in
26.540 / 20.419 / 26.524 seconds. Budget/register/report checks at 200 / 400 / 600
entries took 16.159 / 16.248 / 16.560 seconds. Those checks include navigation,
month-button clicks, assertions, and tracing; they are not page-load benchmarks.

Run from the repository root:

```powershell
dotnet build YHAB.slnx
dotnet test --solution YHAB.slnx --no-build --report-trx --results-directory artifacts/household-regression
dotnet test --project tests/YHAB.UnitTests/YHAB.UnitTests.csproj --report-trx --results-directory artifacts/household-unit-final
dotnet test --project tests/YHAB.IntegrationTests/YHAB.IntegrationTests.csproj --filter-class '*HouseholdPersistenceTests' --report-trx --results-directory artifacts/household-persistence-final
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --filter-class '*HouseholdBrowserTests' --report-trx --results-directory artifacts/household-browser-final
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --no-build --filter-class '*BudgetWorkflowTests' --report-trx --results-directory artifacts/household-existing-browser
dotnet format YHAB.slnx --severity warn
dotnet format YHAB.slnx --severity warn --verify-no-changes
```

Use `--no-build` only after a matching successful build with unchanged sources.
Raw TRX output and browser traces/screenshots are ignored local artifacts; the
durable findings and reproduction instructions belong here.
