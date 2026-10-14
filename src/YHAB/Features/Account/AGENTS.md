# Account and Identity

These rules govern account pages and services, and changes elsewhere that affect
their forms, redirects, authentication, or render modes. The root rules and
[server guidance](../../AGENTS.md) still apply. Before changing account Razor,
styling, components, forms, submitters, or navigation, read the
[UI guide](../../../YHAB.UI/AGENTS.md),
including its native Identity input/submitter and static navigation requirements.
Use the [OneOf outcome conventions](../../../../.github/instructions/csharp.instructions.md#oneof-and-operation-outcomes)
and the root cancellation rules for account outcomes and committed writes;
consult [cancellation lifetimes](../../../../docs/budgeting.md#cancellation-lifetimes).
Account tests also follow the [test guide](../../../../tests/AGENTS.md).

## Static SSR and redirect contracts

- Account pages use static server-side rendering (SSR). Preserve
  `[ExcludeFromInteractiveRouting]` in their page imports, authorization on
  account-management pages, and their HTTP context and cookie contracts. Keep
  form names, POST methods, `[SupplyParameterFromForm]` mapping, and antiforgery
  hooks intact. Account pages stay on static SSR under the root
  [render-mode rules](../../../../AGENTS.md#project-boundaries-and-rendering); do
  not apply a global interactive render mode that changes account behavior.
- `BlazorDisableThrowNavigationException` is enabled. After redirecting, return
  or otherwise terminate the current branch when subsequent work must not
  execute. Do not rely on navigation throwing to stop an account operation.
