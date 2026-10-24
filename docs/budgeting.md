# Manual budgeting

YHAB follows the envelope workflow: add the money you have, assign it to
categories, record spending, and adjust your plan. Plans belong to the signed-in
Identity user and can be shared with other YHAB accounts. There are no bank
connections, imports, or photo storage.

## Everyday workflow

A new plan starts with a first-run path in the board itself: a primary
**Add your first account** card and a three-step checklist (account, categories,
assign money). The panel sits beside the category table on wide screens and above
it on narrow screens. The checklist follows the plan's actual progress, and
**Dismiss** and assignment completion are remembered per plan in the browser, so
changing months or reopening the plan does not reset the checklist while the
account card remains. **How this works** beside the monthly summary opens
carryover, arithmetic, dragging, and target help inline. **Fund targets** and
**Move money** appear once the plan has an account; when shown but unavailable
they state what is missing: Fund targets needs money in Ready to assign and a
visible target or overspent category, and Move money needs at least one
category.

1. Create a plan from **Your plans**. Suggested category groups are optional.
2. Add checking, savings, cash, credit, loan, or tracking accounts. Enter an
   opening date and a signed opening balance; money owed is negative.
3. Assign money in the monthly table. Amount fields accept decimal arithmetic
   such as `150 + 25`, `(600 - 120) / 3`, and dollar/comma formatting.
   Clicking or tabbing into an amount selects its current value; a second click
   can position the caret. Clearing the field and leaving it commits zero.
   A comma followed by one or two digits reads as a decimal fraction, so a
   keypad's `12,50` is 12.50 while `1,234.56` keeps its grouping.
   On a phone, editor amount fields ask for the numeric keypad. A numeric keypad
   has no minus key, so fields that accept a negative amount (an opening balance
   and split amounts) also show a sign control. The monthly assignment field stays
   positive and uses the decimal keypad; take money back out of a category with
   its **Available** pill instead. A typed negative assignment, including an
   expression that evaluates below zero, is rejected with an inline message.
   Account, transaction, move-money, and target editor amounts are committed by
   their Save action, even when the field has not blurred.
   Select a category's **Available** pill to move money without editing its
   assignment. A positive balance offers **Move money** to Ready to assign or
   another category; an overspent balance offers to cover that shortfall from
   Ready to assign or a category that has money.
4. Add transactions in an account or **All transactions**. The editor starts on
   Outflow and asks for a spending category whenever money leaves the budget;
   Ready to assign remains available for inflows, including transfers that put
   money into a budget account. Split amounts are signed and must total the
   transaction amount exactly. A transaction needs a non-zero amount.
5. Transfer between accounts using the transfer selector. One stored transfer
   supplies both sides. Transfers within the budget have no spending category.
   Transfers across the budget and tracking boundary require categories when
   money leaves the budget.
6. Review spending, income/expense, and net worth in **Reflect**. Reports include
   the selected date range and exclude recurring templates and duplicate
   within-budget transfers.

## Sharing a plan

A plan starts private to its owner. On **Plan settings**, the owner creates a
one-time share code and gives it to the person they want to add. Use **Copy code**
beside the generated code to copy it to the clipboard. If the browser blocks
clipboard access, select the code and copy it manually. The recipient
enters that code under **Join a shared plan** on the plans page. No email is sent.
Codes expire after seven days and stop working once redeemed, so access does not
depend on ownership of an unverified email address. Every code is stored as a
hash, and a code can be redeemed only once. Everyone with access can open and edit
everything the owner can. The owner can stop sharing at any time, and a shared
account can leave the plan. Removing access takes effect on the next request, and
a removed account no longer appears in its plans list or API responses. Only the
owner can create codes or remove accounts; creating a code does not change the
plan revision, balances, or history.

Create groups with **Category group** above the plan table, and add categories
with the **+** on a group header. New names are entered directly in the table.
Use **Rename**, double-click a name, or press F2 on a name to edit it inline;
Enter saves and Escape cancels. Names and assignment fields each use one roving
Tab stop for the visible rows. Up and Down move between rows in the same column.
Right moves from a name to its assignment; Left moves back only when the
assignment caret is at the start, and otherwise preserves normal caret movement.
Saving a rename restores focus to its name; **Rename** remains available to
pointer and assistive-technology users without adding another sequential Tab
stop. An open name draft survives a reorder of any category because the table
keeps it by category id outside the sortable subtree, whose unkeyed wrappers
recreate rows.
Empty groups remain visible so they can receive categories. Collapse individual
groups or all groups to focus the plan.

On phones the table stacks instead of scrolling sideways: the category and
Available share the first line, and Assigned and Activity share the second.
Each value carries its own label, and the assignment field uses the decimal
keypad. Compact row, group, and shell controls keep a 40-pixel touch target.
The Available pill is a button at every width, so covering overspending and
moving money out of a category work without a physical minus key.

Drag a category's move handle, with a mouse or by touch, to reorder it within
its group or drop it into another expanded group. The row follows the pointer,
the other rows make room, and the new order appears immediately while it saves;
a failed save restores the saved order. Focus a category row and press Enter or
Space, then Up or Down to move it within its group, or Left or Right to move it
to the top of the neighboring expanded group. Drag
group handles onto a group header to reorder whole groups; an end drop area
appears below the table. Clicking a handle opens **Move up / Move down** and, for
categories, a group selector; these are the touch alternative for moving groups.
Group move controls also hide or show the group; use the **Hidden** view to find
it again. SortableJS, which Fluent UI's sortable list uses, is served from
`YHAB.UI/wwwroot/lib/sortablejs` rather than a CDN. Fluent UI 5.0.1's sortable list
never releases its browser instance or callback reference, so
`YHAB.UI/wwwroot/sortable-lists.js` stops each list and disposes its reference
when the list leaves the page. The same script drops Fluent's source-side removal
callback, because the table applies a cross-group move from the destination's
add alone. It also starts that add only after Fluent restores the source DOM,
since the keyboard path would otherwise render while the row is detached. It
also restores keyboard focus to the moved category's actual row, including its
source row after a rejected save, once rendering settles, and abandons that
restoration when a newer interaction starts; this replaces Fluent's fixed focus
timer, which can fire before a server render arrives. The
category grid keeps table semantics through ARIA roles; the script gives Fluent's
item wrappers the role named by `data-item-role`. Keep the script until the
library handles these cases itself.
Each move saves atomically as one revision and one undoable change, including
normalizing tied display orders. Moving does not change assignments or history.

Select a category row or its name to open its target panel beside the table on
wide screens or below it on smaller screens. Assignment inputs keep their own
editing action. The panel supports targets and monthly snoozes; expand **Category
details** for names, notes, group, and hidden status. Remove a used category by
moving its history to another category. Account
details support notes and closing zero-balance accounts. Loan accounts record
rates and minimum payments; interest and payments are entered manually.

Plan settings rename the plan, store notes, rename payees throughout posted
and recurring entries, and manage who can open the plan. The register supports search, date filters, sorting,
flags, duplication, and bulk deletion. It displays 50 rows per
page. Horizontal table scrolling preserves the surrounding mobile layout.
Filtering, sorting and cursor paging run in PostgreSQL. The browser receives the
current page and its split details, not the entire transaction history. Running
balances include earlier entries even when a search or date filter hides them.
Transaction editors load payee suggestions separately across the plan, including
payees outside the current register page or account. Suggestions refresh with the
catalog and remain case-insensitively deduplicated in the editor.
Opening an editor focuses its heading and brings it into view, including when
editing from a register row below the editor's position on a phone.
Empty category views
explain that the selected filter has no matching categories.
Editor fields are a comfortable tap size, and a transaction editor collects the
payee, direction, and amount before the account and transfer. On phones a long
editor keeps its title, **Close**, and primary save action on screen while the
rest of the form scrolls.
Assignment saves preserve the existing inputs and keyboard focus. Amount fields
are temporarily read-only while a save is pending; a failed save restores the
last confirmed amount alongside the workspace error. Green marks positive money,
red marks negative balances/outflows, and amber marks funding still needed.
Signs, labels, and overspending messages retain the meaning without color.
The compact monthly summary stays at the top of the viewport as you scroll the
category table, keeping Ready to assign and all four totals visible. The page
retains normal document scrolling; keyboard focus and editor headings stay below
the summary. On desktop, **Hide menu / Show menu** in the site header frees space
for the plan and remembers your preference across navigation and reloads. Without
a saved preference, the global menu is collapsed within a plan, leaving a single
compact plan sidebar. On smaller screens, open **Plan & accounts** for navigation;
a new plan's **Add your first account** action stays in the board, so starting does
not require the drawer. The same sidebar is used by transactions, reports, and plan
settings. Installation
controls remain available in the site footer.

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
- **Fund targets** is available when Ready to assign is positive and a visible
  category has an unfunded target or an overspent Available balance. It uses
  only positive Ready to assign and prioritizes overspent categories, then due
  dates and the displayed group/category order. Hidden and snoozed categories
  are excluded.

Rules were informed by YNAB's
[month rollover guide](https://support.ynab.com/en_us/when-the-month-rolls-over-a-guide-rkyyd6qC9)
and [target guide](https://support.ynab.com/how-to-use-targets-rk5kkI9ks).
YHAB is an independent implementation, not a connection to or extension of YNAB.

## Recurrence and history

Repeating instructions can start in the future. One-off transactions cannot.
While the web host is running, a background worker discovers up to 32 due plans
at startup and every 30 seconds. Each plan posts at most 128 scheduled occurrences
per batch; later batches continue from its persisted cursor. An interactive plan
open and saving an already-due instruction also catch up. Static SSR reads never
post. Closed accounts pause
instructions on either transfer side. `Budgeting:AutomaticPosting=false` disables
the background worker while preserving interactive catch-up.
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

The latest 50 nonempty user changes have durable undo/redo history. Ctrl/Cmd+Z and
Ctrl/Cmd+Shift+Z invoke history outside editable controls. A new mutation after
undo replaces the redo branch. Refresh resolves a stale plan revision before a
new edit; conflicting edits never silently overwrite another tab's changes.
Automatic posting does not consume a history slot or discard redo. History stores
only changed records and restores those records while preserving unrelated later
entries. If a later automatic entry depends on a record being removed or conflicts
with the fields being restored, undo fails with a conflict message rather than
deleting those entries.
Bulk payee renames store only the affected entry IDs and their old/new names.
Undo restores the exact original spelling, preserving unrelated fields and later
recurring schedule advances. It fails if an affected entry was removed or its payee
was changed afterward. Occurrences posted after a rename keep their posted name.

Each command carries an operation ID. A lost response can be retried with the
same ID and identical payload without saving twice; using that ID for different
content is rejected. A confirmed save followed by a failed refresh is shown as
saved, with editing disabled until refresh succeeds. An uncertain write offers
**Retry save safely**. Revision checks every 30 seconds offer a refresh notice
without replacing an in-progress entry. Receipts remain for the plan's lifetime;
the 50-change limit applies to undo history, not idempotency receipts.
Opening or explicitly refreshing the budget, and refreshing after a confirmed
save, load the navigation balances and selected month together from one
repeatable-read database snapshot before enabling edits. A concurrent writer cannot
mix two revisions in that response. A bounded fallback retries a read conflict
up to three times; it never resends the saved command.
Exhausted retries retain the saved status and offer manual refresh;
authentication and connection failures do not trigger this conflict retry loop.
Register views continue to load their pages separately without calculating a month.

New application-generated identities use GUID v7. Financial order is explicitly
`Date`, then a server-allocated per-plan `Sequence`, then `Id`; edits and undo/redo
retain the sequence. GUID v7's timestamp does not provide strict ordering within
one millisecond or across clocks, so it is not the financial ordering authority.
Existing sequence-zero records retain their ID tie-breaker. Split rounding uses
stable split IDs so a different database row order cannot change cent allocation.

## Application boundaries

- Shared DTOs and pure calculations: `YHAB.SharedKernel/Budgeting`.
- Browser-compatible UI: `YHAB.UI/Features/Budgeting`.
- EF entities, mapping, commands, authenticated services and minimal endpoints:
  `YHAB/Features/Budgeting`.
- WASM transport: `YHAB.Client/Services/HttpBudgetClient`.

Plan selection, settings, reports, the shell, and Identity forms default to
static SSR with enhanced navigation. The budget/register workspace uses
InteractiveAuto. Each renderer loads a viewer-scoped catalog: once for static
prerendering and again when interactivity starts. The complete ledger is not
serialized into persistent prerender state, which would grow beyond SignalR's
startup message limit. Server rendering calls the application service directly;
WASM uses the authenticated same-origin API. The budget workspace combines its
metadata, account balances and selected month in one consistent read projection.
Register pages, explicit versioned month reads and report summaries have separate
projections. The interactive
fallback posts due entries after the catalog arrives.
Fluent providers live inside the interactive workspace. Native static forms
retain named POST form mapping and antiforgery.

Each operation creates and disposes an `ApplicationDbContext` through
`IDbContextFactory`. Every plan lookup is scoped to the owner and the accounts the
plan is shared with. Composite foreign
keys prevent ledger references crossing plans. Decimal columns use precision
18/scale 2. A plan revision is an EF concurrency token, claimed before ledger
writes. Ledger changes, monotonic sequence allocation, sparse history patches,
operation receipts and recurrence cursors commit in one transaction. Competing
workers use this same revision claim and the unique scheduled-occurrence index;
there is no separate scheduler database or distributed lease. Failed plan posting
is logged with the plan ID while other discovered plans continue. Reads do not track
entities and use a repeatable-read transaction so the ledger and its revision
stay consistent across queries. Explicit transactions run inside EF's execution
strategy, including when Aspire enables transient retries. Migrations run through
Aspire's existing migration resource.

## Cancellation lifetimes

Components that own asynchronous I/O inherit `CancellableComponentBase`; markup-only
components and editors that just raise events do not need it. `CreateOperation()`
links the component lifetime to the named `RequestAborted` cascade supplied by the
static root. Capture the operation's token before awaiting and dispose the linked
source only when that operation finishes. Component disposal cancels lifetime work;
custom cleanup overrides `DisposeCoreAsync` and calls the base implementation.
Do not hide/reimplement disposal in derived components or call `StateHasChanged`
during disposal. Cleanup that must run after cancellation uses its own bounded token.

Static SSR and prerendering link to the current HTTP request. Cascades do not cross
interactive renderer boundaries: Interactive Server and WebAssembly get a fresh
component lifetime, independent of the completed prerender request. Tokens and token
sources are never serialized as component parameters or HTTP payloads. The static
Identity pages keep their existing forms, cookies, antiforgery, and routing contracts.

The workspace, register, and month board cancel superseded reads and retain generation
checks and post-await cancellation checks, because a dependency can ignore a token.
An operation owns its source; the field used to cancel a previous operation is a
borrowed reference. Never dispose that source while the older operation still uses it.
Child read delegates explicitly accept tokens. Parent-owned commands retain normal
`EventCallback` contracts and use the workspace operation's token.

Server-rendered budget calls pass the token through `ServerBudgetClient` to queries
and commands. WebAssembly passes it through `HttpBudgetClient` to HTTP send and JSON
reads. Aborting HTTP can cancel the server's `HttpContext.RequestAborted`; minimal API
token parameters bind to that server token. This is cooperative transport cancellation,
not transfer of the .NET token itself, and intermediaries can delay disconnect detection.
EF context creation, database operations, transaction commits, execution-strategy
retry delays, and long month-replay loops observe cancellation. Background recurrence
keeps its host shutdown token instead of borrowing a browser/request lifetime.

Cancellation does not undo a committed write. Workspace timeouts preserve the original
command/operation ID for uncertain writes; a confirmed save followed by a failed refresh
requires only a refresh. Navigation/disposal cancellation is silent. Read timeouts keep
editing disabled with a refresh message. No cancellation is converted into a generic
domain failure or unconditional success, and no background task is launched to finish
a request-owned write.

Identity's tokenless `UserManager` methods use its supported protected token hook via
`CancellableUserManager`. A scoped `IdentityCancellation` receives `RequestAborted`
before cookie authentication. Circuit revalidation sets its own token in a fresh scope;
the manager does not retain `HttpContext` in a circuit. Direct store initialization
also receives the scoped token. After the first successful irreversible write in a
multi-step account operation, `CompleteWrite()` deliberately stops forwarding request
cancellation so remaining account writes and cookie refresh can finish. Existing partial
outcomes still distinguish later failure from complete success.

A retained Interactive Server circuit is not disposed on every temporary disconnect.
Component lifetime cancellation therefore does not mean immediate disconnect cancellation.
The current reconnect behavior is preserved; a circuit-disconnect policy would need a
separate, replaceable connection lifetime and restart/recovery rules. Likewise, tokenless
framework calls cannot be forcibly stopped; check cancellation before publishing results.

Framework references: [Blazor synchronization context and disposal](https://learn.microsoft.com/aspnet/core/blazor/components/synchronization-context?view=aspnetcore-10.0),
[minimal API special parameter binding](https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/parameter-binding?view=aspnetcore-10.0#special-types).

`PreserveRecurringOccurrenceDates` backfills occurrence identities from their
existing transaction dates and moves the unique occurrence index to the scheduled
date. Older undo/redo snapshots normalize missing scheduled dates to the persisted
occurrence identity before comparison; changing the editable transaction date
does not change that identity during undo/redo. Restoring deleted sequence-zero
transactions also preserves their original ordering. Downgrading to the previous
schema requires occurrence transaction dates to satisfy its old unique index;
the downgrade does not change transaction dates to force compatibility.

The calculator is pure: `Start` produces an immutable opening state and `Advance`
projects one month into its budget and next opening state. With `BudgetMonthFunding`,
it needs only that month's posted transactions and allocations, aggregated future
assignments and target contributions, and the period openings for dated refill
targets. `TargetCalculator.PeriodFor` supplies the shared cycle-boundary policy.
`Calculate` remains the complete-history entry point and uses the same transition;
comparisons with it establish replay consistency, not an independent math oracle.
Literal totals and conservation checks supply the independent expectations.

The server persists opening states in `BudgetCheckpoint`, containing balances and
category carry, not transactions. A cold request builds checkpoints through the
requested month in year-sized transaction/allocation windows. Later requests load
the nearest valid opening and only the additional period openings needed by active
refill targets. SQL aggregates prior target contributions and future reservations;
these queries do not materialize historical allocations. A missing target-period
opening triggers replay from an earlier checkpoint rather than assuming zero carry.
Assignment amounts and posted financial movements invalidate openings after the
earliest affected month, considering both old and new values. Account opening
balances/dates/types and account/category identities or credit mappings reset all
openings. The pure `CheckpointPolicy` preserves openings for names, notes, targets,
visibility, snoozing, payees and flags; these do not change
financial carry. Transaction order and split identities remain financial inputs
because they can affect credit rounding and payment allocation. This happens in the same
transaction as the ledger/history write, including undo and automatic posting.
Publication locks the owned plan row without incrementing its revision. Each
financial write records its revision and earliest invalid opening month in the
same transaction. A calculation from an older revision may publish only openings
before every intervening financial change; it cannot restore invalidated states.
This lets historical openings survive current-month editing without requiring a
quiet interval. Newer boundaries that start earlier replace the older boundaries
they cover, so storage grows with distinct affected months rather than edit count.
Foreign and future-revision publications are rejected. Explicitly versioned month
reads and commands still require the requested revision. The combined workspace
read returns its own internally consistent revision. Checkpoint format versions
permit rebuilding after calculation semantics change.

The server caches small month results for two minutes, bounded by category-row
count, keyed by owner, plan, revision, server date and month. Any mutation changes
the key, including backdated edits and future assignments. Authorization and the
current revision are checked even on cache hits.

Common writes load only their affected transactions; assignments/catalog edits
load no transactions or split queries. The view catalog contains metadata rather
than allocation history. An assignment loads its single month/category row.
Undo/redo load records named by the history patch plus later dependents of any
account/category being removed, preserving conflict checks. Persistence applies
explicit patch updates/removals and leaves unrelated tracked records intact.
Money moves and target funding use the revision-checked monthly projection and
current allocations. Completed-operation retries return current metadata without
reloading the ledger. Bulk payee changes project only matching entry IDs/names and
use one parameterized update; their history does not contain complete transactions.
Rename, undo and redo preserve financial checkpoints. Account edits use a SQL
aggregate for first transaction date and posted balances.
Aggregate queries still process relevant account history
in the database; they do not materialize it in the application. Reports aggregate prior movements per account and
requested movements per account/month and category/month in SQL. The pure report
calculator adds dated opening balances and builds net worth and spending from those
totals; transaction and split entities are not loaded. Both aggregate queries and
the catalog share one repeatable-read revision, including for partial-month ranges.
The database still processes relevant historical movements to calculate balances.
The browser never needs the complete ledger for these views. The validation report records observations
through 100,000 entries, 100 accounts, 500 categories and 20 years of allocations,
not a latency guarantee. Background posting locks the owned plan row before reading
one bounded batch and uses the revision obtained after waiting. This coordinates
independent processes and avoids discarding work because discovery saw an older
revision. Interactive commands, including manual catch-up, retain their supplied
revision checks. An empty background batch creates no revision or receipt; cancellation
rolls back and releases the row lock. See the validation report for measured costs
and the remaining deployment-scale validation.

Command transformations receive immutable `CommandIds` allocated at the operation
boundary. Replaying the same snapshot, command, date and IDs produces identical
data, including credit payment categories and bounded
recurring batches. Allocation uses GUID v7; financial ordering still uses explicit
transaction sequence numbers. A recurring batch reserves at most 128 occurrences
and their splits; unused reserved IDs are discarded.

Recurring reads load selected templates rather than their entire posting history.
The `IndexRecurringTemplates` migration replaces the full repeat/date index with
a plan/date/sequence/ID index filtered to `Repeat <> 0`. Both per-plan selection
and worker discovery can search recurring templates without scanning posted rows.
The ordinary plan/date register index remains separate. The worker still examines
eligible templates across plans before choosing its bounded set of plans; this
does not make discovery constant-cost as the number of templates grows.
The service first proposes a pure batch, looks up only those scheduled identity
pairs in PostgreSQL, and reruns the same transformation with the same allocated IDs
if any dates already exist. At most 128 pairs are queried, using the plan/template/
scheduled-date unique index. Skipped duplicates still consume the batch's date limit
and advance its cursor. The lookup is plan-scoped and uses the stable scheduled date,
so resetting a template or moving an occurrence's actual date does not repost it.

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
| Catalog, balances, due flag | GET `/api/plans/{planId}/view` |
| Catalog, balances and month from one revision | GET `/api/plans/{planId}/workspace?month={date}` |
| Current revision | GET `/api/plans/{planId}/revision` |
| Monthly projection | GET `/api/plans/{planId}/months/{month}?version={version}` |
| Register page | GET `/api/plans/{planId}/register?query={encodedJson}` |
| Report summaries | GET `/api/plans/{planId}/reports?from={date}&through={date}` |
| Distinct payees | GET `/api/plans/{planId}/payees` |
| Sharing state | GET `/api/plans/{planId}/sharing` |
| Create a one-time share code | POST `/api/plans/{planId}/sharing/codes` |
| Join with a share code | POST `/api/plans/join` |
| Stop sharing | DELETE `/api/plans/{planId}/sharing/{userId}` |
| Leave a shared plan | POST `/api/plans/{planId}/leave` |
| Create account/group/category/transaction | POST `/api/plans/{planId}/{collection}` |
| Move group / category | POST `/api/plans/{planId}/groups/reorder` / `categories/reorder` |
| Update an item | PUT `/api/plans/{planId}/{collection}/{resourceId}` |
| Assign money | PUT `/api/plans/{planId}/assignments` |
| Bulk delete entries | POST `/api/plans/{planId}/transactions/delete` |
| Move money / fund targets | POST `/api/plans/{planId}/money-moves` / `auto-assign` |
| Plan settings | PUT `/api/plans/{planId}/settings` |
| Rename payee / merge category | POST `/api/plans/{planId}/payees/rename` / `categories/merge` |
| Undo / redo / post due repeats | POST `/api/plans/{planId}/undo` / `redo` / `recurring/post-due` |

Concrete command records are request contracts, not EF entities or OneOf unions.
Commands include `operationId`; retain it together with the original command when
retrying an uncertain write. The workspace's optional `month` defaults to the
server's current budget month; an explicit date is normalized to its month.
`RegisterQuery` includes account, search, status,
date range, sort, revision and an opaque next-page cursor. Page size defaults to
50 and is capped at 100. A stale requested revision returns 409. The complete-plan
read remains available for internal comparisons and existing API consumers.
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
| Recurring entries without one-off scheduling | `RejectsOneOffFutureEntriesButAcceptsRepeatingInstructions`, `DueRecurrencesArePostedOnceWithAnchorDayPreserved`, `FrequenciesRetainTheAnchorAcrossCalendarBoundaries`, `TwiceMonthlyClampsFebruaryAndReturnsToTheOriginalDays` |
| Recurring edits, occurrence identity, and upgrade compatibility | `EditingTemplateDetailsPreservesTheOriginalMonthEndAnchor`, `ChangingTemplateScheduleEstablishesANewAnchor`, `OccurrencesCanShareEditedDatesWithoutLosingIdentityOrHistoryAsync`, `MigrationBackfillsScheduledDatesAndLegacyHistoryCanStillBeRestoredAsync` |
| Legacy creation/deletion history and stable restored ordering | `LegacyOccurrenceCreationAndDeletionRestoreStableIdentityAsync`, `DeletingMigratedTransactionCanUndoAndRedoWithoutChangingSequenceAsync`, `RestoringLegacyZeroSequenceDoesNotAllocateOrChangeOrder` |
| Transfers and account closing | `CreditBalanceTransferMovesFundedPaymentMoneyToTheNewCard`, `ClosingAnAccountRequiresZeroBalanceAndKeepsEditableOpeningValues` |
| Category history and payee management | `MergingAUsedCategoryPreservesAssignmentsSpendingAndAccountBalance`, `MergingAssignedCategoryPersistsHistoryAndSupportsUndoRedoAsync`, `PayeeRenameChangesMatchingPostedAndRecurringEntriesOnly`, `TransactionEditorsUsePayeeProjectionAndRefreshSuggestionsAsync` |
| Private plans, SQL integrity, durable history, and concurrent edits | `OwnerIsolationHistoryAndConcurrentWritesPersistAcrossContextsAsync`, `DatabaseRejectsCrossPlanReferencesAndAccountDeletionRemovesOwnedLedgerAsync` |
| In-app plan sharing, editing, and access removal | `ShareCodeGrantsEditingAndReadAccessUntilTheOwnerRemovesItAsync`, `JoinRejectsInvalidExpiredEmptyAndOwnerCodesAsync`, `ASharedMemberCanLeaveButAnOwnerCannotAsync`, `CodesAreSingleUseAndRedemptionRetriesAreIdempotentAsync`, `ConcurrentRedemptionIsIdempotentAndConcurrentRemovalHasOneWinnerAsync`, `PlansPageSeparatesOwnedAndSharedPlansAndShowsJoinFormAsync`, `OwnerCreatesShareCodeAndRemovesACollaboratorAsync`, `SharedMemberSeesOwnerAndRejectsUnconfirmedLeaveAsync`, `PlansPageJoinsWithACodeAsync`, `JoinServiceErrorIsShownAsync` |
| Manual browser workflow, calculator entry, keyboard undo/redo, reports, and API guards | `ManualPlanPurchaseUndoAndReportsAgreeAsync` at 1440px and 390px, including the phone amount keypad, the Available-pill move flow, editor tap size, and sticky editor chrome; `TappingAvailableMovesMoneyOutOfTheCategoryAsync` covers the touch move flow |
| Themes, mobile navigation, and SSR document continuity | `FluentNavigationPreservesDocumentAndThemeChoice` at 1280px and 390px |
| Actual startup, migrations, and database readiness | `AppHostCompletesMigrationsAndServesHealthyApplication` |

See [the test guide](../tests/README.md) for commands and prerequisites. Passing
these checks does not establish exact parity with every YNAB edge case or a
production performance guarantee.
