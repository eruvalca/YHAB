---
applyTo: "src/YHAB/Features/Account/**"
---

# Account and Identity

These rules govern account pages and services, and changes elsewhere that affect
their forms, redirects, authentication, or render modes. The repository-wide rules
and the [server guidance](server.instructions.md) still apply. Before changing
account Razor, styling, components, forms, submitters, or navigation, read the
[UI guide](ui.instructions.md), including its native Identity input/submitter and
static navigation requirements. Use the
[union outcome conventions](csharp.instructions.md#union-types-and-operation-outcomes)
and the repository-wide cancellation rules for account outcomes and committed
writes; consult
[cancellation lifetimes](../../docs/budgeting.md#cancellation-lifetimes). Account
tests also follow the [test guide](tests.instructions.md).

## Static SSR and redirect contracts

- Account pages use static server-side rendering (SSR). Preserve
  `[ExcludeFromInteractiveRouting]` in their page imports, authorization on
  account-management pages, and their HTTP context and cookie contracts. Keep
  form names, POST methods, `[SupplyParameterFromForm]` mapping, and antiforgery
  hooks intact. Keep the explicit `app.UseAntiforgery()` call even though the
  ASP.NET Core 11 template omits it: static SSR emits form tokens only when the
  token-based middleware runs, so removing it would silently drop them. Account
  pages stay on static SSR under the repository-wide
  [render-mode rules](../copilot-instructions.md#project-boundaries-and-rendering);
  do not apply a global interactive render mode that changes account behavior.
- `BlazorDisableThrowNavigationException` is enabled. After redirecting, return
  or otherwise terminate the current branch when subsequent work must not
  execute. Do not rely on navigation throwing to stop an account operation.
- Passkey sign-in runs `SignInManager.PreSignInCheck` in ASP.NET Core 11, like the
  other sign-in methods. Keep `SignInOutcome` mapping `IsLockedOut` and
  `IsNotAllowed` and keep the login page handling both: a successful passkey
  assertion no longer bypasses confirmation policies or lockout.
- Keep the redirect status message on the `Identity.StatusMessage` cookie. Blazor's
  static SSR TempData API is component-only: `ITempData`, `[SupplyParameterFromTempData]`,
  and the cascading value are its public surface, while `TempDataService`, its
  `ITempDataProvider`, and the `GetOrCreateTempData` accessor are internal. A
  redirect service therefore cannot write TempData, and moving the message into
  components would require a fabricated cascading value in bUnit tests. Revisit if
  a public service-side TempData API ships.
