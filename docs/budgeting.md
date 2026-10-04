# Manual budgeting

YHAB follows the envelope workflow: add the money you have, assign it to
categories, record spending, and adjust your plan. Plans belong to the signed-in
Identity user. There are no bank connections, imports, shared plans, or photo
storage.

## Everyday workflow

1. Create a plan from **Your plans**. Suggested category groups are optional.
2. Add checking, savings, cash, credit, loan, or tracking accounts. Enter an
   opening date and a signed opening balance; money owed is negative.
3. Assign money in the monthly table. Amount fields accept decimal arithmetic
   such as `150 + 25`, `(600 - 120) / 3`, and dollar/comma formatting.
   Clicking or tabbing into an amount selects its current value; a second click
   can position the caret. Clearing the field and leaving it commits zero.
4. Add transactions in an account or **All transactions**. Outflows need a
   category; income can go to Ready to assign. Split amounts are signed and must
   total the transaction amount exactly.
5. Transfer between accounts using the transfer selector. One stored transfer
   supplies both sides, and each side has its own clearing status. Transfers
   within the budget have no spending category. Transfers across the budget and
   tracking boundary require categories when money leaves the budget.
6. Reconcile an account against its posted bank balance. Mark cleared entries
   first. A difference requires explicit approval of an adjustment; matching
   cleared entries become reconciled. Unreconcile before editing or deleting
   locked entries.
7. Review spending, income/expense, and net worth in **Reflect**. Reports include
   the selected date range and exclude recurring templates and duplicate
   within-budget transfers.

Use category details to set targets, notes, display order, hidden status, and
monthly target snoozes. Remove a used category by moving its history to another
category. Group management supports names, order, and hidden groups. Account
details support notes and closing zero-balance accounts. Loan accounts record
rates and minimum payments; interest and payments are entered manually.

Plan settings rename the plan, store notes, and rename payees throughout posted
and recurring entries. The register supports search, date/status filters, sorting,
flags, duplication, approval, clearing, and bulk deletion. It displays 50 rows per
page. Horizontal table scrolling preserves the surrounding mobile layout.
Opening an editor focuses its heading and brings it into view, including when
editing from a register row below the editor's position on a phone.
Reconciled entries show their locked state in the editor and cannot be saved.
Select the entry in the register and use **Mark uncleared / unreconcile** first;
either reconciled side of a transfer locks the whole entry. Empty category views
explain that the selected filter has no matching categories.
Assignment saves preserve the existing inputs and keyboard focus. Amount fields
are temporarily read-only while a save is pending; a failed save restores the
last confirmed amount alongside the workspace error. Green marks positive money,
red marks negative balances/outflows, and amber marks funding still needed.
Signs, labels, and overspending messages retain the meaning without color.
The compact monthly summary stays at the top of the viewport as you scroll the
category table, keeping Ready to assign and all four totals visible. The page
retains normal document scrolling; keyboard focus and editor headings stay below
the summary. On desktop, **Hide menu / Show menu** in the site header frees space
for the plan and remembers your preference across navigation and reloads.

## Monthly rules

- Positive category balances carry forward. Negative categories reset at the
  month boundary. Cash overspending reduces next month's Ready to assign;
  unfunded credit spending becomes debt.
- Funded credit purchases move money from their spending category into the
  corresponding credit payment category. Payments use that reserve. Opening
  credit debt needs an explicit assignment to its payment category.
- Within each month, credit refunds first offset the most recent earlier
  purchases on the same card and in the same category. These cancellations
  do not become payment reserves available to transfers or cover cash
  overspending. Excess refunds can fund other spending, with cash spending
  taking priority; refunds received before a later purchase remain separate.
- Balance transfers move only the payment money available at the transfer;
  later purchases retain their own reserves.
- A positive credit balance behaves like spendable cash until depleted.
- Future assignments reserve money from Ready to assign. Moving money changes
  assignments without inventing income or altering account balances.
- Monthly/weekly refill targets consider positive carryover once that month
  arrives. Set-aside targets ask for new contributions. Balance targets track
  the amount still available. Dated targets spread remaining contributions across
  the months left; refill targets count spending within their target period.
- **Fund targets** uses only positive Ready to assign and prioritizes overspent
  categories, then due dates and category order. Hidden and snoozed categories
  are excluded.

Rules were informed by YNAB's
[month rollover guide](https://support.ynab.com/en_us/when-the-month-rolls-over-a-guide-rkyyd6qC9)
and [target guide](https://support.ynab.com/how-to-use-targets-rk5kkI9ks).
YHAB is an independent implementation, not a connection to or extension of YNAB.

## Recurrence and history

Repeating instructions can start in the future. One-off transactions cannot.
Due occurrences are materialized when an interactive plan opens, and when an
already-due repeating instruction is saved. They appear as **Needs approval**.
No background process posts while every client is closed; reopening catches up.
The plan's server date uses `Budgeting:TimeZone` (default `America/Chicago`).
Dates and amounts are date-only values and fixed decimal dollars.

Monthly recurrence retains its original day: January 31 becomes February 28,
then March 31. Editing an amount, memo, or other details preserves that anchor;
changing the next date or repeat frequency starts a new schedule from that date.
Posted occurrences have an immutable scheduled date separate from their editable
transaction date. Two occurrences can share a transaction date without being
posted twice. Closing an account pauses instructions involving that account.
Use the register's **Recurring** view to edit or delete future repeating
instructions without removing already-posted occurrences.

The latest 50 mutations have durable undo/redo history. Ctrl/Cmd+Z and
Ctrl/Cmd+Shift+Z invoke history outside editable controls. A new mutation after
undo replaces the redo branch. Refresh resolves a stale plan revision before a
new edit; conflicting edits never silently overwrite another tab's changes.

## Application boundaries

- Shared DTOs and pure calculations: `YHAB.SharedKernel/Budgeting`.
- Browser-compatible UI: `YHAB.UI/Features/Budgeting`.
- EF entities, mapping, commands, authenticated services and minimal endpoints:
  `YHAB/Features/Budgeting`.
- WASM transport: `YHAB.Client/Services/HttpBudgetClient`.

Plan selection, settings, reports, the shell, and Identity forms default to
static SSR with enhanced navigation. The budget/register workspace uses
InteractiveAuto. Each renderer loads an owner-scoped snapshot: once for static
prerendering and again when interactivity starts. The complete ledger is not
serialized into persistent prerender state, which would grow beyond SignalR's
startup message limit. Server rendering calls the application service directly;
WASM uses the authenticated same-origin API. Interactive loading and refresh post
due recurring entries after the snapshot arrives; static SSR never posts them.
Fluent providers live inside the interactive workspace. Native static forms
retain named POST form mapping and antiforgery.

Each operation creates and disposes an `ApplicationDbContext` through
`IDbContextFactory`. Every plan lookup includes the owner ID. Composite foreign
keys prevent ledger references crossing plans. Decimal columns use precision
18/scale 2. A plan revision is an EF concurrency token; ledger changes and
bounded JSON history snapshots commit in one transaction. Reads do not track
entities and use a repeatable-read transaction so the ledger and its revision
stay consistent across queries. Migrations run through Aspire's existing migration resource.

`PreserveRecurringOccurrenceDates` backfills occurrence identities from their
existing transaction dates and moves the unique occurrence index to the scheduled
date. Older undo/redo snapshots remain readable. Downgrading to the previous
schema requires occurrence transaction dates to satisfy its old unique index;
the downgrade does not change transaction dates to force compatibility.

The client currently receives the complete owned plan ledger so month changes
and calculations can run locally. This favors personal manual-entry plans;
very large ledgers would need paged projections and more compact history.

## Minimal API

Development exposes authenticated OpenAPI at `/openapi/v1.json`. Cookie
authentication applies to `/api/plans`. Obtain a request token from
`GET /api/plans/token`; send it in `X-CSRF-TOKEN` for every mutation. DTOs carry
the current `version`. Problems use standard ProblemDetails:
400 validation, 401/403 authentication/authorization, 404 absent or non-owned
plan, and 409 stale revision. Responses are not cacheable.

| Operation | Route |
| --- | --- |
| List / create plans | GET / POST `/api/plans` |
| Read a plan | GET `/api/plans/{planId}` |
| Create account/group/category/transaction | POST `/api/plans/{planId}/{collection}` |
| Update an item | PUT `/api/plans/{planId}/{collection}/{resourceId}` |
| Assign money | PUT `/api/plans/{planId}/assignments` |
| Change clearing / approval | PATCH `/api/plans/{planId}/transactions/status` |
| Bulk delete entries | POST `/api/plans/{planId}/transactions/delete` |
| Move money / fund targets | POST `/api/plans/{planId}/money-moves` / `auto-assign` |
| Reconcile | POST `/api/plans/{planId}/reconciliations` |
| Plan settings | PUT `/api/plans/{planId}/settings` |
| Rename payee / merge category | POST `/api/plans/{planId}/payees/rename` / `categories/merge` |
| Undo / redo / post due repeats | POST `/api/plans/{planId}/undo` / `redo` / `recurring/post-due` |

Concrete command records are request contracts, not EF entities or OneOf unions.
See `src/YHAB/YHAB.http` for request examples. Existing Identity email delivery
and deployment configuration still need the normal environment-specific setup
described in the root README.

## Acceptance evidence

See the [three-month household validation](budgeting-validation.md) for growing
transaction datasets, independent monetary expectations, and campaign findings.

The budgeting checks complement the existing Identity tests. They use fixed
boundary values, fresh bUnit renderers, disposable PostgreSQL containers, and
isolated AppHosts. Browser tests exercise both desktop and phone widths.

| Requirement | Concrete evidence |
| --- | --- |
| Calculator inputs and invalid expressions | `EvaluatesBoundedDecimalArithmetic`, `RejectsInvalidOrUnboundedInput`, `CalculatorEmitsEvaluatedAmountAndRejectsInvalidExpressionAsync` |
| Monthly assignments, carryover, and cash overspending | `NextMonthAssignmentUsesTheDisplayedMonthAndEvaluatedAmountAsync`, `PositiveBalancesCarryForwardAndFutureAssignmentsReserveMoney`, `CashOverspendingReducesNextMonthReadyToAssign` |
| Credit debt, funded purchases, payments, and refunds | `CreditOpeningDebtDoesNotCreateSpendableMoney`, `FundedCreditPurchasesMoveCashToPaymentCategory`, `CardPaymentReducesReserveWithoutCountingAsAnotherPurchase`, `RefundRestoresCategoryAndReducesPaymentReserve` |
| Cross-card refunds, cash priority, and payment reserve timing | `CrossCardRefundFundsPurchasesRegardlessOfTransactionOrder`, `CrossCardRefundCoversCashSpendingBeforeCreditSpending`, `BalanceTransferDoesNotUseMoneyFromLaterPurchases`, `CashPaymentAfterBalanceTransferUsesTransferredReserve`, `PositiveCardBalanceUsedForTransferReservesOnlyBorrowedMoney` |
| Unfunded refunds, partial funding, and month rollover | `UnfundedRefundDoesNotReserveMoneyOnAnotherCardOrReduceNextMonthCash`, `PartialRefundReservesOnlyAssignedMoneyForRemainingCreditSpending`, `UnfundedRefundDoesNotCoverCashOverspending` |
| Refund cancellation and transferable reserves | `RefundOffsetsCannotFundAnEarlierBalanceTransfer`, `RefundBeforePurchaseReleasesOpeningReserveBeforeBalanceTransfer`, `PartialRefundsAcrossMultiplePurchasesPreserveOnlyFundedReserves` |
| Split validation and cent preservation | `InvalidSplitTotalsAndForeignCategoryAreRejected`, `SplitRoundingPreservesEveryCentOfPositiveCreditCrossing` |
| Targets and automatic assignment | `MonthlyBehaviorDistinguishesCarryContributionsAndBalance`, `DatedRefillCountsMoneyAlreadySpentDuringTheTargetPeriod`, `AutoAssignUsesOnlyReadyCashAndSkipsHiddenOrSnoozedTargets` |
| Recurring entries without one-off scheduling | `RejectsOneOffFutureEntriesButAcceptsRepeatingInstructions`, `DueRecurrencesArePostedOnceWithApprovalAndAnchorDayPreserved`, `FrequenciesRetainTheAnchorAcrossCalendarBoundaries`, `TwiceMonthlyClampsFebruaryAndReturnsToTheOriginalDays` |
| Recurring edits, occurrence identity, and upgrade compatibility | `EditingTemplateDetailsPreservesTheOriginalMonthEndAnchor`, `ChangingTemplateScheduleEstablishesANewAnchor`, `OccurrencesCanShareEditedDatesWithoutLosingIdentityOrHistoryAsync`, `MigrationBackfillsScheduledDatesAndLegacyHistoryCanStillBeRestoredAsync` |
| Transfers, reconciliation, and account locks | `TransferClearingUpdatesOnlyTheSelectedAccountSide`, `CreditBalanceTransferMovesFundedPaymentMoneyToTheNewCard`, `ReconciliationLocksOnlyClearedEntriesAndRequiresExplicitAdjustment`, `ClosingAnAccountRequiresZeroBalanceAndOpeningChangesRespectReconciliation` |
| Category history and payee management | `MergingAUsedCategoryPreservesAssignmentsSpendingAndAccountBalance`, `MergingAssignedCategoryPersistsHistoryAndSupportsUndoRedoAsync`, `PayeeRenameChangesMatchingPostedAndRecurringEntriesOnly` |
| Private plans, SQL integrity, durable history, and concurrent edits | `OwnerIsolationHistoryAndConcurrentWritesPersistAcrossContextsAsync`, `DatabaseRejectsCrossPlanReferencesAndAccountDeletionRemovesOwnedLedgerAsync` |
| Manual browser workflow, calculator entry, keyboard undo/redo, reports, reconciliation, and API guards | `ManualPlanPurchaseUndoAndReportsAgreeAsync` at 1440px and 390px |
| Themes, mobile navigation, and SSR document continuity | `FluentNavigationPreservesDocumentAndThemeChoice` at 1280px and 390px |
| Actual startup, migrations, and database readiness | `AppHostCompletesMigrationsAndServesHealthyApplication` |

See [the test guide](../tests/README.md) for commands and prerequisites. Passing
these checks does not establish exact parity with every YNAB edge case or
performance with very large ledgers.
