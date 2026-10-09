# Three-month household validation

Validation campaign: October 3, 2026. Synthetic activity covers January 1 through
March 31, 2026, including February's shorter month. The initial campaign changed
only tests and documentation. The subsequent BUG-01 investigation changes
workspace startup as described below. Earlier uncommitted budgeting fixes remain
part of the tested workspace.

The foundation/performance sections are chronological. The
[latest cold-rebuild measurements](#cold-rebuild-profiling-and-combined-dimensions)
and [combined workspace load measurements](#consistent-workspace-refresh-investigation)
supersede the corresponding earlier measurements and remaining-work lists.
The final cross-layer regression gate passed all 880 tests; the
[foundation audit](#final-foundation-audit-and-validation) records the completed scope. The
[browser diagnostic control](#browser-diagnostic-retention-control) below explains
a substantial part of the observed native-memory growth.

Current status of the earlier remaining-work list:

| Area | Current evidence | Still open |
| --- | --- | --- |
| Selective loading | Sparse history/retries, checkpoint-based funding, SQL report aggregates and selective account/bulk operations replace the earlier full-ledger workflow paths. The isolated 100k fixture measured undo at 38.5 ms and funding at 50.5 ms. Cold profiling identified test-counter overhead and removed unnecessary single-split sorting allocations. | Cold replay still processes necessary history; reports still scan relevant database rows; bulk changes scale with affected records. The explicit full-plan snapshot endpoint retains its full-read contract, though the UI uses projections. Older entity-counted allocation figures include substantial harness overhead. |
| Other plan dimensions | Combined 100k transactions, 100 accounts, 500 categories, 240 months and 120k allocations passed full month equivalence and independent cash checks. The command-only Release control measured cold/warm replay at 956.8/27.2 ms, allocating 367.86/2.23 MB. | Stored openings and cold rebuild work still grow with history and category count. These workstation observations do not establish arbitrary-scale limits. |
| Coverage gaps | The saved `unit-cold-optimized` report has 100% line/branch coverage for `CatalogChanges`, `TransactionChanges` and the principal pure policies, including generated lambdas; 543 unit tests passed. | Coverage does not prove every financial combination or mutation resistance. |
| Sustained load and memory | Two independent Release web processes, eight editors and recurring workers were measured for ten minutes. The final combined workspace read accepted 9,535/9,599 writes, with zero refresh conflicts or exhausted refreshes and save/refresh HTTP p95 of 147.3 ms. Server private memory returned to about 160 MB per process. After 600 browser edit/navigation cycles, disconnecting diagnostics released about 223 MB of renderer private memory without replacing the live application. | Plan-wide revision checks still reject genuinely stale writes (0.7% in the final shared-plan run). WASM capacity is measured, not live managed-object usage; these short runs do not establish hours-long stability or deployment capacity. |

## Foundation hardening and large ledgers

Follow-up: October 3–4, 2026. The calculator stays pure; history patching, history
position/retention decisions, bounded recurrence planning, sequence allocation,
register validation and workspace save/refresh state now have direct deterministic
tests. Server integration checks cover behavior those pure tests cannot establish:
SQL ordering and filtering, constraints, atomic rollback, retries, owner isolation,
simultaneous writers, and scheduled-occurrence uniqueness. Browser checks retain
the existing desktop/phone workflow and populated SSR/Server/WebAssembly handoff.

The implementation and operational tradeoffs are maintained in the
[budgeting guide](budgeting.md), rather than duplicated here.

### Coverage evidence

Native MTP code coverage from the full unit project, with source-file totals
including nested classes and compiler-generated lambdas:

| Foundation | Covered lines | Covered branches |
| --- | ---: | ---: |
| Budget calculator | 222/222 (100%) | 180/180 (100%) |
| Targets | 52/52 (100%) | 36/36 (100%) |
| Reports | 38/38 (100%) | 52/52 (100%) |
| History patches | 79/79 (100%) | 106/106 (100%) |
| History policy | 3/3 (100%) | 6/6 (100%) |
| Recurrence planner and calendar | 41/41 (100%) | 24/24 (100%) |
| Transaction sequence policy | 17/17 (100%) | 14/14 (100%) |
| Workspace save/refresh state | 13/13 (100%) | 12/12 (100%) |
| Register query validation | 8/8 (100%) | 38/38 (100%) |
| Money commands | 100/100 (100%) | 119/122 (97.54%) |
| Catalog commands | 111/111 (100%) | 170/184 (92.39%) |
| Transaction commands | 161/161 (100%) | 235/254 (92.52%) |
| Amount expression evaluator | 99/100 (99%) | 40/40 (100%) |

The amount evaluator's uncovered line is the compile-time maximum-amount
constant declaration; its parser executes every line and branch. Zero-dollar
credit entries, reversed balance transfers, and payments from tracking accounts
exercise the calculator's final boundary branches. Financial expectations use
literal amounts and independent household formulas; projection/full-replay
comparisons separately check consistency. Coverage is evidence
of execution, not a mutation score or a guarantee that all financial rules are
correct. No exhaustive mutation campaign was run.

Assertion review paired exact amounts and literal boundary dates with structural
checks, rejection outcomes, unchanged-input checks, and observable persistence
effects. Representative protected regressions include ignoring sequence order,
off-by-one history retention, duplicate recurring posting, undo deleting later
automatic entries, saving again after a committed write whose refresh failed,
and leaking a cached month across owners. These are source-to-assertion checks,
not claimed experimentally measured mutation kills.

### Scale observations

The disposable PostgreSQL fixture spreads transactions over ten years and grows
to 10,000, 50,000 and 100,000 entries. Each threshold checks four sort modes,
two nonoverlapping 50-row pages, exact account totals, monthly projection/full
replay agreement, idempotent save retries, small assignment-history payloads,
undo preservation, owner denial and stale-revision rejection.

| Transactions | Catalog JSON | Two 50-row pages, four sort modes | Full ledger JSON for comparison |
| ---: | ---: | ---: | ---: |
| 10,000 | 3,715 bytes | 67–181 ms | 5,050,426 bytes |
| 50,000 | 3,716 bytes | 155–165 ms | 25,282,426 bytes |
| 100,000 | 3,716 bytes | 249–281 ms | 50,572,427 bytes |

These are local observations from one focused run with concurrent isolated
fixtures, not production latency guarantees or a benchmark of every plan shape.
The catalog contains one account and starter categories in this fixture; its
size is not constant as accounts, categories and allocations grow. Assignment
history remains below 2 KB at each threshold. The 100,000-entry full-read plus
two-calculation comparison took 2.16 seconds. A single SQL window calculation
replaced repeated per-row history sums; the earlier two-page measurement was
1.4–1.9 seconds at that threshold.

### Findings and resolutions

| Finding | Resolution and regression evidence |
| --- | --- |
| Full ledgers and full before/after snapshots multiplied transfer and history costs | Catalog/month/report projections, server register paging, selective mutation loads, sparse history patches; `LargePlansUseBoundedReadsAndSmallHistoryWithExactTotalsAsync`. |
| A response lost after commit could make a retry apply twice | Atomic operation receipts and retained client operation IDs; `InterruptedUndoRetriesTheOriginalOperationAndDoesNotSendParallelChangesAsync` plus the large-plan receipt checks. |
| Save success and refresh failure were indistinguishable | Pure save/refresh states with blocked editing and accurate messages; `CommittedUndoWithFailedRefreshDisablesEditingUntilRefreshAsync`. |
| Later automatic entries could be overwritten by snapshot undo | Patch conflict/reference checks and user-only history; `BackgroundPostingIsBoundedIdempotentAndPreservesUserUndoAsync`. |
| EF retry-enabled Aspire rejected explicit read transactions | Query reads now run inside EF execution strategies; integration fixtures enable retries and the browser suite passes. |
| Unordered split rows caused false undo conflicts and could shift rounding cents | Structural split equality and stable split-ID rounding order; `SplitStorageOrderDoesNotCreateFalseConflicts` and `SplitStorageOrderCannotMoveACentBetweenCashAndCreditFunding`. |
| PostgreSQL search escaping depended on an implicit escape character | Explicit escape character; `PagesFiltersAndTransferPerspectivesRetainExactFinancialOrderAsync` covers `%`, `_`, backslash, amounts and display labels. |
| An instruction without an explicit anchor could skip dates across bounded batches | Persist its original anchor before advancing its cursor; `BoundedCatchupEventuallyPostsEveryOccurrenceExactlyOnce`. |
| Delayed previous-plan reads and keyboard commands could race recovery | Request generations and operation guards; `LateReadFromPreviousPlanCannotReplaceCurrentPlanAsync` and `RefreshBlocksUndoUntilTheNewRevisionIsLoadedAsync`. |
| A failing recurring plan could abandon healthy plans in the same batch | Per-plan error logging/continuation; `PostingFailureDoesNotAbandonOtherPlansAndClosedAccountsStayPausedAsync`. |

Validation artifacts are under ignored `TestResults/foundations`. The final
solution build passed with zero warnings/errors. The full suite passed **716
tests: 438 unit, 252 component, 18 database integration, 1 Aspire integration,
and 7 browser cases**, with zero failures/skips. The separate unit coverage run
also passed all 438 cases. Formatting and its verification pass completed cleanly.

```powershell
dotnet build YHAB.slnx
dotnet test --solution YHAB.slnx --no-build --report-trx --results-directory TestResults/foundations/final
dotnet test --project tests/YHAB.UnitTests/YHAB.UnitTests.csproj --coverage --coverage-output-format cobertura --report-trx --results-directory TestResults/foundations/coverage-complete
dotnet test --project tests/YHAB.IntegrationTests/YHAB.IntegrationTests.csproj --filter-class '*LedgerFoundationTests' --report-trx --results-directory TestResults/foundations/scale-optimized
dotnet format YHAB.slnx --severity warn
dotnet format YHAB.slnx --severity warn --verify-no-changes
```

### Review regression follow-up

The three review findings were reproduced before fixing their production paths.
The original legacy-history test stored identical snapshots and therefore missed
conflicts in a real edit. Its replacement tests change memo/date values and also
verify that normalization still rejects a genuine later edit.

| Finding | Fix and regression evidence |
| --- | --- |
| Restoring a deleted sequence-zero transaction changed financial ordering and broke redo | Preserve the saved sequence for every restoration, including zero; `RestoringLegacyZeroSequenceDoesNotAllocateOrChangeOrder` and `DeletingMigratedTransactionCanUndoAndRedoWithoutChangingSequenceAsync`. |
| Migrated scheduled dates disagreed with older history snapshots | Normalize missing occurrence identities on both history sides before comparison; `MigrationBackfillsScheduledDatesAndLegacyHistoryCanStillBeRestoredAsync` and `LegacyOccurrenceCreationAndDeletionRestoreStableIdentityAsync` cover edits, creation, deletion and repeated undo/redo. |
| Transaction-free catalogs removed payee suggestions | Load the payee projection separately and refresh it with the catalog; `TransactionEditorsUsePayeeProjectionAndRefreshSuggestionsAsync` covers add/edit, off-page payees, case-insensitive deduplication and refresh. The desktop/phone workflow checks the suggestions attached to the native Fluent input. |

Follow-up validation passed **717 tests: 439 unit, 254 component, 22 PostgreSQL
integration and 2 affected browser cases**, with zero failures/skips. The other
five browser cases and standalone Aspire integration case were not rerun for
these fixes. Unit coverage retains 100% line and branch coverage for the
transaction-ordering policy, including its compiler-generated lambdas.
Artifacts are under ignored `TestResults/review-fixes`.

The first browser attempt failed because the new test selector matched both the
source datalist and Fluent's shadow-root copy. The corrected assertion reads the
native input's associated options and passes at both desktop and phone widths.

```powershell
dotnet build YHAB.slnx
dotnet test --project tests/YHAB.UnitTests/YHAB.UnitTests.csproj --no-build --coverage --coverage-output-format cobertura --report-trx --results-directory TestResults/review-fixes/unit
dotnet test --project tests/YHAB.ComponentTests/YHAB.ComponentTests.csproj --no-build --report-trx --results-directory TestResults/review-fixes/component
dotnet test --project tests/YHAB.IntegrationTests/YHAB.IntegrationTests.csproj --no-build --report-trx --results-directory TestResults/review-fixes/integration
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --filter-class '*BudgetWorkflowTests' --report-trx --results-directory TestResults/review-fixes/browser-final
dotnet format YHAB.slnx --severity warn
dotnet format YHAB.slnx --severity warn --verify-no-changes
```

### Remaining foundation work: transitions, checkpoints and deterministic commands

The engine now exposes a pure month transition with immutable opening state.
Command transformations consume supplied immutable IDs, allocated as GUID v7 at
the operation boundary. PostgreSQL stores monthly openings; same-transaction
invalidation covers edits, undo/redo and automatic posting. A revision-checked
publication lock prevents an old calculation from resurrecting invalid state.
Reports load their requested range with SQL-derived opening account balances.

| Requirement | Regression evidence |
| --- | --- |
| Exhaust retained history and reject further undo without writes | `ExhaustingRetainedHistoryRejectsAnotherUndoWithoutWritingAsync` checks all 50 reversals, exact amounts, cursor, unchanged snapshot and receipt count. |
| Undo/edit race after both read the same revision | `UndoAndEditAfterTheSameReadHaveExactlyOneWinnerAsync` checks one winner, one conflict, and consistent cursor/history/receipts for either outcome. |
| Failed undo is atomic | `FailedUndoRollsBackCursorLedgerRevisionHistoryAndReceiptAsync` checks ledger, cursor, revision, receipts, history and checkpoint rollback, then retries the same operation. |
| Text undo owns Ctrl/Cmd+Z | `ManualPlanPurchaseUndoAndReportsAgreeAsync` verifies native text undo, unprevented keyboard events and unchanged plan revision on desktop/phone, alongside normal plan undo. |
| Many targets and long history | `ManyDatedTargetsAndBackdatedCorrectionsPreserveTenYearsOfProgress` checks 80 dated targets across 120 months, independently calculated cents, untouched inputs and a backdated correction. |
| Deterministic commands and bounded identity allocation | `SuppliedIdentitiesMakeEveryCreatingCommandReplayExactly`, `MaximumRecurringBatchHasDistinctSuppliedTransactionAndSplitIdentities` and `MalformedCommandsReachValidationWithoutUnboundedIdentityAllocation`. |
| Resume calculations without earlier transactions | `TransitionCanResumeWithoutPriorTransactionsAndRetainsCreditAndCashSemantics` verifies cash, credit overspending/reserves and balances. |
| Persisted checkpoint and report correctness | `CheckpointsSurviveCurrentEditsAndRebuildBackdatedUndoAndCatalogChangesAsync` and `StaleOrForeignCheckpointPublicationCannotResurrectInvalidatedStateAsync` compare complete outputs; `OlderCheckpointFormatsAreIgnoredReplacedAndCascadeWithPlanDeletionAsync` covers rebuild/deletion. |
| Worker/user contention | `BackgroundPostingAndAssignmentContendWithoutLostMoneyOrOccurrencesAsync` forces both operations past the same read, then checks retries, 260 unique occurrences, $740 balance, $40 assignment and user-only history. |
| Scale, database work, allocations and browser memory | `LargePlansUseBoundedReadsAndSmallHistoryWithExactTotalsAsync` and `GrowingHouseholdLedgerMatchesBudgetRegisterAndReportsAsync` check 10k/50k/100k thresholds, bounded pages, exact totals and measured costs. |

The sequential scale run (`TestResults/remaining/scale-sequential`) observed:

| Entries | Cold month/checkpoint build | Assignment save | Resumed month after assignment | Save process allocations | Resumed process allocations |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 10,000 | 278.0 ms | 39.2 ms | 29.6 ms | 915,512 B | 1,457,216 B |
| 50,000 | 342.9 ms | 17.7 ms | 17.2 ms | 604,624 B | 1,427,336 B |
| 100,000 | 645.9 ms | 15.4 ms | 14.6 ms | 586,832 B | 1,421,704 B |

Every save used 12 SQL commands (8.4–10.3 ms aggregate execution) and no ledger
read; resumed months used 14 (8.4–10.2 ms). Cold reads allocated approximately
32.5/127.9/252.5 MB. These are process-wide allocation deltas, including runtime
background work, not request-attributed memory or retained memory. SQL execution
duration excludes subsequent reader materialization; elapsed time includes it.
No machine-dependent timing assertions were added. Initial concurrent fixtures
made allocation figures noisy, so the three thresholds now run sequentially
within one test without changing the runner's parallel settings.

Unit coverage (`TestResults/remaining/coverage-complete`) is 100% lines/branches
for `BudgetCalculator`, `TargetCalculator`, `CommandIds`, `MoneyChanges`,
`LedgerPatch`, `HistoryPolicy`, `TransactionOrdering`, `RecurrencePlanner`,
`WorkspaceOperation` and `RegisterPolicy`. `CatalogChanges` has 100% lines and
178/182 branches (97.80%); `TransactionChanges` has 100% lines and 244/248
branches (98.39%). Branch totals include compiler-generated lambdas. Remaining
partial compound/nullable branches are not claimed covered. Behavioral checks
and exact expectations remain more important than these percentages.

The final Chromium run measured post-GC JS heap after loading each register:

| Entries | Navigation to interactive register | JS heap | DOM nodes | Rendered transaction rows |
| ---: | ---: | ---: | ---: | ---: |
| 10,000 | 3,277 ms | 9,385,080 B | 19,792 | 50 |
| 50,000 | 3,215 ms | 9,181,456 B | 19,792 | 50 |
| 100,000 | 4,107 ms | 8,679,056 B | 18,073 | 50 |

This includes document navigation and InteractiveAuto startup, not just the API
request. Heap/DOM measurements use Chromium's diagnostic protocol after garbage
collection. They do not establish the absence of leaks or measure WebAssembly
linear memory. The 200/400/600-entry walkthrough also verifies cold server
rendering, cached WebAssembly rendering, monthly math, filtering and reports.

The first scale probe exposed two unnecessary empty ledger/split queries during
assignments; these are now skipped. A full-suite browser run exposed a test
synchronization error: the workspace's interactive marker appears before the
register's asynchronous data load finishes. The test now waits for the actual
transaction count instead of treating that marker as data readiness.

Final validation passed **749 tests: 460 unit, 254 component, 27 PostgreSQL
integration, 1 Aspire integration and 7 browser cases**, zero failures/skips.
The separate unit coverage run also passed all 460 tests. The solution build
completed with zero warnings/errors. Artifacts are under ignored
`TestResults/remaining`; the final full-suite results are in `final-verified`.
Formatting and the final no-changes verification both passed.

```powershell
dotnet build YHAB.slnx
dotnet test --solution YHAB.slnx --no-build --report-trx --results-directory TestResults/remaining/final-verified
dotnet test --project tests/YHAB.UnitTests/YHAB.UnitTests.csproj --no-build --coverage --coverage-output-format cobertura --report-trx --results-directory TestResults/remaining/coverage-complete
dotnet test --project tests/YHAB.IntegrationTests/YHAB.IntegrationTests.csproj --filter-method '*LargePlansUseBoundedReadsAndSmallHistoryWithExactTotalsAsync' --report-trx --results-directory TestResults/remaining/scale-sequential
dotnet format YHAB.slnx --severity warn
dotnet format YHAB.slnx --severity warn --verify-no-changes
```

Undo/redo, funding operations and some bulk commands still load a full ledger;
catalogs still include all allocations, and checkpoint reading grows with months
and categories. Those are explicit remaining scale dimensions, not failures at
the tested thresholds. The cold first calculation still materializes history
through the selected month. The browser heap observations cover Chromium JS,
not WebAssembly linear memory or total browser-process memory.

### Command branch closure and scale measurements (October 4, 2026)

This follow-up supersedes the remaining coverage gaps and measurement limitations
in the preceding acceptance record. It adds nine command boundary cases and
measurements for every command type, wide catalogs, competing replicas and browser
memory. Production logic only unwraps an already-validated account ID before
comparing it inside the clearing predicate; it removes unreachable nullable
branches without changing the account-selection rule.

The isolated unit coverage run passed **469 tests, zero failures/skips**:

| Source | Covered lines | Covered branches, including generated lambdas |
| --- | ---: | ---: |
| `CatalogChanges` | 111/111 | 182/182 |
| `TransactionChanges` | 163/163 | 244/244 |

`CommandBranchTests` checks accepted opening-date boundaries and next-day rejection
from both transfer perspectives, unrelated entries, merging with uncategorized
income, payment-category rejection, and all scoped/global approval combinations.
It checks exact outcomes and unchanged inputs. These percentages do not establish
mutation resistance or prove every combination correct.

#### Selective loading across operations

`SelectiveAndFullLedgerOperationsRetainExactResultsAtEveryThresholdAsync` runs
sequential disposable plans at 10k, 50k and 100k transactions, with 10 accounts,
50 categories, ten years and 6,000 allocations each. Setup uses parameterized
synthetic SQL (checking accounts and one split per original entry); measured
commands use the real service and database. Different transfer/credit/split mixes
can have different costs. It checks
exact assignments, funding, reversals, state changes, balances, report ending
net worth, receipts and recurring occurrence dates. A new overdue daily template
posts its first 128 occurrences on save, then two batches complete all 365.

At 100k entries, the passing isolated run observed:

| Operation | Elapsed ms | Transaction entities read | SQL commands / execution ms | Process allocations MB |
| --- | ---: | ---: | ---: | ---: |
| Plan settings | 170 | 0 | 11 / 12.1 | 65.6 |
| Group edit | 80 | 0 | 11 / 9.4 | 61.4 |
| Category edit | 82 | 0 | 12 / 9.6 | 61.4 |
| Account edit | 590 | 10,000 | 14 / 16.5 | 423.6 |
| Assignment | 84 | 0 | 12 / 10.9 | 61.5 |
| Retry completed assignment | 3,463 | 100,000 | 9 / 26.6 | 1,307.6 |
| Undo | 4,738 | 100,000 | 13 / 30.0 | 3,531.1 |
| Redo | 5,044 | 100,000 | 13 / 27.0 | 3,529.5 |
| Move money | 4,861 | 100,000 | 14 / 27.4 | 3,793.8 |
| Fund targets | 5,274 | 100,000 | 14 / 30.0 | 3,796.5 |
| Rename payee (90,000 matches) | 16,507 | 100,000 | 103 / 8,078.9 | 4,754.0 |
| Clear 50 entries | 175 | 50 | 14 / 20.4 | 65.2 |
| Edit one entry | 608 | 1 | 14 / 487.9 | 63.0 |
| Delete 50 entries | 97 | 50 | 14 / 18.4 | 64.5 |
| Reconcile one account | 644 | 9,995 | 14 / 20.4 | 426.6 |
| Merge one category | 421 | 1,999 | 16 / 165.3 | 165.7 |
| Two-year report | 828 | 18,928 | 9 / 47.9 | 220.5 |
| Create overdue recurring template | 448 | 0 | 14 / 346.7 | 66.6 |
| Recurring batch 2 | 244 | 129 | 12 / 109.0 | 68.5 |
| Recurring batch 3 | 201 | 257 | 12 / 106.0 | 70.6 |

Transactions here mean EF entities, not all rows examined by PostgreSQL. Splits
match those counts except the report's one splitless reconciliation adjustment.
Every operation also reads the catalog and all allocations (6,000, or 5,880 after
the category merge). Reports return just 7,203 serialized bytes at this threshold.
Recurring batches read prior occurrences of the selected template, so their read
cost grows with template history even though each batch creates at most 128.

| Entries | Assignment ms | Receipt retry ms | Undo ms | Fund targets ms | Rename payee ms |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 10,000 | 339 | 1,171 | 2,338 | 985 | 1,496 |
| 50,000 | 83 | 1,627 | 2,396 | 2,819 | 6,934 |
| 100,000 | 84 | 3,463 | 4,738 | 5,274 | 16,507 |

These are single sequential Debug observations, including first-use JIT/cache
effects, not a benchmark distribution or production SLA. End-of-operation
private memory at 100k ranged from 456 to 991 MB and working set from 580 to
1,105 MB; it includes earlier operations, the test host, and snapshots retained
for later assertions. MB/GB in these tables use decimal units. The several GB in
the allocation column represent cumulative allocation churn, **not retained GB**.
SQL duration excludes subsequent reader materialization. Raw per-operation
counts and memory samples are in `TestResults/scale-hardening/operations-complete`.

#### Wide catalogs and long allocation history

`WideCatalogsAndLongAllocationHistoriesRetainExactBalancesAsync` holds transaction
count at 10k and increases independent catalog dimensions. It asserts exact cash
conservation before/after assignment, current assigned amounts, no transaction
entities loaded for assignment, and persisted checkpoint counts.

| Accounts / categories / months | Allocations | Catalog JSON bytes | Cold month ms | Assignment ms / allocated MB | Resumed month ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| 10 / 50 / 60 | 3,000 | 334,809 | 720 | 388 / 37.4 | 57 |
| 50 / 200 / 120 | 24,000 | 2,601,497 | 1,238 | 2,425 / 251.6 | 306 |
| 100 / 500 / 240 | 120,000 | 12,794,907 | 3,098 | 2,812 / 1,207.7 | 1,320 |

The largest assignment ends at 266 MB private/391 MB working set. Its 1,208 MB
allocation delta is again churn, not resident memory. The resumed month loads
all 240 previous checkpoint entities, all 120k allocations and only two current
transactions; checkpoints occupy 6,963,541 JSON characters in 241 openings.
This isolates allocation/checkpoint growth from transaction-count growth.
Evidence: `TestResults/scale-hardening/wide-final`.

#### Sustained competing writers and recurring processors

`SustainedReplicasPreserveEveryUserWriteAndRecurringOccurrenceAsync` uses three
10k-entry plans, independent DI/cache/context graphs per editor/processor/reader,
and one shared disposable PostgreSQL database. Each plan has four templates with
365 daily occurrences due. Each offered-load window lasts 60 seconds, followed
by a measured backlog drain. A reader continuously checks unique 50-row pages.
Processors run in a tight loop to stress revision contention; this deliberately
does **not** reproduce the hosted worker's 30-second polling cadence. The paced
editors target two edits/second each, synchronized at the start (a bursty load).

| Editors / processors | User commits/s | User conflict rate | Completion p50 / p95 / max ms | Worker commits during load | Templates still due / drain seconds |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1 / 1, continuous | 40.9 | 0.0% | 21 / 41 / 271 | 24 | 4 / 0.73 |
| 4 / 2, continuous | 70.9 | 25.1% | 39 / 74 / 3,474 | 2 | 12 / 2.58 |
| 8 / 3, continuous | 38.8 | 62.5% | 69 / 889 / 9,134 | 1 | 12 / 1.95 |
| 8 / 3, paced | 16.0 | 47.2% | 85 / 141 / 271 | 36 | 0 / 0.00 |

All **10,005 successful user writes** across the four windows have durable
operation receipts and exact final assignments. Plan revisions equal successful
user plus recurring commits, user history remains capped at 50, and every
scenario finishes with exactly 4,380 occurrences, correct dates, unique sequence
numbers and the expected balances. No unexpected error logs were captured.
Conflicts are explicit expected outcomes and are retried with the same operation
ID; they are not counted as successful writes. Completion latency includes retries.

| Load | Private memory start / sampled peak / end MB | Working set peak MB | Allocated GB over window | Gen 0 / 1 / 2 collections |
| --- | ---: | ---: | ---: | ---: |
| 1 / 1 | 54.1 / 82.4 / 70.9 | 205.2 | 22.1 | 1,779 / 1,167 / 11 |
| 4 / 2 | 96.4 / 96.5 / 91.8 | 226.0 | 36.4 | 3,199 / 1,098 / 250 |
| 8 / 3 | 96.4 / 107.5 / 101.0 | 241.0 | 28.2 | 2,595 / 1,027 / 297 |
| 8 / 3 paced | 96.5 / 107.2 / 95.2 | 242.5 | 14.5 | 1,262 / 310 / 76 |

These are test-process samples once per second and cumulative allocation deltas;
they include the reader, tight processor polls, receipt/latency recording and GC.
They exclude PostgreSQL's process and may miss peaks between samples. The paced
case makes 42,171 processor polls after/between successful batches, so its churn
is not an estimate of normal hosted-worker consumption. This is a minute-scale
contention experiment, not a deployed multi-host/network test or hours-long soak.
Evidence: `TestResults/scale-hardening/contention-final`.

Continuous editing can starve recurring work even with one editor on one plan;
more workers alone do not solve it. When offered writes are paced, all due work
finishes during the window. No financial data loss was found, but bounded retry
backoff/jitter and eventual progress for a repeatedly conflicted plan need design
and regression coverage before promising a posting deadline under sustained edits.

#### Browser memory and an extended editing session

`GrowingHouseholdLedgerMatchesBudgetRegisterAndReportsAsync` passes the realistic
200/400/600-entry workflow, cold Server and cached WebAssembly rendering, register
thresholds, 60 navigation/assignment cycles, and a wide-catalog plan. The trace is
saved and its page closed before measuring a fresh page without snapshot tracing.
The 60 cycles use enhanced navigation without changing `performance.timeOrigin`,
wait for the refreshed monthly Assigned total and input, and preserve 50-row pages.
The load timer targets one cycle/second; the measured session took 65.7 seconds.
No page JavaScript errors were captured.

| Register entries | Interactive navigation ms | Post-JS-GC heap MB | WASM capacity MB | DOM nodes | Chromium private / working set MB |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 10,000 | 1,590 | 7.02 | 125.70 | 12,011 | 378.9 / 526.5 |
| 50,000 | 1,619 | 7.00 | 125.70 | 12,011 | 390.9 / 538.9 |
| 100,000 | 1,707 | 7.00 | 125.70 | 12,011 | 398.5 / 546.1 |

| Edit/navigation cycle at 100k | Post-JS-GC heap MB | WASM capacity MB | DOM nodes | Chromium private / working set MB |
| ---: | ---: | ---: | ---: | ---: |
| 1 | 8.94 | 125.70 | 21,765 | 411.9 / 588.9 |
| 6 | 13.87 | 125.70 | 57,130 | 455.1 / 638.7 |
| 12 | 19.37 | 125.70 | 97,124 | 483.8 / 671.9 |
| 30 | 35.68 | 150.86 | 226,702 | 631.2 / 802.3 |
| 60 | 60.71 | 150.86 | 438,622 | 825.9 / 997.4 |

The bounded register scales well by transaction count, but repeated navigation
shows persistent growth over this window even after JavaScript GC and with trace
snapshots disabled. This is an actionable retention investigation, **not proof
of an unbounded leak or attribution to Fluent/Blazor/application code**. Browser
GC does not force .NET GC, and these samples do not identify retaining roots.
Profile component/interop lifetime and detached DOM retainers before choosing a
fix; require a longer repeated-navigation regression to demonstrate a plateau.

The 100-account, 500-category, 120k-allocation budget takes 10,854 ms to become
interactive, renders all 500 amount inputs, and correctly shows $99,880,000 Ready
to assign. It has 14.03 MB JS heap, 181.08 MB WASM capacity, 55,510 DOM nodes and
648.0 MB Chromium private memory after a new-document navigation. This fixture
has no transactions; it isolates the catalog/rendering dimension from ledger
size. Browser process totals still reflect earlier work in the same browser.

The memory probe uses bounded CDP heap/DOM/process calls and the byte length of
.NET's short-lived `localHeapViewU8()` view. Linear capacity is not live managed
heap usage. CDP lists four processes in this run; auxiliary processes it omits
are outside the sample, and working-set sums may double-count shared pages.
These workstation runs are not on a dedicated performance host; unrelated
machine activity, JIT, caches and Debug instrumentation affect elapsed times.
Evidence: `TestResults/scale-hardening/browser-final60`.

#### Findings and next implementation order

1. **Investigate navigation retention.** Collect retaining paths for the growing
   DOM/JS objects and distinguish delayed managed collection from undisposed
   subscriptions/components. Establish a plateau after the eventual fix.
2. **Bound allocations and checkpoint reads.** Load the relevant month/category
   allocations and nearest valid opening plus needed target history. The 12.8 MB
   catalog and 1.2 GB assignment allocation delta show that transaction paging
   alone does not solve large-plan costs. Preserve backdated invalidation tests;
   then remeasure the 500-row budget before deciding on category virtualization.
3. **Remove full-ledger history/funding/retry loads.** Load affected patch entities
   for undo/redo; use the monthly projection for money moves/target funding;
   return an appropriate receipt/revision result without reconstructing an entire
   ledger. Keep atomic history/cursor/receipt and retry semantics covered.
4. **Inspect selective SQL plans and bulk writes.** One-entry edit materializes
   one transaction but spends 488 ms executing SQL at 100k. Use `EXPLAIN ANALYZE`
   to identify the responsible query before changing indexes. Restrict rename
   reads to matches and optimize the actual 90k-row write/history cost. Recurring
   reads also need a bounded occurrence-identity lookup as template history grows.
5. **Improve contention fairness, then repeat a deployment soak.** Measure
   backoff/jitter and guaranteed eventual worker progress without weakening
   optimistic concurrency. Repeat with the real 30-second worker schedule,
   independently deployed web processes, production build settings and a longer
   duration. Current financial/atomicity checks pass; timing and memory findings
   remain open optimization work rather than claimed fixes.

| Requested validation | Exact regression evidence |
| --- | --- |
| Remaining catalog branches | `OpeningDateIgnoresUnrelatedEntriesButChecksBothTransferSides`; `UsedCategoryNeedsSpendingReplacementAndPreservesUnassignedIncome` |
| Remaining transaction branches | `PaymentCategoryCannotBeUsedAsASpendingSplit`; `UnassignedInflowCanBeSavedButCannotBeClearedFromAnotherAccount`; `ScopedAndGlobalApprovalPreserveUnselectedEntriesAndClearingSides` |
| Selective loading for every command type | `SelectiveAndFullLedgerOperationsRetainExactResultsAtEveryThresholdAsync` |
| Large catalogs, allocation history and checkpoints | `WideCatalogsAndLongAllocationHistoriesRetainExactBalancesAsync` |
| Sustained contention, retries, recurrence correctness and server memory | `SustainedReplicasPreserveEveryUserWriteAndRecurringOccurrenceAsync` |
| Browser memory, session growth and wide rendering | `GrowingHouseholdLedgerMatchesBudgetRegisterAndReportsAsync` |

During test development, probe/assertion failures were corrected: JSONB string
length was measured after materialization, a newly saved overdue template was
correctly expected to include immediate occurrences, and Fluent shadow-input
selectors and asynchronous month-refresh waits were made explicit. An initial
heap-wide object enumeration stalled and was interrupted; the bounded runtime
memory-view probe replaces it. A constructor-based observer could not see memory
exported by WebAssembly and was removed. No tests were skipped or assertions
weakened to hide a financial mismatch. Only clean completed runs supply the
measurement tables above.

The first full-solution validation (`full-final`) passed 759 tests and failed
two, with zero skips. Both failures were cancellation during AppHost startup
(the household browser case and the phone purchase workflow), while the 30
PostgreSQL tests and all unit/component/Aspire tests passed. A follow-up solution
run serializes test modules to separate database stress from browser startup;
no per-project parallel settings, assertions or startup deadlines were changed.

The completed serialized run passed **761 tests: 469 unit, 254 component,
30 PostgreSQL integration, 1 Aspire integration and 7 browser cases**, with zero
failures/skips in 8m 57s. Both startup failures passed on this rerun. The browser
retention signal reproduced as well (61.49 MB post-GC JS heap and 438,622 DOM nodes
at cycle 60). Each separate measurement filter passed its one selected case,
and the independent coverage run passed all 469 unit cases. The solution build
passed with zero warnings/errors. Formatting made no source changes.
Final artifacts: `TestResults/scale-hardening/full-serialized`.

Reproduction commands (run from the repository root; run the three integration
measurement filters and the browser filter sequentially for comparable samples):

```powershell
dotnet test --project tests/YHAB.UnitTests/YHAB.UnitTests.csproj --report-trx --coverage --coverage-output-format cobertura --results-directory TestResults/scale-hardening/coverage-verified
dotnet test --project tests/YHAB.IntegrationTests/YHAB.IntegrationTests.csproj --filter-method '*SelectiveAndFullLedgerOperationsRetainExactResultsAtEveryThresholdAsync' --report-trx --results-directory TestResults/scale-hardening/operations-complete
dotnet test --project tests/YHAB.IntegrationTests/YHAB.IntegrationTests.csproj --filter-method '*WideCatalogsAndLongAllocationHistoriesRetainExactBalancesAsync' --report-trx --results-directory TestResults/scale-hardening/wide-final
dotnet test --project tests/YHAB.IntegrationTests/YHAB.IntegrationTests.csproj --filter-class '*SustainedContentionTests' --report-trx --results-directory TestResults/scale-hardening/contention-final
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --filter-class '*HouseholdBrowserTests' --report-trx --results-directory TestResults/scale-hardening/browser-final60
dotnet format YHAB.slnx --severity warn
dotnet build YHAB.slnx
dotnet test --solution YHAB.slnx --no-build --report-trx --results-directory TestResults/scale-hardening/full-final
dotnet test --solution YHAB.slnx --no-build --max-parallel-test-modules 1 --report-trx --results-directory TestResults/scale-hardening/full-serialized
dotnet format YHAB.slnx --severity warn --verify-no-changes
```

### Performance implementation observations (October 4, 2026)

These focused runs supersede the corresponding baseline costs above; they are
not a final validation claim for the entire pending optimization set. All timing
observations remain single workstation Debug runs. MB means decimal MB, and
allocation deltas measure churn rather than retained memory.

The generated Fluent initializer fixes two profiled retaining roots: FAST
definition observers capturing removed controls and a strong theme-object cache.
The served-asset browser run passed 120 enhanced-navigation/edit cycles with no
document reload. DOM counts plateaued at 9,730 from cycles 12–120; post-JS-GC
heap grew from 8.40 to 9.10 MB over that interval. WASM capacity stabilized at
150.86 MB from cycle 30; sampled Chromium private memory rose from 419.4 MB at
cycle 12 to 506.1 MB at cycle 120. These do not prove total process memory is flat.
The 500-category budget became interactive in 3,311 ms versus the baseline
10,854 ms. Evidence: `TestResults/performance/browser-120` (1 passed, no failures/skips).
Release publishing also completed; the published JavaScript and its Brotli/gzip
assets contain both lifetime fixes, and the generator rejects an unknown source
bundle and preserves unchanged output. Release browser and longer-session
verification are still required.

Metadata catalogs omit allocation history, assignments load their exact key, and
monthly projections use SQL contribution aggregates and a bounded checkpoint set.
The largest fixture (100 accounts / 500 categories / 240 months) changed as follows:

| Observation | Baseline | Focused optimized run |
| --- | ---: | ---: |
| Catalog JSON bytes | 12,794,907 | 194,908 |
| Assignment allocation entities read | 120,000 | 1 |
| Warm-month allocation entities read | 120,000 | 500 |
| Warm-month checkpoint entities read | 240 | 1 |
| Warm-month elapsed ms | 1,320 | 42.0 |
| Warm-month process allocated MB | 319.9 in the intermediate catalog-only run | 5.23 |
| Cold-month elapsed ms | 3,098 | 1,645 |

Cold replay still processes all necessary history, now in year-sized windows;
the largest fixture used 73 SQL commands, trading round trips for bounded ledger
windows. Checkpoint states themselves still grow with months and categories.
Evidence: `wide-allocation` and `wide-checkpoint` under `TestResults/performance`,
each 1 passed, no failures/skips. `MonthlyCheckpointTests` passed 7 cases including
missing period recovery and backdated changes. An initial run failed four JSON
string comparisons solely because SQL zero had a different decimal scale; every
field and category row is now compared by value. No financial expectation changed.

Selective patch persistence, undo/redo reads, receipt retries and monthly funding
were then measured at 10k/50k/100k entries. At 100k:

| Operation | Baseline ms / transaction entities | Optimized ms / transaction entities |
| --- | ---: | ---: |
| Receipt retry | 3,463 / 100,000 | 10.8 / 0 |
| Undo assignment | 4,738 / 100,000 | 36.4 / 0 |
| Redo assignment | 5,044 / 100,000 | 15.0 / 0 |
| Move money | 4,861 / 100,000 | 44.0 / 0 |
| Fund targets | 5,274 / 100,000 | 82.2 / 54 |

The optimized money move follows an explicit monthly read, matching the UI's
loaded-month workflow. Funding then resumes its persisted opening after the move
invalidates the result cache. Cold replay is measured separately. Exact balances,
receipts and undo values pass at all three transaction thresholds. Evidence:
`TestResults/performance/operations-selective` (1 passed, no failures/skips).
`selective-history` passed 15 persistence cases; `history-dependents` passed 4
cases rejecting deletion of principals referenced by later automatic writes.
`patch-persistence` passed both mixed sparse-update cases with automatic change
detection initially enabled and disabled. `month-funding-unit` passed 13 pure
cycle/transition cases. Later added tests and the full suite still need final runs.

Actual `EXPLAIN (ANALYZE, BUFFERS)` plans exposed a 100k-row scan and PostgreSQL JIT
on a one-entry edit. Separate indexed ID lookups and split queries based on the
selected IDs reduced observed total SQL execution from 362 ms to 23.6 ms; the
transaction and split selects took 2.1 and 1.9 ms. Their diagnostic EXPLAIN executions
took 0.110 and 0.042 ms and used indexes. Evidence: `sql-statistics` and `sql-selective`
under `TestResults/performance`; each passed one case. Due-template discovery still
needs its index/query assessment; materializing one entity alone does not prove
the underlying SQL examines only one row.

Recurring posting now proposes at most 128 dates and queries only their stable
identities. `LongRunningTemplatesReadOnlyCandidateIdentitiesAndRespectPlanScopeAsync`
verifies 1,001 prior occurrences, a moved actual date, another owner's overlapping
template IDs, and schedule reset/undo/redo. Posting loads one template entity;
duplicate-only catch-up loads that template and 128 identity DTOs, with no prior
posting entities or splits. `recurrence-bounded` passed 12 persistence/query cases;
`recurrence-unit` passed 15 transaction cases. The bounded lookup and cursor tests
retain literal counts, dates, balances and unchanged inputs.

Background batches now acquire the owned plan row before reading their input,
refreshing the revision after the lock wait. This uses PostgreSQL's
[row-lock behavior](https://www.postgresql.org/docs/current/explicit-locking.html#LOCKING-ROWS)
inside the existing transaction; interactive commands keep optimistic revision
checks. Eight concurrent background calls to a 72-date remainder produced one
commit, with no empty revisions/receipts. `background-claims` passed 11 concurrency,
atomicity and posting cases. Sustained tests now require all 36 batches to finish
before editors stop. The isolated `contention-locked` run passed all four 60-second
scenarios (one test, no failures/skips). Every scenario posted all 4,380 occurrences
in 36 batches during load, with no backlog or unexpected error logs:

| Editors / processors | User commits/s | User conflict rate | User completion p95 / max ms | Worker commits during load, baseline → current |
| --- | ---: | ---: | ---: | ---: |
| 1 / 1, continuous | 58.4 | 0.3% | 21.6 / 404.5 | 24 → 36 |
| 4 / 2, continuous | 130.1 | 25.3% | 28.2 / 40,550.3 | 2 → 36 |
| 8 / 3, continuous | 106.3 | 62.6% | 33.7 / 60,020.9 | 1 → 36 |
| 8 / 3, paced | 16.0 | 47.4% | 83.2 / 351.3 | 36 → 36 |

The worker starvation observed in the baseline did not recur in this experiment.
The synthetic editor's immediate retry-until-success loop can still starve an
individual writer: one editor completed just one change at the end of the highest
load. This is not a latency improvement for every writer, despite better aggregate
throughput and p95. The application reports a revision conflict and asks the user
to refresh/retry; it does not run that automatic rebasing loop. Preserve this
adversarial result, and measure deployment traffic separately rather than hiding
the tail with a different load generator. Process allocation churn was 6.38–13.43 GB
per window and sampled private peaks 77.9–97.1 MB; the tight polling/test-harness
limitations of the baseline still apply.

Remaining: account/reconciliation read costs, metadata checkpoint invalidation,
due-template discovery plans, report aggregation assessment, independent Release-process
sustained-load measurements, and the final coverage/build/format/browser validation
gates. No completion or production latency guarantee is implied.

The recurring optimization checkpoint passed **793 tests: 485 unit, 256 component,
44 PostgreSQL integration, 1 Aspire integration and 7 browser**, with zero failures
or skips. Three focused cases passed after strengthening cancellation cleanup and
the SQL-plan assertions. The principal pure policies retain 100% line/branch
coverage; `TransactionChanges` is now 167/167 lines and 246/246 branches, including
generated lambdas. Build completed with zero warnings/errors, and the final format
verification was clean. These gates validate the current checkpoint, not the
remaining optimization backlog. Commands were run from the repository root:

```powershell
dotnet format YHAB.slnx --severity warn
dotnet format YHAB.slnx --severity warn --verify-no-changes
dotnet build YHAB.slnx
dotnet test --project tests/YHAB.UnitTests/YHAB.UnitTests.csproj --no-build --report-trx --coverage --coverage-output-format cobertura --results-directory TestResults/performance/unit-recurring
dotnet test --project tests/YHAB.ComponentTests/YHAB.ComponentTests.csproj --no-build --report-trx --results-directory TestResults/performance/components-recurring
dotnet test --project tests/YHAB.IntegrationTests/YHAB.IntegrationTests.csproj --no-build --report-trx --results-directory TestResults/performance/integration-recurring
dotnet test --project tests/YHAB.IntegrationTests/YHAB.IntegrationTests.csproj --filter-class '*BackgroundPostingTests' --filter-class '*SelectiveSqlPlanTests' --report-trx --results-directory TestResults/performance/recurring-final-checks
dotnet test --project tests/YHAB.AspireIntegrationTests/YHAB.AspireIntegrationTests.csproj --no-build --report-trx --results-directory TestResults/performance/aspire-recurring
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --no-build --report-trx --results-directory TestResults/performance/browser-recurring
```

The browser rerun retained 9,730 DOM nodes at cycles 12 and 120; post-JS-GC heap
was 8.47 and 9.16 MB. Chromium private bytes still rose from 427.9 to 511.2 MB,
so total process plateau remains unproven. The 500-category view rendered in
3,091 ms with the exact $99,880,000 Ready to assign. The full-page screenshot
also confirms that all 500 rows are rendered; this is not category virtualization.
As a full-suite observation, its timings and process totals are not an isolated
performance comparison.

Inspection identified that payee-only edits invalidated financial checkpoints
and retained full transaction records in undo history. The bulk optimization now
projects matching IDs/names, uses one parameterized update, and keeps versioned
name-only history in the existing JSON columns. No migration is needed. Rename,
undo and redo preserve financial checkpoints; undo also preserves later recurring
schedule advances and the names of newly posted occurrences.

The isolated `operations-payee` run passed all 10k/50k/100k thresholds (one test,
no failures/skips). All monetary checks, receipt counts, exact names, history
cardinality and recurring dates passed. No transaction or split entities were
materialized by the three bulk operations:

| Ledger entries / renamed entries | Rename ms | Rename allocated MB | Undo ms | Redo ms | History JSON characters |
| --- | ---: | ---: | ---: | ---: | ---: |
| 10,000 / 9,000 | 240.8 | 12.02 | 245.9 | 239.1 | 1,260,076 |
| 50,000 / 45,000 | 1,100.4 | 48.37 | 1,101.2 | 1,103.9 | 6,300,076 |
| 100,000 / 90,000 | 2,080.7 | 92.18 | 2,248.5 | 2,502.3 | 12,600,076 |

Compared with the 100k baseline, rename fell from 16,507 ms and 4,754 MB of
allocation churn. These are still proportional bulk updates and history payloads,
not constant-cost operations. The actual matched names cross the database boundary;
zero entity materializations does not mean zero rows read. Matching retains .NET
ordinal case semantics, including Unicode; SQL receives parameterized literal names.
The same run measured the still-unoptimized report at 730.6 ms, 206.39 MB allocated,
and 18,928 transaction entities. Account metadata edits still read account history
(10,000 transactions and 216.78 MB allocated in this fixture).

`payee-persistence` passed all nine database cases. Two initial rollback assertions
failed only because PostgreSQL returned the unchanged ledger in a different order;
the assertions now compare stable sequence order and every field. The first scale
attempt incorrectly expected EF entity counters to observe ordinary DTO projections;
the corrected assertion checks exact persisted history cardinality. Financial and
rollback expectations were retained. Evidence is under `TestResults/performance`.

The bulk-payee checkpoint passed all five projects: **509 unit, 256 component,
53 PostgreSQL integration, 1 Aspire integration and 7 browser tests** (826 total,
zero failures/skips). The desktop/phone workflow was subsequently strengthened
to rename a reconciled transaction through the static settings form, undo/redo
in the interactive register, and check the unchanged $925 balance, reconciliation
state and refreshed payee suggestions. Both updated cases passed in
`browser-payee-workflow` (2 passed, zero failures/skips; reruns of existing cases).
The final solution build has zero warnings/errors.

The `unit-payee-final` report records 16/16 lines and 20/20 branches for `PayeePatch`,
27/27 and 22/22 for `BudgetHistoryCodec`, and 1/1 and 4/4 for the mutation's empty
policy. `CatalogChanges`, `TransactionChanges`, the budget calculator and sparse
ledger patch retain their prior 100% line/branch coverage. The codec rejects
non-object roots before deserialization, so redundant null-instance branches were
removed; absent name arrays and unsupported/malformed history remain tested errors.

| Payee requirement | Direct evidence |
| --- | --- |
| Case matching, input preservation, validation boundaries and conflicts | `RenameStoresOnlyChangedNamesAndPreservesInputs`, `MissingNamesAreRejected`, `NameLimitIsCheckedBeforeTrimming`, `RestoreRequiresEveryExpectedIdentityAndExactNameButAllowsUnrelatedEntries` |
| Sparse/new and existing history formats, invalid data | `PayeeHistoryRoundTripsWithoutTransactionSnapshots`, `LedgerHistoryRetainsItsExistingFormatAndSparseValues`, `UnknownOrIncompleteHistoryFailsBeforeAnyRestore`, `MalformedHistoryPreservesItsParsingFailure` |
| Exact history, checkpoints, later automatic fields and owner isolation | `RenameAndHistoryPreserveCheckpointsAndLaterAutomaticFieldsAsync` |
| Rejected stale names/deletions, one winning claim, rollback and retry | `HistoryRejectsChangedOrRemovedPayeesWithoutPartialWritesAsync`, `CompetingBulkRenamesCommitOneCompleteResultAsync`, `FailureAfterBulkUpdateRollsBackNamesCursorAndReceiptAndCanRetryAsync` |
| Unicode and literal names in SQL | `MatchingUsesOrdinalCaseRulesAndLiteralParameterizedNamesAsync` |
| 10k/50k/100k costs and exact effects | `SelectiveAndFullLedgerOperationsRetainExactResultsAtEveryThresholdAsync` |
| Real static-form rename and interactive undo/redo on desktop/phone | `ManualPlanPurchaseUndoAndReportsAgreeAsync`, through `VerifyPayeeRenameUndoAsync` |

Commands for this checkpoint (use `--no-build` only after the matching build):

```powershell
dotnet format YHAB.slnx --severity warn
dotnet build YHAB.slnx
dotnet test --project tests/YHAB.UnitTests/YHAB.UnitTests.csproj --no-build --report-trx --coverage --coverage-output-format cobertura --results-directory TestResults/performance/unit-payee-final
dotnet test --project tests/YHAB.ComponentTests/YHAB.ComponentTests.csproj --no-build --report-trx --results-directory TestResults/performance/components-payee
dotnet test --project tests/YHAB.IntegrationTests/YHAB.IntegrationTests.csproj --no-build --report-trx --results-directory TestResults/performance/integration-payee
dotnet test --project tests/YHAB.AspireIntegrationTests/YHAB.AspireIntegrationTests.csproj --no-build --report-trx --results-directory TestResults/performance/aspire-payee
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --no-build --report-trx --results-directory TestResults/performance/browser-payee
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --no-build --filter-class '*BudgetWorkflowTests' --report-trx --results-directory TestResults/performance/browser-payee-workflow
dotnet format YHAB.slnx --severity warn --verify-no-changes
```

#### Report aggregation checkpoint

Reports now return SQL account/month and category/month totals, with earlier
movements collapsed to one opening bucket per account. The pure projection adds
each account's opening balance on its actual opening date, then calculates assets
and debt per account before summing. Catalog and both aggregate queries share the
existing owner-authorized repeatable-read transaction. Partial months, tracking
transfers, refunds and recurring-template exclusion retain their prior meanings.

The isolated `report-scale-isolated` run passed all 10k/50k/100k thresholds (one
test, zero failures/skips). It measured seven SQL commands per report, no transaction,
split or allocation entities, and the same ending net worth as the account view:

| Ledger entries | Two-year report ms | SQL execution ms | Process allocated MB | Response bytes |
| --- | ---: | ---: | ---: | ---: |
| 10,000 | 87.0 | 16.3 | 2.17 | 7,313 |
| 50,000 | 86.2 | 57.7 | 2.01 | 7,435 |
| 100,000 | 157.2 | 120.7 | 2.02 | 7,435 |

The preceding 100k payee-phase run measured 730.6 ms, 206.39 MB allocated and
18,928 transaction entities for this report. These are descriptive Debug samples,
not retained-memory figures or timing guarantees. The first aggregate measurement
also ran alongside the SQL-plan test; the isolated rerun above avoids that source
of measurement interference.

`ReportsReturnBoundedMonthlyAggregatesFromLargeLedgersAsync` captured actual
PostgreSQL plans after analyzing its separate 100k fixture. The account query
returned 250 rows and the category query 1,095 rows. EXPLAIN execution times were
33.683 and 26.300 ms. Underlying scans still visited the 100k ledger/split tables;
this optimization bounds returned data and application allocations, not all
database work. The report and SQL-plan tests assert those distinctions explicitly.

`ReportProjectionTests` passed three cases comparing complete report values with
the existing full-history calculator and independent three-month totals, including
foreign plans with overlapping IDs, partial dates, account openings and a write
between the two aggregate reads. The initial compile caught an ambiguous C# query
keyword and was corrected before these tests ran. Evidence is under
`TestResults/performance/report-projection`, `operations-report`, and
`report-scale-isolated`.

The complete unit and component suites passed **515 and 256 cases** respectively;
the affected report/household/checkpoint/ledger persistence selection passed
**16 cases**, all with zero failures/skips. `unit-report` records 63/63 lines and
96/96 branches for `ReportCalculator`, including generated lambdas. Catalog and
transaction changes retain 111/111 lines, 182/182 branches and 167/167 lines,
246/246 branches respectively. Full solution build completed with zero warnings/errors.

The first affected browser selection passed both desktop/phone workflows but
failed the household memory assertion (2 passed, 1 failed, 0 skipped). Its budget,
register and report checks passed at 200/400/600 transactions. During the long
session, DOM counters were 7,304 at cycles 12 and 30, then 9,730 at cycle 60,
exceeding the warm sample plus 500 bound. This failure is retained in `browser-report`.
An isolated diagnostic rerun passed the entire household case, including all
120 cycles, with 9,730 DOM nodes at cycles 12/30/60/120. The optional heap snapshots
at cycles 1/12/120 each contain 308 nodes marked detached, one live register table
and no old budget/register tables. This does not explain or erase the first failure.
The assertion has not been loosened; when diagnostics are enabled it now captures
a heap snapshot before failing the growth bound, to aid the continuing investigation.

In that diagnostic rerun, sampled Chromium private bytes rose from 425.77 MB at
cycle 12 to 507.11 MB at cycle 120. WASM capacity stabilized at 150.86 MB from
cycle 30, and JS heap rose from 8.43 to 9.03 MB. These measurements, influenced by
diagnostic collection, do not establish a steady-state process-memory plateau.
The 500-category budget became interactive in 2,849.4 ms with exact totals.
Evidence: `report-memory-diagnostic` and ignored local `report-memory-snapshots`.
Longer Release/process measurements and the inconsistent DOM baseline remain open.

Report-phase commands, from the repository root:

```powershell
dotnet test --project tests/YHAB.IntegrationTests/YHAB.IntegrationTests.csproj --filter-class '*ReportProjectionTests' --report-trx --results-directory TestResults/performance/report-projection
dotnet test --project tests/YHAB.IntegrationTests/YHAB.IntegrationTests.csproj --filter-method '*SelectiveAndFullLedgerOperationsRetainExactResultsAtEveryThresholdAsync' --filter-method '*ReportsReturnBoundedMonthlyAggregatesFromLargeLedgersAsync' --report-trx --results-directory TestResults/performance/operations-report
dotnet test --project tests/YHAB.IntegrationTests/YHAB.IntegrationTests.csproj --no-build --filter-method '*SelectiveAndFullLedgerOperationsRetainExactResultsAtEveryThresholdAsync' --report-trx --results-directory TestResults/performance/report-scale-isolated
dotnet format YHAB.slnx --severity warn
dotnet build YHAB.slnx
dotnet test --project tests/YHAB.UnitTests/YHAB.UnitTests.csproj --no-build --report-trx --coverage --coverage-output-format cobertura --results-directory TestResults/performance/unit-report
dotnet test --project tests/YHAB.ComponentTests/YHAB.ComponentTests.csproj --no-build --report-trx --results-directory TestResults/performance/components-report
dotnet test --project tests/YHAB.IntegrationTests/YHAB.IntegrationTests.csproj --no-build --filter-class '*ReportProjectionTests' --filter-class '*MonthlyCheckpointTests' --filter-class '*HouseholdPersistenceTests' --filter-class '*LedgerFoundationTests' --report-trx --results-directory TestResults/performance/report-persistence-regressions
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --no-build --filter-class '*BudgetWorkflowTests' --filter-class '*HouseholdBrowserTests' --report-trx --results-directory TestResults/performance/browser-report
$env:YHAB_HEAP_SNAPSHOTS = (Join-Path (Get-Location) 'TestResults/performance/report-memory-snapshots')
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --filter-class '*HouseholdBrowserTests' --report-trx --results-directory TestResults/performance/report-memory-diagnostic
Remove-Item Env:YHAB_HEAP_SNAPSHOTS
dotnet format YHAB.slnx --severity warn --verify-no-changes
```

#### Account validation, checkpoints and recurring discovery

Account edits now read SQL facts for opening-date, reconciliation and closing
validation instead of materializing account history. Reconciliation uses an
aggregate cleared balance, then loads only posted entries whose selected account
side is cleared and will change. Earlier reconciled/uncleared entries, future
entries and templates stay outside the mutation/history payload. Transfer-side
clearing states remain independent. These SQL aggregates still examine relevant
account history in the database; zero EF entities does not mean zero rows scanned.

The extracted `CheckpointPolicy` compares financial fields rather than every
serialized field. Names, notes, targets, snoozing and clearing preserve saved
openings. Amount/date/sequence/split-identity changes invalidate subsequent months;
account financial changes and category identity changes reset the openings.
Versioned result caches still refresh after metadata changes, so targets and labels
update even when their financial carry is reusable.

| Requirement | Direct evidence |
| --- | --- |
| Transfer direction, dates, templates and opening balances | `FactsIncludeBothTransferSidesButExcludeTemplatesFutureAmountsAndOpeningBalances`, `OtherTransferSideCannotLockTheAccountsOpening`, `AccountAggregatesRespectDatesTransferSidesAndOverlappingForeignIdsAsync` |
| Reconciliation from aggregate history, exact changed records and undo/redo | `ReconciliationCanUseAggregatesWithoutLoadingPreviouslyReconciledHistory`, `ReconciliationReadsOnlyChangedEntriesAndRestoresThemWithoutTouchingOtherSidesAsync` |
| Metadata preserves financial checkpoints; financial edits rebuild exact totals | `PresentationTargetsClearingAndSnoozingPreserveFinancialOpeningsAndInputs`, `PostedMovementChangesInvalidateBothOldAndNewDates`, `MetadataAndClearingPreserveCheckpointContentsWhileMoneyChangesRebuildAsync` |
| Sparse loading across 10k/50k/100k ledgers and wide catalogs | `SelectiveAndFullLedgerOperationsRetainExactResultsAtEveryThresholdAsync`, `WideCatalogsAndLongAllocationHistoriesRetainExactBalancesAsync` |
| Template discovery avoids posted-history scans and preserves every scheduled date | `EntryEditsAndRecurringBatchesHaveInspectablePlansAndExactEffectsAsync` |

The first full account integration run passed 56 cases and failed four with
`ManyServiceProvidersCreatedWarning`. Each measurement fixture supplied a stateful
singleton interceptor, causing EF's global provider cache to retain distinct service
graphs. `BudgetDatabase` now owns/disposes its EF internal provider and registers
interceptors through that graph. An initial attempt retaining `AddInterceptors`
failed all three focused account cases; registering both interceptor interfaces
through DI resolved it. The warning remains enabled and probes remain isolated.
This follows the documented [EF singleton interceptor lifetime](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors)
and [Npgsql internal-provider registration](https://www.npgsql.org/efcore/api/Microsoft.Extensions.DependencyInjection.NpgsqlServiceCollectionExtensions.html).

Fresh isolated measurements after that harness correction passed all three ledger
thresholds. Both account operations materialized zero transaction/split entities
in this fixture. Its reconciliation has no remaining cleared candidates; the
dedicated persistence test separately verifies three changed records, one adjustment,
and exact undo/redo while preserving unrelated entries.

| Entries | Account edit ms / allocated MB | Reconcile ms / allocated MB | Two-year report ms / allocated MB |
| --- | ---: | ---: | ---: |
| 10,000 | 101.4 / 1.44 | 45.5 / 2.04 | 76.0 / 2.13 |
| 50,000 | 39.6 / 1.39 | 39.0 / 2.01 | 82.0 / 2.00 |
| 100,000 | 44.6 / 1.40 | 43.4 / 2.00 | 140.8 / 2.01 |

Account edits used 11 SQL commands, reconciliation 13, and reports seven. The 100k
run also measured assignment undo at 38.5 ms, target funding at 50.5 ms (54 current
transaction entities), and renaming 90k payees at 1,923.1 ms / 92.13 MB allocated.
These are Debug samples, not latency guarantees or retained-memory figures. Changing
fixture provider ownership changes model/query cache reuse, so timing comparisons
with earlier harness runs are descriptive rather than a controlled speedup ratio.
Evidence: `operations-discovery-isolated` (one test passed, zero failures/skips).

The separate wide-plan run passed all three dimensions. At 100 accounts, 500
categories and 240 months, the catalog remained 194,908 bytes. Cold replay took
1,451.7 ms / 452.84 MB allocated and 73 SQL commands; the subsequent month took
35.1 ms / 5.16 MB, reading one checkpoint, 500 allocations and two transactions.
An assignment read one allocation. Stored checkpoint JSON totaled 6,963,541
characters across 241 openings. Cold history work and stored opening size still
grow with plan dimensions. Evidence: `wide-discovery-isolated` (one passed,
zero failures/skips); it ran after the ledger measurements, without competing tests.

The recurrence SQL-plan regression initially failed because finding one template
discarded 100,256 posted rows (EXPLAIN execution 5.338 ms). The Aspire-authored
`IndexRecurringTemplates` migration replaces the full repeat/date index with a
partial plan/date/sequence/ID index containing only `Repeat <> 0` rows. The ordinary
register index remains. With the new index, per-plan discovery measured 0.057 ms
and global worker discovery 0.091 ms in EXPLAIN; neither scanned the posted ledger.
The strengthened fixture keeps another batch due while captured SELECTs are
explained, verifies all 512 scheduled dates and exact balances/revisions, then
checks an empty worker pass. It passed in `discovery-nonempty`. Work still grows
with eligible templates across plans; the index does not eliminate that dimension.

The corrected full integration run passed **60 tests**, with zero failures/skips;
the strengthened nonempty SQL-plan case passed again afterward. The complete unit
and component runs passed **543 and 256** respectively, also without failures/skips.
`unit-discovery` retains 100% line/branch coverage for the principal calculation,
history, recurrence and workspace policies, `CatalogChanges`, `TransactionChanges`,
and the new `AccountLedgerFacts` and `CheckpointPolicy`. The solution build completed
with zero warnings/errors. These results do not close the earlier browser DOM
baseline inconsistency or prove a total-process memory plateau.
The real Aspire startup/migration test also passed (**one test**, zero failures/skips)
in `aspire-discovery`, using disposable storage and the normal migration dependency.

Evidence is under `TestResults/performance`. Commands were run from the repository
root; `--no-build` follows the matching solution/test build:

```powershell
dotnet build YHAB.slnx
dotnet test --project tests/YHAB.IntegrationTests/YHAB.IntegrationTests.csproj --no-build --report-trx --results-directory TestResults/performance/integration-discovery
dotnet test --project tests/YHAB.UnitTests/YHAB.UnitTests.csproj --report-trx --coverage --coverage-output-format cobertura --results-directory TestResults/performance/unit-discovery
dotnet test --project tests/YHAB.ComponentTests/YHAB.ComponentTests.csproj --report-trx --results-directory TestResults/performance/components-discovery
dotnet test --project tests/YHAB.IntegrationTests/YHAB.IntegrationTests.csproj --no-build --filter-method '*EntryEditsAndRecurringBatchesHaveInspectablePlansAndExactEffectsAsync' --report-trx --results-directory TestResults/performance/discovery-nonempty
dotnet test --project tests/YHAB.IntegrationTests/YHAB.IntegrationTests.csproj --no-build --filter-method '*SelectiveAndFullLedgerOperationsRetainExactResultsAtEveryThresholdAsync' --report-trx --results-directory TestResults/performance/operations-discovery-isolated
dotnet test --project tests/YHAB.IntegrationTests/YHAB.IntegrationTests.csproj --no-build --filter-method '*WideCatalogsAndLongAllocationHistoriesRetainExactBalancesAsync' --report-trx --results-directory TestResults/performance/wide-discovery-isolated
dotnet test --project tests/YHAB.AspireIntegrationTests/YHAB.AspireIntegrationTests.csproj --no-build --report-trx --results-directory TestResults/performance/aspire-discovery
```

Remaining goal work is a longer Release run with independent application processes
and the real worker cadence, investigation of the intermittent browser DOM baseline
and total-process memory growth, and a final review of the complete optimization set.

#### Independent Release processes: initial contention evidence

`ProcessLoadTests` now runs actual Release web executables in two Aspire replicas,
with disposable PostgreSQL and unchanged 30-second recurring workers. Each plan
has 100k posted entries and two daily schedules with 365 dates due. Assertions
verify every accepted receipt, exact category assignments, all 730 occurrence
identities/dates/amounts, $2,909,270 total account balance, revisions and the 50-entry
history bound. Console logs are checked for errors. The process-memory probe
resolves the web executable beneath `dotnet run`; the launcher's memory would
substantially understate application usage.

The first three-minute run synchronized eight writers against one plan through
the Aspire proxy. It passed (one test, zero failures/skips), but only 560 of 2,880
attempts committed; 2,320 returned conflicts (80.6%). One editor made 359 commits
while the others made 23–41. Recurring catch-up was observed at 90.1 seconds.
The 43.1 ms p95 includes rejected attempts, not user retry/completion time.
Evidence: `TestResults/performance/process-load-runtime`.

The next experiment discovered each replica's local listening port and directed
four writers to each. Writers were staggered across each 500 ms interval rather
than synchronized. The initial endpoint guard rejected Aspire's
`yhab.dev.localhost` name (two preflight failures in `process-load-release`);
the observed resources also listen on `localhost` at that same port, which the
corrected probe uses without logging environment secrets.

Both ten-minute cases passed (two tests, zero failures/skips in
`process-load-direct`). The runner overlapped these theory cases, so they are
correctness stress evidence, not isolated comparative benchmarks:

| Plans / posted entries | Accepted / attempted writes | Conflicts | Attempt p95 / max ms | All recurrence caught up |
| --- | ---: | ---: | ---: | ---: |
| 1 / 100k | 9,580 / 9,599 | 19 (0.2%) | 31.6 / 464.9 | 80.1 s |
| 8 / 800k | 9,590 / 9,600 | 10 (0.1%) | 31.1 / 168.0 | 90.9 s |

Every editor made 1,194–1,199 commits in the shared-plan case and 1,198–1,199 in
the independent-plan case. End-of-load private memory was 141.8/136.2 MB for the
shared-plan web processes and 155.7/134.1 MB for the independent-plan processes;
after 60 seconds idle it was 142.0/134.3 MB and 155.7/134.0 MB respectively. These
are sampled process values, excluding the database and harness. They do not
establish hours-long memory behavior or deployment capacity.

The current harness selects one plan-count configuration per invocation so
measurements run sequentially without changing xUnit's parallelism settings. It
adds initial month loading and the balance/month refresh after accepted saves,
checking conservation and the edited category amount. The preceding mutation-only
latency figures must not be presented as full UI-workflow timings.

The isolated shared-plan workflow run passed (one test, zero failures/skips in
`workflow-release-1`). Initial month loading took 1,485.2 ms. Over 600.6 seconds,
8,266 of 9,599 writes committed; 1,333 writes conflicted (13.9%) and 6,122 accepted
writes encountered a subsequent month-refresh conflict. Recurring catch-up was
observed at 80.2 seconds. Accepted-write p95 was 84.5 ms; save plus balance/month
refresh p95 was 516.4 ms, with a 924.3 ms maximum. Refresh conflicts preserve the
accepted write and require a further refresh; these are not lost financial writes.

A two-minute `dotnet-counters` collection on one verified web process late in
that run recorded approximately 35.14 GB allocated over 115 seconds of reported
increments and 8.73 seconds of GC pause time. These counters include all work in
that process and diagnostic overhead; they are not retained bytes per request.
GC committed memory ranged from 267.4 to 382.9 MB, while last-collection generation-2
size ranged from 73.2 to 92.3 MB. Sampled successful month-request p95 windows were
approximately 321–602 ms; view-request p95 windows were approximately 91–136 ms.
This identifies substantial replay/refresh cost under many writers to one plan;
the exact query/checkpoint cause still needs a targeted reproduction. Counter
evidence is in `workflow-release-1-counters.csv`; the
[official counters documentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters)
describes the process-scoped measurements.

The sequential eight-plan workflow run also passed (one test, zero failures/skips
in `workflow-release-8`). It completed **all 9,598 attempted writes** over 601.5
seconds, with zero rejected writes. Thirteen post-save month refreshes conflicted
during recurring catch-up; that count stopped growing once all schedules caught
up, observed at 61.0 seconds into the offered load. Initial month reads across
the eight plans took 7,565.4 ms before editing started. Accepted-write p95 was
36.5 ms; full save/refresh p95 was 202.0 ms, with a 1,182.9 ms maximum. No automatic
user-write retry loop is included in either latency series.

At the end of this independent-plan load, the two web processes used 195.1 and
164.4 MB private memory; after a further 60 seconds idle, they used 194.8 and
164.5 MB. A matching two-minute counter sample on one replica recorded 3.25 GB
allocated over 115 seconds and 0.32 seconds GC pause time, with 92.6–110.5 MB
committed GC memory. Its five-second request p95 windows ranged from 29–45 ms for
months and 111–134 ms for views. Evidence: `workflow-release-8-counters.csv` and
the per-process JSON paths reported in the TRX. Counters sampled one replica and
only part of each run; their window percentiles are not whole-run percentiles.

The experiments distinguish synchronized bursts, staggered edits on one plan,
and independent-plan editing. They support progress and exact financial results
across separate web processes at this offered load, but do not promise zero
refresh conflicts, arbitrary same-plan write concurrency, deployment capacity or
hours-long memory stability. A same-plan replay/query investigation remains
justified; ordinary independent-plan editing did not show the same contention.

Commands used for the final sequential cases (Release was built first):

```powershell
dotnet build tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --configuration Release
$env:YHAB_LOAD_SECONDS = '600'
$env:YHAB_LOAD_PLANS = '1'
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --configuration Release --no-build --filter-class '*ProcessLoadTests' --report-trx --results-directory TestResults/performance/workflow-release-1
$env:YHAB_LOAD_PLANS = '8'
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --configuration Release --no-build --filter-class '*ProcessLoadTests' --report-trx --results-directory TestResults/performance/workflow-release-8
Remove-Item Env:YHAB_LOAD_SECONDS, Env:YHAB_LOAD_PLANS
```

The full solution build and 543 unit/256 component cases passed before these
measurements (zero failures/skips). An initial analyzer failure from a shadowed
test-local variable was corrected without suppressions. These results are a
checkpoint, not the final validation of the complete pending change set.

#### Extended browser session and checkpoint-publication reproduction

The isolated Release household run (`browser-release-long`) passed **one test,
zero failures/skips**, including 200/400/600-entry financial checks, 10k/50k/100k
registers, **600 navigation/edit cycles**, and the 500-category budget. The actual
web child executable was verified under `bin/Release/net10.0`. The first launch
was blocked before testing by sandbox access to NuGet configuration; the authorized
retry produced the completed result. No development database was used.

From cycle 12 to 600, post-GC JS heap rose from 8.39 to 9.10 MB. WASM capacity
stayed at 150.86 MB from cycle 30. Renderer private memory rose from 325.16 to
574.01 MB and remained 569.06 MB after 60 seconds idle. The new-document wide
budget then reduced renderer private memory to 394.56 MB. This is not evidence
of a stable long-session native-memory plateau.

The low and high DOM samples both report the WebAssembly renderer: 7,304 nodes
with two documents versus 9,730 with three, while listeners remain at 1,077.
The earlier suspected server-to-WASM timing explanation is therefore insufficient.
The cycle-12 and cycle-600 heap snapshots each contain 308 detached native nodes,
one live register table and no old budget/register tables. The latter snapshot
has 4,724 additional Chromium inspector `NetworkResourcesData::ResourceData`
objects. These observations narrow the investigation to transient documents and
native/browser diagnostic retention; they do not yet explain all private bytes.
The original DOM growth bound and weak-reference check passed unchanged.

`CurrentMonthEditsBetweenReplayAndPublicationPreserveExactBalancesAsync` then
forced read/edit/publication order three times on an isolated 100k-entry,
24-month plan. Every financial result matched the full calculator and the
fixture's independent cash total. All three stale publications saved zero
openings; each next replay materialized **100,000 transactions and splits**.
Replay elapsed times were 3,032.9, 2,898.1 and 2,823.0 ms, allocating approximately
1.03 GB each despite only 21–27 ms of SQL execution. These are Debug process
allocation measurements, not retained bytes. After an uncontended publication,
a subsequent current-month edit needed only 284 current transactions and splits,
one checkpoint and eight allocations: **38.7 ms / 3.59 MB allocated**.
Evidence: `checkpoint-publication` (**one passed, zero failures/skips**).

This confirms a conservative checkpoint-publication performance problem: an
intervening current-month edit rejects valid historical openings along with
invalid future openings, allowing repeated historical replay. Any remedy must
still reject states affected by backdated edits, account/category financial
changes, foreign owners and concurrent invalidation. Exact revision validation
must remain on user-visible monthly results and mutations.

The revised browser diagnostic passed its isolated Release 120-cycle run
(`browser-native`, **one passed, zero failures/skips**). It records both an initial
GC sample and a second fixed collection after a rendering opportunity, plus
bounded native allocator traces with heap snapshots. At cycle 120 the initial
sample reproduced 9,730 nodes/three documents, while the second had 7,304/two.
Second-collection counts were 7,304 at cycles 12/30/60/120, listeners stayed
1,077, and the original +500 bound passed. This establishes a collectible
transient-document contribution to the inconsistent baseline. The probe does not
purge caches, signal memory pressure or collect until an assertion passes.

In the same diagnostic run, renderer private memory rose from 329.16 to 412.10 MB
between cycles 12 and 120. Native dumps show buffer-partition allocated objects
growing from 77.08 to 110.84 MB, and Blink's committed heap from 41.94 to 72.68 MB
while live Blink objects grew only from 6.53 to 7.23 MB. WASM capacity grew by
25.17 MB; malloc's reported size fell from 56.12 to 40.27 MB. These allocator
categories can overlap and are not additive accounting for process private bytes.
They distinguish retained objects from allocator reservation, but the larger
buffer allocation and longer-session plateau still require investigation.
Optional allocator tracing starts a separate Chromium tracing-service process,
reported separately in samples; it must not be confused with renderer growth.

The installed Playwright browser manifest pins Chromium **153.0.8010.12**. Its
driver enables the Network domain without buffer overrides. That exact Chromium
version defaults to a 200 MB inspector response buffer on desktop and clears its
stored resources on a new document load
([versioned Chromium source](https://raw.githubusercontent.com/chromium/chromium/153.0.8010.12/third_party/blink/renderer/core/inspector/inspector_network_agent.cc)).
Together with the additional inspector resource objects, this makes diagnostic
retention a plausible contributor, not proof that it accounts for all growth.
A controlled comparison without Network-domain capture or a longer plateau
measurement is needed before attributing the remaining private bytes to app code.
The baseline measurements above were not altered by disabling capture or clearing
these buffers.

Commands for these completed focused runs:

```powershell
$env:YHAB_BROWSER_CYCLES = '600'
$env:YHAB_MEMORY_SAMPLES = (Join-Path (Get-Location) 'TestResults/performance/browser-release-memory.jsonl')
$env:YHAB_HEAP_SNAPSHOTS = (Join-Path (Get-Location) 'TestResults/performance/browser-release-snapshots')
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --configuration Release --filter-class '*HouseholdBrowserTests' --report-trx --results-directory TestResults/performance/browser-release-long
Remove-Item Env:YHAB_BROWSER_CYCLES, Env:YHAB_MEMORY_SAMPLES, Env:YHAB_HEAP_SNAPSHOTS
dotnet test --project tests/YHAB.IntegrationTests/YHAB.IntegrationTests.csproj --filter-class '*CheckpointPublicationTests' --report-trx --results-directory TestResults/performance/checkpoint-publication
$env:YHAB_BROWSER_CYCLES = '120'
$env:YHAB_MEMORY_SAMPLES = (Join-Path (Get-Location) 'TestResults/performance/browser-native-memory.jsonl')
$env:YHAB_HEAP_SNAPSHOTS = (Join-Path (Get-Location) 'TestResults/performance/browser-native-snapshots')
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --configuration Release --filter-class '*HouseholdBrowserTests' --report-trx --results-directory TestResults/performance/browser-native
Remove-Item Env:YHAB_BROWSER_CYCLES, Env:YHAB_MEMORY_SAMPLES, Env:YHAB_HEAP_SNAPSHOTS
```

#### Checkpoint publication under continuous edits

The earlier deterministic reproduction established that intervening current-month
edits discarded valid historical openings. Publication now checks compact persisted
invalidation boundaries while holding the same plan lock as financial writes.
Only openings preceding every financial change since the calculation's revision
can be published. Financial writes, undo/redo and their boundary records commit or
roll back together. Metadata edits preserve openings, and newer earlier boundaries
replace the older boundaries they cover. User-visible reads and mutations retain
their exact revision checks. The Aspire-authored
`PreserveValidCheckpointPublications` migration adds the boundary table with a
plan/version primary key and cascading plan foreign key.

The isolated 100k-entry, 24-month reproduction passed after the fix (one test,
zero failures/skips in `checkpoint-publication-fixed`). It forces the same three
read/edit/publication sequences as before and compares all month fields/category
rows plus the independent cash conservation total:

| Measurement | Before | After |
| --- | ---: | ---: |
| Openings saved after each intervening edit | 0 | 24 |
| Second/third replay transaction entities | 100,000 each | 284 each |
| Second/third replay elapsed | 2,898.1 / 2,823.0 ms | 22.1 / 14.7 ms |
| Second/third replay allocated bytes | about 1.03 GB each | 3.15 MB each |

The initial cold replay remains necessary: 2,951.2 ms and 1.03 GB allocated in
this Debug fixture. Subsequent replays load one checkpoint and eight allocations;
the final uncontended current-month read took 28.0 ms / 3.61 MB allocated. Timing
is a local sample, not a latency guarantee; process allocation deltas measure
churn, not retained memory. Exact read counts and financial assertions are the
regression contracts.

The isolated ten-minute Release workflow repeat also passed (one test, zero
failures/skips in `workflow-checkpoint-1`). It used the same two web processes,
eight staggered editors, 100k-entry plan, 730 due occurrences and unchanged worker
cadence as `workflow-release-1`. All accepted receipts, exact category amounts,
scheduled occurrence identities/dates, final $2,909,270 account total and retained
history passed; neither web process logged an unexpected error.

| Shared-plan workflow | Before | After |
| --- | ---: | ---: |
| Accepted writes / attempts | 8,266 / 9,599 | 9,575 / 9,599 |
| Rejected writes | 1,333 (13.9%) | 24 (0.3%) |
| Accepted-write p95 | 84.5 ms | 34.6 ms |
| Save + balance/month refresh p95 | 516.4 ms | 132.5 ms |
| Save + refresh maximum | 924.3 ms | 536.9 ms |
| Post-save refresh conflicts | 6,122 | 3,886 |
| Recurring catch-up observed | 80.2 s | 80.1 s |

Initial month loading took 1,361.4 ms. Per-editor commits ranged from 1,195 to
1,199. Refresh conflicts preserve accepted writes and need another refresh;
neither latency series includes user retries. These local samples show improvement
at this offered load, not guaranteed latency or arbitrary same-plan concurrency.

A two-minute counter collection on one verified Release web process, after
recurring catch-up, reported **2.75 GB allocated / 0.28 seconds GC pause**, compared
with the earlier 35.14 GB / 8.73 seconds sample. Committed GC memory ranged from
119.3 to 142.2 MB. Five-second successful-request p95 windows ranged from 29.7 to
36.6 ms for months and 55.5 to 63.8 ms for views. Counters include all work and
diagnostic overhead in one replica; window percentiles are not whole-run
percentiles. Evidence: `workflow-checkpoint-1-counters.csv`.

At load end, the two web processes used 157.0/153.7 MB private memory; after the
60-second idle period they used 157.8/151.1 MB. This is short-run process evidence,
not an hours-long stability claim. Both test-owned processes were disposed.
The remaining investigations are cold rebuild allocation/CPU costs and browser
native-memory attribution/plateau, followed by the final whole-change review.

Validation after the migration: **543 unit, 256 component, 63 integration and one
Aspire startup/migration test passed**, with zero failures/skips. The isolated
100k reproduction and Release load case above passed separately. The solution
and Release test/app builds both completed with zero warnings/errors.

```powershell
dotnet test --project tests/YHAB.IntegrationTests/YHAB.IntegrationTests.csproj --no-build --filter-method '*CurrentMonthEditsBetweenReplayAndPublicationPreserveExactBalancesAsync' --report-trx --results-directory TestResults/performance/checkpoint-publication-fixed
dotnet test --project tests/YHAB.IntegrationTests/YHAB.IntegrationTests.csproj --no-build --report-trx --results-directory TestResults/performance/integration-checkpoint
dotnet test --project tests/YHAB.UnitTests/YHAB.UnitTests.csproj --no-build --report-trx --results-directory TestResults/performance/unit-checkpoint
dotnet test --project tests/YHAB.ComponentTests/YHAB.ComponentTests.csproj --no-build --report-trx --results-directory TestResults/performance/components-checkpoint
dotnet test --project tests/YHAB.AspireIntegrationTests/YHAB.AspireIntegrationTests.csproj --no-build --report-trx --results-directory TestResults/performance/aspire-checkpoint
dotnet build tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --configuration Release
$env:YHAB_LOAD_SECONDS = '600'
$env:YHAB_LOAD_PLANS = '1'
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --configuration Release --no-build --filter-class '*ProcessLoadTests' --report-trx --results-directory TestResults/performance/workflow-checkpoint-1
Remove-Item Env:YHAB_LOAD_SECONDS, Env:YHAB_LOAD_PLANS
```

#### Browser diagnostic retention control

`InspectorRetentionTests.DisconnectingDiagnosticsPreservesTheLivePlanAsync`
owns a disposable Chromium process independently of its CDP connection. It
reuses the original enhanced-navigation/assignment workload and derives its
executable and launch defaults from Playwright's installed headless browser.
Only the diagnostic transport, owned persistent profile, certificate handling
and initial blank page differ from the ordinary browser fixture. This allows a
disconnect/reconnect without closing the context or navigating the page.

The isolated Release run completed **600 cycles and passed one test, zero
failures/skips**, in 11 minutes 41 seconds. The 100k-entry fixture used no recurring
templates so that automatic writes could not invalidate the control's exact
revision check. Every cycle saved through the UI and checked the refreshed assigned
amount and bounded 50-row register. The original DOM growth bound and removed-table
WeakRef assertion remained unchanged.

| Sample | JS heap MB | WASM capacity MB | DOM nodes / documents | Renderer private MB |
| --- | ---: | ---: | ---: | ---: |
| Cycle 12 | 8.44 | 125.70 | 7,267 / 2 | 328.74 |
| Cycle 30 | 8.63 | 150.86 | 7,267 / 2 | 367.71 |
| Cycle 600 | 9.30 | 150.86 | 7,267 / 2 | 577.36 |
| Idle 60 seconds / before disconnect | 9.30 | 150.86 | 7,267 / 2 | 566.15 |
| After reconnect | 9.97 | 150.86 | 7,267 / 2 | 343.18 |

The before/after control preserved renderer PID 45828, `performance.timeOrigin`,
the actual document, workspace element and .NET runtime object identities, URL
and WebAssembly renderer. It did not reload the page, clear application caches,
signal memory pressure or repeat garbage collection until an assertion passed.
A further real assignment persisted as $601, revision 602, with the independent
$2,910,000 cash total and no uncaught page errors. The browser/profile and test
AppHost were disposed after the check.

Heap snapshots contained 335 inspector `NetworkResourcesData::ResourceData`
records at cycle 12 and 5,061 before disconnect; none remained after reconnect.
The renderer's buffer-partition allocated-object bytes fell from 218.66 to
4.52 MB. Those allocator values overlap other accounting categories and must
not be added to private-memory totals. Together with the approximately 223 MB
private-memory reduction, this establishes a substantial diagnostic-retention
contribution to the earlier growth. It does not prove every remaining native byte
is necessary, measure live WASM managed objects, or establish hours-long stability.
The small JS/listener increase after reconnect belongs to a new diagnostic
connection and is retained in the report rather than removed from the samples.

Evidence: `TestResults/performance/browser-control-long`,
`browser-control-long-memory.jsonl` and `browser-control-long-snapshots`.
An earlier 120-cycle exploratory control also passed, releasing about 119 MB,
but used full Chromium with different defaults (eight documents/six renderers).
Its absolute footprint is not compared with the headless-shell baseline. A first
preflight attempt failed on the disposable application's certificate; the shared
registration helper now explicitly permits that test certificate for its anonymous
API check. This was a harness setup failure, not an application memory finding.

Follow-up validation passed **543 unit and 256 component tests**, the existing
household browser workflow with 120 edit/navigation cycles, and the independent
process-load fixture with its original defaults (120-second load, one plan), all
with zero failures/skips. The two latter cases ran sequentially after the isolated
control. Their purpose was to verify the shared helper extraction and fixture
options; the shorter load run does not replace the earlier ten-minute measurements.
The full solution build passed with zero warnings/errors before these runs, and
the Release control run rebuilt the affected test/application projects.

```powershell
dotnet build YHAB.slnx
$env:YHAB_BROWSER_CYCLES = '600'
$env:YHAB_MEMORY_SAMPLES = "$PWD/TestResults/performance/browser-control-long-memory.jsonl"
$env:YHAB_HEAP_SNAPSHOTS = "$PWD/TestResults/performance/browser-control-long-snapshots"
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --configuration Release --filter-class '*InspectorRetentionTests' --report-trx --results-directory TestResults/performance/browser-control-long
Remove-Item Env:YHAB_BROWSER_CYCLES, Env:YHAB_MEMORY_SAMPLES, Env:YHAB_HEAP_SNAPSHOTS
dotnet test --project tests/YHAB.UnitTests/YHAB.UnitTests.csproj --no-build --report-trx --results-directory TestResults/performance/unit-inspector
dotnet test --project tests/YHAB.ComponentTests/YHAB.ComponentTests.csproj --no-build --report-trx --results-directory TestResults/performance/components-inspector
$env:YHAB_BROWSER_CYCLES = '120'
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --configuration Release --no-build --filter-class '*HouseholdBrowserTests' --report-trx --results-directory TestResults/performance/browser-inspector-regression
Remove-Item Env:YHAB_BROWSER_CYCLES
$env:YHAB_LOAD_SECONDS = '120'
$env:YHAB_LOAD_PLANS = '1'
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --configuration Release --no-build --filter-class '*ProcessLoadTests' --report-trx --results-directory TestResults/performance/load-inspector-regression
Remove-Item Env:YHAB_LOAD_SECONDS, Env:YHAB_LOAD_PLANS
```

#### Cold rebuild profiling and combined dimensions

Release EventPipe captures isolated the cold replay with test-harness markers,
excluding fixture seeding and full-ledger assertion reads. The marked captures
reported zero lost events and no missing allocation stacks. EF's entity-counting
interceptor caused per-row property dictionaries, delegates and closures: the
instrumented 100k replay allocated about 1.033 GB. A separate command-only control
with the same financial checks allocated 246.32 MB and took 993.4 ms. This is a
correction for measurement overhead, not a production speedup. Earlier counted
runs remain useful evidence of read counts and exact behavior; their allocation
and latency numbers include substantial harness overhead.

The remaining profile attributed roughly 31 MB to sorting single-split
transactions. `BudgetCalculator.ApplyEntry` now bypasses sorting for zero/one
split and retains deterministic ID sorting into a new array for multiple splits.
The identical command-only control then allocated 217.48 MB and took 907.2 ms:
28.85 MB (11.7%) less allocation. These are single local samples, not latency
guarantees. Process allocation is churn, not retained memory. The profiles are
under `TestResults/performance/cold-rebuild-{command-only,optimized}` with matching
`.nettrace` and `-analysis.json` files. Each corresponding test passed once with
zero failures/skips. A controller-only capture that executed no test was discarded
as validation; subsequent traces attached to MTP's actual test worker.

The expanded command-only test then ran three shapes sequentially in Release:

| Transactions / accounts / categories / months | Cold ms / allocated MB | Warm ms / allocated MB | Cold SQL commands |
| --- | ---: | ---: | ---: |
| 100k / 2 / 8 / 24 | 1,005.8 / 215.09 | 52.6 / 1.49 | 9 |
| 10k / 100 / 500 / 240 | 590.9 / 159.17 | 25.8 / 2.18 | 63 |
| 100k / 100 / 500 / 240 | 956.8 / 367.86 | 27.2 / 2.23 | 63 |

The last two include 120k allocations. Every shape checks all month fields and
category rows against full replay, separately checks independent cash conservation,
and verifies every generated opening month before saving/reusing checkpoints.
Counting was disabled, so `entities []` in this control does not mean zero rows
read. The cold interval excludes writing checkpoints; the warm interval covers the
month query. Entity-count regressions remain separate and unchanged. Evidence:
`cold-combined-final`, one test passed with zero failures/skips.

`FundedCreditPurchasesMoveCashToPaymentCategory` pins the single-split reserve
amount. `SplitStorageOrderCannotMoveACentBetweenCashAndCreditFunding` pins the
multi-split cent allocation and now explicitly verifies input order is unchanged.
The fresh `unit-cold-optimized` coverage run passed 543 tests with zero failures
or skips; source totals including generated lambdas are 261/261 lines and 184/184
branches for `BudgetCalculator`, 127/127 and 172/172 for `CatalogChanges`, and
184/184 and 246/246 for `TransactionChanges`. Existing long-history invariant and
monthly-transition checks provide independent amounts, repeatability and immutable
input assertions. Final cross-layer checks follow the refresh-read changes.

```powershell
dotnet test --project tests/YHAB.IntegrationTests/YHAB.IntegrationTests.csproj --configuration Release --filter-method '*ColdReplayWithoutEntityCountingRetainsExactBalancesAsync' --report-trx --results-directory TestResults/performance/cold-combined-final
```

#### Consistent workspace refresh investigation

An intermediate UI change prefetched the view and month and retried their revision
race at most three times, without resending a committed command. Fifteen focused
component cases passed, including selected-month preservation, save status,
bounded retry exhaustion and non-conflict failures. The ten-minute Release
experiment `workflow-refresh-final` passed its financial checks (one test, zero
failures/skips) but exposed an unacceptable performance result: 8,768/9,566 accepted
writes, 798 rejected writes, 15,522 read conflicts, 1,226 recovered refreshes and
4,652 exhausted refreshes. Only 4,116 accepted writes reached successfully refreshed
balances within the retry limit. Their p95 was 287.4 ms, including retries; the
all-attempt save/refresh p95 was 482.2 ms. These are local observations and the
successful-display series excludes exhausted attempts. The intermediate retry-only
approach is superseded in the current source, not presented as an optimization.

`PlanMonthView` now combines the navigation/catalog and budget month in one
repeatable-read database snapshot, exposed through the authenticated minimal
`GET /api/plans/{id}/workspace` endpoint. It reuses the owner/revision/date/month
cache and safe checkpoint publication. A concurrent write may supersede the
returned revision, but cannot mix revisions within the response. Explicitly
versioned month reads and all user writes retain their revision checks.
The UI publishes the paired values together, reuses that prepared month, and
keeps the saved message if display refresh fails. A bounded conflict fallback
remains for read errors; it never retries a mutation. The register still loads
independently without calculating a month.

The new database regression forces an income write after the workspace's snapshot
begins but before account/month reads finish. It checks old-revision cash of
$2,001,092, new-revision cash of $2,001,117, matching available/Ready to assign,
and rejection of stale writes. Separate checks cover cached ownership, missing
plans, requested/default months and date validation. These two database tests
and all fifteen workspace component cases passed independently, then passed
again in the complete regression gate below.

The isolated Release `workflow-combined-final` run passed one test, zero failures
or skips, after ten minutes of offered load and one minute idle. It used two web
processes, eight editors, one 100k-transaction plan and 730 due recurring dates:

| Observation | Combined workspace result |
| --- | ---: |
| Accepted writes / offered attempts | 9,535 / 9,599 |
| Rejected stale writes | 64 (0.7%) |
| Refresh conflicts / exhausted refreshes | 0 / 0 |
| Accepted write p95 | 35.2 ms |
| Save through successfully refreshed balances p95 / maximum | 147.3 / 904.5 ms |
| Initial workspace read before editing | 1,542.8 ms |
| Recurring catch-up observed | 80.1 seconds |
| Web-process private memory at load end | 160.46 / 161.27 MB |
| Web-process private memory after 60 seconds idle | 160.31 / 159.54 MB |

All 9,535 accepted writes reached a successfully refreshed projection. Latency
includes read recovery if needed, but excludes a user retry of a rejected write;
none was replayed automatically. These HTTP intervals end when projection data
arrives; they do not measure browser painting or a deployed network. Every receipt,
per-editor assignment, recurring date, final balance and the 50-entry history
limit was checked, with no error logs.
The sampled private-memory peaks were 347.80/410.55 MB during warm-up/catch-up.
These are process samples, not retained-object sizes. The earlier 132.5 ms
checkpoint-run refresh figure included failed refresh attempts without following
them to successful display, so it is not an equivalent end-to-end comparator.
Unrelated applications were running on the workstation; these observations are
descriptive, not controlled capacity or latency guarantees.

```powershell
$env:YHAB_LOAD_SECONDS = '600'
$env:YHAB_LOAD_PLANS = '1'
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --configuration Release --filter-class '*ProcessLoadTests' --report-trx --results-directory TestResults/performance/workflow-combined-final
Remove-Item Env:YHAB_LOAD_SECONDS, Env:YHAB_LOAD_PLANS
```

#### Final foundation audit and validation

The original architecture report and its follow-up scope were checked against the
current implementation, assertions and recorded measurements. The completed work
covers these boundaries:

| Boundary | Evidence checked |
| --- | --- |
| Pure calculations and targets | Literal money-conservation expectations, five/ten-year histories, 80 dated targets, rollover boundaries, repeatability and unchanged inputs in `CalculationInvariantTests`, `MonthlyTransitionTests` and target tests. |
| History and uncertain saves | Pure history/patch policies; all 50 retained changes; empty/exhausted history; failed-save and failed-undo rollback; competing undo/edit; receipts and operation identity; UI pending/failed-refresh states and real text-field keyboard ownership. |
| Recurrence and ordering | Pure bounded calendars/planning and supplied identities; explicit financial sequence; scheduled-date uniqueness; cursor/occurrence atomicity; multi-instance posting and continued catch-up during edits; automatic posts preserve user history. |
| Selective reads and writes | Every command type at 10k/50k/100k, sparse history/retries, SQL report aggregates, indexed recurring discovery, affected-row bulk work, private projections and register cursors. Explicit full-plan reads retain their documented full-read contract. |
| Checkpoint correctness | Full-output comparisons plus independent cash checks; backdating, future assignments, targets, undo, financial resets, publication races, owner isolation and bounded invalidation metadata. |
| Workspace consistency | Forced read/write interleaving, cache ownership, date/month selection, stale-write rejection, paired publication and bounded read recovery without mutation replay. |
| Scale and memory | Combined 100k-entry/500-category/20-year cold replay, wide catalogs/allocations, isolated Release HTTP load, server memory/GC, 600-cycle browser control and diagnostic-retention attribution. |

The assertion review considered incorrect ordering, lost cents, input mutation,
partial rollback, stale publication, foreign cache reuse, unbounded retries and
resending committed commands. Independent expected balances and observable state
checks complement full-replay comparisons. This was an assertion review, not an
empirical mutation-testing campaign. The saved `unit-cold-optimized` coverage
report still matches the current pure-policy sources; subsequent changes affect
the workspace projection/orchestration and its boundary tests.

The final full solution run passed **880 tests: 543 unit, 261 component, 66
PostgreSQL integration, one Aspire and nine browser-project cases; zero failures
and zero skips**. Test projects ran sequentially to avoid cross-project resource
contention, preserving normal parallelism within each project. These are
correctness results; use the isolated Release measurements for performance.
The final solution build passed with zero warnings/errors, both formatting
commands exited zero, and `git diff --check` passed. Test-owned applications
were disposed. Reports and build/format logs are under `TestResults/performance`.

```powershell
dotnet format YHAB.slnx --severity warn
dotnet format YHAB.slnx --severity warn --verify-no-changes
dotnet build YHAB.slnx
dotnet test --solution YHAB.slnx --no-build --max-parallel-test-modules 1 --report-trx --results-directory TestResults/performance/final-workspace
```

The measured bottlenecks in this campaign are addressed. Cold reconstruction,
relevant database scans and genuinely large edits still have costs proportional
to their inputs. The next performance validation should use representative
hosting, agreed latency/memory budgets and a multi-hour soak, including concurrent
cold starts. Current evidence does not establish deployment capacity, live WASM
managed-object retention or every possible financial combination. Those limits
do not reopen the earlier coverage gaps or the resolved full-ledger workflows.

Documentation review covered `AGENTS.md`, root/build/test READMEs and the existing
budgeting guide. Test guidance, endpoint/refresh behavior and this report were
updated; setup and build conventions remain accurate.

## Compact summary and navigation follow-up

The monthly budget now combines Ready to assign, funding actions, and the four
totals in one sticky panel. It stays within the budget section while the document
scrolls normally. Focus handling keeps category controls and editor headings
below the panel. The desktop shell has a **Hide menu / Show menu** button whose
local preference survives enhanced navigation and reloads; the phone drawer is
independent. Both changes preserve the static shell and existing render modes.

The browser regressions assign the final $700 in the last category and verify
that Ready to assign reaches zero while visible, then undo to restore $700.
They also check summary height, backward keyboard focus, reclaimed navigation
width, keyboard restoration, reload/enhanced-navigation persistence, and a mobile
drawer round trip after hiding the desktop menu. Initial browser runs caught an
unnecessarily full-width action stack and lost keyboard focus after enhanced
navigation. The stack now uses its content width. Trace inspection showed that
SSR patches removed Fluent's browser-added tabindex from the menu toggle;
explicit `tabindex="0"` preserves it. The test also waits for the mobile dialog
to leave the modal top layer before testing keyboard access to the page.

The corrected summary measures about 138 px at 1440 px desktop width and 196 px
at 390 px phone width with the walkthrough plan's balances. Manual wheel scrolling
held it 8 px from the viewport top over the lower categories. Light/dark checks
showed no page overflow; focused phone amounts remained visible beneath it.

Validation: solution build passed with zero warnings/errors; 294 unit tests and
247 component tests passed (zero failures/skips). The final full browser attempt
passed six of seven cases; after the explicit-tabindex correction, both navigation
cases passed in a focused rerun (zero failures/skips), resolving the remaining
failure. No persistence or accounting rules changed.

Commands for this follow-up, from the repository root:

```powershell
dotnet build YHAB.slnx
dotnet test --project tests/YHAB.UnitTests/YHAB.UnitTests.csproj --no-build
dotnet test --project tests/YHAB.ComponentTests/YHAB.ComponentTests.csproj --no-build
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --no-build
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --no-build --filter-class '*NavigationTests'
dotnet format YHAB.slnx --severity warn
dotnet format YHAB.slnx --severity warn --verify-no-changes
```

## Spacing, scrolling, and amount-editing follow-up

The later October 3 pass reviewed six additional screenshots and the assignment
recording. Changes use the existing Fluent 5.0.0 components and theme tokens;
there are no Identity model changes or migrations. The account link now says
**Your account**, as requested.

| Finding | Resolution / evidence |
| --- | --- |
| UI-07: mouse-wheel scrolling did not reach lower categories | Reproduced with a 1,921 px document inside a 720 px viewport: body overflow was hidden and wheel input left `scrollY` at zero. Override Fluent's baseline body height/overflow for document scrolling. Real wheel tests reach the document bottom at desktop and phone widths. |
| UI-08: navigation had disconnected gray/black panels | Main, plan, and Identity settings navigation now use transparent resting backgrounds with the subtle Fluent hover token. Active indicators and keyboard focus remain. |
| UI-09: editor spacing compounded and controls stayed narrow | Fluent's 12 px field margins stacked with 16 px layout gaps. Editors now use zero field margins, full-column text/select controls, and an account grid that collapses to one column on phones. Checkbox sizing stays intrinsic. |
| UI-10: register search sat below its neighboring selectors | Top-align labeled fields instead of bottom-aligning different-height field wrappers. Browser geometry checks compare the actual control hosts. |
| UI-11: assignment updates remounted every amount control | Remove the plan revision from input keys; retain category/month identity. Pending fields are read-only instead of disabled, and draft text stays steady until the confirmed result arrives. Browser checks preserve the original DOM input and keyboard focus. No decorative animation was added. |
| UI-12: empty amounts failed validation and typing required clearing first | Focus selects all; a subsequent click can position the caret. Blank/whitespace commits zero, including clearing an existing zero. Equal-valued expressions do not create another save. Component cases check normalization, invalid-input recovery, and successful/rejected pending writes. |
| UI-13: long emails wrapped in the shell | A compact **Your account** link replaces the email label; the email remains on account pages. Authentication/navigation tests retain guest and logout coverage. |
| UI-14: narrow register columns split Outflow/Inflow headings, and split-row removal was above adjacent controls | Keep numeric headings on one line within the horizontally scrollable table; align the split remove action with its memo input. Budget amounts now target the Fluent input's actual shadow part for right alignment. |
| UI-15: phone transaction selectors protruded into card padding | Fluent's shadow control has a 160 px minimum width even when its host is narrower. Stack money-direction/amount and repeat/flag fields below the small breakpoint; assert the visible dropdown border fits its host, not just the document. |

Positive balances and available funds use green, negatives/outflows use red, and
funding gaps use amber across the plan, account summary/register, and report
summaries. Numeric signs and descriptive labels remain visible. Screenshot checks
also cover forms and dropdown surfaces in both themes.

Validation used `dotnet build YHAB.slnx` (zero warnings/errors), the complete unit
and component projects (294 and 247 passed), and all seven Playwright cases
(seven passed, zero failed/skipped), including the 200/400/600-transaction
household checkpoints and server/WebAssembly transitions. Targeted regression
evidence includes `ClearingAnAmountCommitsZeroAndClearsValidationAsync`,
`UnchangedAmountsDoNotCreateAnotherSaveAsync`,
`PendingSaveKeepsDraftThenDisplaysTheParentResultAsync`, and
`ManualPlanPurchaseUndoAndReportsAgreeAsync` at both widths. The authenticated
navigation assertion now expects the requested compact label.
After the final numeric-heading, split-action alignment, and phone dropdown changes, the full
solution build, 294 unit tests, 247 component tests, and both affected browser
workflows passed again (zero failures/skips); the focused browser rerun took
40 seconds. Formatting application and verification both exited zero.

Early development runs caught zero-normalization and test expectation issues.
One full browser attempt was interrupted by starting the manual Aspire preview
while its test AppHosts were active (two passed, four failed before interruption,
one unfinished). That result was discarded as a validation-session collision;
the clean seven-case rerun completed in 1m 54s. Run preview and Aspire-based
tests sequentially. No test parallelism settings or application readiness
dependencies were weakened.

The hands-on pass covered light/dark navigation, account creation layout,
category targets, groups, moving money and undo, splits, recurring-frequency
controls, reconciliation, account filters, reports, plan settings, and Identity
navigation. A $25 move reduced Ready to assign from $2,000 to $1,975; undo
restored $2,000. Both themes and the 390 px phone layout were inspected. The final
phone transaction check measured matching 309 px host/border widths for all six
dropdowns. Browser warning/error logs and YHAB warning/error telemetry were empty.
Aspire's existing ENV-02 recurred at 20:51:24 UTC; it remains a hosting dependency
issue, not a clean-host claim. The temporary preview was stopped normally after
validation; the database volume was retained.

Commands for this UI pass, from the repository root:

```powershell
dotnet build YHAB.slnx
dotnet test --project tests/YHAB.UnitTests/YHAB.UnitTests.csproj --no-build
dotnet test --project tests/YHAB.ComponentTests/YHAB.ComponentTests.csproj --no-build
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --no-build
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --no-build --filter-class '*BudgetWorkflowTests'
dotnet format YHAB.slnx --severity warn
dotnet format YHAB.slnx --severity warn --verify-no-changes
```

## UI and manual workflow follow-up

On October 3, 2026, a separate synthetic local account and **Household walkthrough**
plan were used for hands-on browser checks. No real YNAB plans or existing YHAB
user data were changed. This pass also inspected the user's two screenshots and
Aspire startup log. Raw startup logs remain outside Git because dashboard login
URLs contain access tokens.

| Finding | Resolution / evidence |
| --- | --- |
| UI-01: every Fluent dropdown contained an extra border and clipped text | Global native button styling applied to the dropdown's slotted button: 1 px border and 6.4 / 12 px padding inside a 20 px line box. Exclude slotted buttons while keeping low selector specificity, so native Identity buttons and category links retain their styling. Browser checks assert zero inner border/padding in budget, account, and transaction selectors. |
| UI-02: workspace status and filter actions were misaligned | Set Fluent's explicit vertical alignment, remove default field margins in filter rows, separate the history toolbar from the heading, and allow pagination to wrap. Desktop geometry and phone overflow checks guard the result. |
| UI-03: dark styling changed after enhanced navigation | Blazor replaced the body's client-added theme marker while Fluent's dark tokens remained active. Reapply Fluent's saved theme on `enhancedload`; retain System behavior and static SSR navigation. Tests exercise navigation in light, dark, and System mode, plus OS changes. |
| UI-04: reconciled transactions looked editable and displayed an unchecked cleared state | The server already rejected these edits. The editor now explains how to unreconcile, disables Save and the clearing toggle when either transfer side is reconciled, and displays the actual source clearing state. Four component cases cover both locks and an unlocked transfer. |
| UI-05: an empty category filter showed only column headings | Add an explicit no-matches message. Browser tests select an empty Hidden view and restore All categories. |
| UI-06: opening an editor from a low register row left it offscreen | Reproduced on a 390 px phone viewport: the transaction editor appeared above the current scroll position. The shared editor surface now focuses its heading after its first interactive render, bringing it into view without interrupting subsequent input. Browser checks cover account, transaction, and reconciliation editors. |
| LOG-01: repeated antiforgery cache-header warnings | Plan pages and API responses now use `Cache-Control: no-cache, no-store` and `Pragma: no-cache`, matching antiforgery requirements. The subsequent manual run returned no YHAB warning/error telemetry; authenticated browser checks verify the headers on snapshots and tokens. |
| ENV-02: Aspire's ContainerExec watcher terminates after 60 seconds | Reproduced in Aspire 13.6.0 after healthy startup. Remains open in the dependency; see investigation below. |

Manual checks used actual rendered controls, including opening dropdowns, typing
arithmetic, clicking actions, and inspecting screenshots. The core checkpoint was:

- Opening checking $5,250 (entered as `5000 + 250`), savings $3,000, card -$400.
- Assign $6,100; set a $650 grocery target and fund its $50 shortfall. Ready to
  assign becomes $2,100 with $6,150 assigned.
- Post a cleared $120 card grocery purchase; split an $80 checking purchase
  into $60 groceries and $20 shopping. An unbalanced $79 split disables Save.
- Transfer $500 checking to savings and pay $200 from checking to the card.
- Create a $14.99 monthly subscription due today, approve its occurrence,
  verify the next instruction is November 3, and refresh without duplicating it.
- Move $20 from groceries to shopping to cover cash overspending, undo and redo.
- Bulk-clear checking entries; a $1 reconciliation mismatch requires explicit
  adjustment consent. Reconcile the matching $4,455.01 balance.
- Create a Seasonal group and Holiday meals category; assign $100, hide it,
  retrieve it with Hidden, and move its history to Gifts. Ready to assign is $2,000.
- Rename the plan, save notes, rename the subscription payee, and filter reports
  to October 1–3. Spending is $214.99: groceries $180, shopping $20,
  subscriptions $14.99. Account transfers and payments do not become expenses.

At this checkpoint, balances are checking **$4,455.01**, savings **$3,500**,
card **-$320**, with **$320** available for card payment. Net worth is
**$7,635.01** ($7,955.01 assets less $320 debt). The later $100 category
assignment and history move do not alter these account/report totals.

The final phone pass verified the renamed **Family streaming** payee in both its
posted and recurring entries (next occurrence November 3). Selecting November
preserved $5,955.01 of category balances without posting future recurring activity.
Assigning $300 there reduced Ready to assign to $1,700 and appeared as a future
assignment when returning to October; Undo restored $2,000. A one-off transaction
dated October 4 displayed the unsupported-future-date message and disabled Save.
The reconciled split editor displayed its clearing state, lock notice, and disabled
Save correctly. Mobile dropdown filtering, light/dark navigation, and document
overflow checks passed. The skip link remained outside the viewport when unfocused.

Final active-run YHAB telemetry returned no warnings/errors, and the browser had
no warnings/errors for that run. The separate AppHost ContainerExec timeout
recurred at 17:52:08 UTC; it remains ENV-02. Validation used ordinary Aspire shutdown
and preserved the development database.

Screenshots from the synthetic walkthrough are local ignored artifacts under
`artifacts/ui-validation`: `desktop-dark.jpg`, `phone-light.jpg`,
`phone-dropdown.jpg`, and `phone-editor.jpg`. Automated screenshots also cover
desktop/phone light and dark views under the Playwright project's `TestResults`.

### Automated follow-up results

The complete five-project run passed **551 tests, 0 failed, 0 skipped** in
2 minutes 11 seconds: 294 unit, 241 component, 8 PostgreSQL integration,
1 Aspire integration, and 7 Playwright tests. This includes the existing
one-/two-/three-month scenarios at up to 3,000 calculation/persistence transactions
and 600 browser transactions. Evidence is in `artifacts/ui-regression.log` and
the five TRX files in `artifacts/ui-regression`. This run preceded the final
editor-focus enhancement, whose follow-up validation is recorded below.

```powershell
dotnet build YHAB.slnx
dotnet test --solution YHAB.slnx --no-build --report-trx --results-directory artifacts/ui-regression
dotnet format YHAB.slnx --severity warn
```

After the editor-focus change, a fresh solution build passed with zero warnings
and errors; unit tests passed **294/294** and component tests **241/241**. The
seven-case browser rerun passed five cases but the two extended workflow cases
failed an incorrect native `IsEnabledAsync` assertion on Fluent's custom button
host. Their focus/viewport assertions had passed. The assertion was corrected to
inspect Fluent's `disabled` attribute, then both affected desktop/phone cases
passed (**2 passed, 0 failed, 0 skipped**). No product change was needed for that
test-harness failure. The final five other browser cases were not redundantly rerun.

```powershell
dotnet test --project tests/YHAB.UnitTests/YHAB.UnitTests.csproj --no-build --report-trx --results-directory artifacts/ui-final
dotnet test --project tests/YHAB.ComponentTests/YHAB.ComponentTests.csproj --no-build --report-trx --results-directory artifacts/ui-final
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --no-build --report-trx --results-directory artifacts/ui-final
dotnet build YHAB.slnx
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --no-build --filter-class YHAB.PlaywrightTests.BudgetWorkflowTests --report-trx --results-directory artifacts/ui-editor-final
dotnet format YHAB.slnx --severity warn
dotnet format YHAB.slnx --severity warn --verify-no-changes
```

Documentation review checked `AGENTS.md`, the root README, `build/README.md`,
`tests/README.md`, and the existing budgeting feature documentation. Updated
theme/styling setup, editor behavior, browser-test conventions, and this findings
record. Durable agent and build rules remain accurate and unchanged.

The manual pass complements the deterministic one-, two-, and three-month tests
below. It is not a claim that every Identity ceremony, browser engine, or possible
financial edge case was manually exercised.

### Aspire log investigation

The repeated `AuxiliaryBackchannelService` connection-reset exceptions in the
attachment are logged at Debug when tooling clients disconnect. Aspire's
[connection handler](https://github.com/microsoft/aspire/blob/56f3e9c0d216c0c7069dabb49dd0464e4827744f/src/Aspire.Hosting/Backchannel/AuxiliaryBackchannelService.cs)
explicitly treats that reset as an expected disconnection.

The **Critical** ContainerExec timeout is different and must not be dismissed as
debug noise. The installed 13.6.0 package's checksum-verified
[KubernetesService source](https://github.com/microsoft/aspire/blob/56f3e9c0d216c0c7069dabb49dd0464e4827744f/src/Aspire.Hosting/Dcp/KubernetesService.cs)
awaits the watch response inside a finite API timeout. The observed stack ends in
that watch setup while awaiting stream data. This is consistent with an idle
ContainerExec watch not completing setup before the 60-second startup budget;
that explanation is an inference, not a verified upstream fix. PostgreSQL and
YHAB remain Running/Healthy and migrations Finished throughout the reproduction.
ContainerExec monitoring is degraded even though the application works.

The official package query still listed 13.6.0 as the newest stable release.
No logging suppression, reflection patch, dummy container execution, package
downgrade, or timeout inflation was added. The warning remains visible and needs
an upstream Aspire correction. Recheck the exact version before applying a future
patch and validate resource-command monitoring as well as web readiness.

Browser console connection errors during intentional AppHost shutdown were
separated from active-run errors by endpoint and timestamp.

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

## Plan density and inline catalog editing — October 9, 2026

The plan workspace now uses a compact month/summary header and a 14rem plan
sidebar shared with transactions, reports and settings. The global menu collapses
on plan routes when no explicit preference is saved. Account names and balances
share a row; installation controls live in the footer while update notices remain
above page content. The shell still uses document scrolling and static Identity
navigation/forms.

Groups and categories can be created and renamed inline. Categories and whole
groups can be dragged into display order; clicking a handle exposes keyboard and
touch move controls. Selecting a category opens its target panel beside the table
on wide screens and below it on narrow screens. Ordering is revision-checked,
atomic and undoable, preserving targets, allocations and transaction history.

At a 1440 × 960 viewport, the browser check requires at least ten fully visible
categories with targets, row heights of 36–64 pixels, and the first category
above 320 pixels. The 390-pixel check covers collapsed plan navigation, absence
of document overflow, editing and persisted target changes.
These measurements use a disposable seeded plan, not the production plan in the
reference screenshot. Desktop/phone screenshots and target-panel screenshots
are saved under the Playwright output's `TestResults` directory.

| Requirement | Evidence |
| --- | --- |
| Inline creation, rename, real desktop dragging, phone moves, targets and reload persistence | `PlanCatalogTests.InlineCatalogEditingOrderingAndTargetsPersistAsync` (1440 and 390 pixels). |
| Assignment refresh preserves an unfinished target | `BudgetCatalogTests.AssignmentRefreshPreservesAnOpenTargetDraftAsync`. |
| Rename preserves existing category metadata | `BudgetCatalogTests.CreatesGroupAndRenamesCategoryWithoutReplacingTargetAsync`. |
| Invalid destinations fail; ordering preserves financial data | `CatalogOrderingTests.RejectsMissingForeignAndSelfDestinations` and `CategoryMoveAcrossGroupsPreservesTargetsAndFinancialHistory`. |
| Revisions, stale writes, undo and redo work in PostgreSQL | `BudgetPersistenceTests.ReorderingPersistsAtomicallyAndSupportsUndoRedoAsync`. |
| Existing money, navigation and installation/update flows still work | `BudgetWorkflowTests`, `NavigationTests` and `PwaTests`. |

Final completed test runs:

| Layer / selection | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Full unit project | 567 | 0 | 0 |
| Full component project | 274 | 0 | 0 |
| PostgreSQL ordering case | 1 | 0 | 0 |
| Plan catalog, budgeting workflow and navigation browser classes | 6 | 0 | 0 |
| PWA browser class | 5 | 0 | 0 |
| Total | 853 | 0 | 0 |

The initial combined 11-case browser run had nine passes and two AppHost startup
timeouts, before those two cases opened a browser. The final browser runs above
used separate class batches without changing timeouts or parallel settings and
both exited zero. This is targeted infrastructure validation, not a full-solution
test run. The full solution build passed with zero warnings and errors.

Commands run from the repository root after the final source changes:

```powershell
dotnet format YHAB.slnx --severity warn
dotnet build YHAB.slnx
dotnet test --project tests/YHAB.UnitTests/YHAB.UnitTests.csproj --no-build
dotnet test --project tests/YHAB.ComponentTests/YHAB.ComponentTests.csproj --no-build
dotnet test --project tests/YHAB.IntegrationTests/YHAB.IntegrationTests.csproj --no-build --filter-method '*ReorderingPersistsAtomicallyAndSupportsUndoRedoAsync'
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --no-build --filter-class '*PlanCatalogTests' --filter-class '*BudgetWorkflowTests' --filter-class '*NavigationTests'
dotnet test --project tests/YHAB.PlaywrightTests/YHAB.PlaywrightTests.csproj --no-build --filter-class '*PwaTests'
dotnet format YHAB.slnx --severity warn --verify-no-changes
```

The formatter needed to run outside the agent sandbox because its build-host
named pipe was inaccessible inside it. No analyzer settings, test settings,
packages or production data were changed. Documentation review covered the
usage guide, setup guide and test guide; agent/build conventions remain accurate.
