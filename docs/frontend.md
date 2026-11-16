# Frontend: Fluent UI, styling, and the installable app

YHAB's browser face is built with Fluent UI Blazor v5, self-hosted fonts and CSS,
and a deliberately chosen mix of static SSR and interactive rendering. This guide
owns that detail; the [root README](../README.md) keeps only the quickstart.

## Installable web app

YHAB supports installation from a compatible browser over HTTPS. Open **Install
YHAB** in the site footer for an install button when the browser offers one,
or instructions for its menu. On iPhone/iPad, use **Share → Add to Home Screen**.
An installed app opens **Your plans** in its own window; normal authentication
still applies. Browser and OS support determine the installation experience.
`manifest.webmanifest` declares standalone display with a `display_override`,
`orientation: any`, finance/productivity categories, a neutral white splash
background, and shortcuts to **Your plans** and **Home**. The shell reserves the
safe-area insets for the header, content, footer, and reconnect notice so an
installed window clears notches and home indicators.

This is an online application with an offline fallback. After one successful
visit and service-worker installation, failed full-page and Blazor enhanced GET
navigations show a connection screen with **Try again**. The worker stores only
`offline.html`, its stylesheet, and its icon. It never caches account/budget HTML
or API responses, and never intercepts POSTs or queues saves. A first visit without
a connection cannot show the fallback. Losing connectivity while already editing
continues to use the application's existing error/reconnect handling; if a save
was interrupted, check its status after reconnecting before entering it again.
The Blazor runtime may manage its own framework-resource cache independently.

New deployments install a worker in the background and offer **Reload to update**
or **Later**. Finish saves and preserve unsaved entries before confirming the
reload. Only the tab requesting it reloads; other tabs keep their forms and offer
their own reload. A later visit or returning to a visible tab checks for updates
(visibility checks are limited to once per minute). Keep server APIs compatible
with open clients during deployment; the update prompt cannot make old code
compatible with a breaking backend change.

The selected Pocket Fan artwork with **YHAB** on its front pocket is kept in
`design/branding/pocket-fan-yhab.png`. The exported 192px and 512px icons, 180px
Apple touch icon, and 32px favicon live under `src/YHAB/wwwroot`. The manifest and
offline document assume the application is hosted at the origin root, as it is
today. Serve the worker at its stable URL and allow revalidation through any CDN;
do not give that URL immutable cache rules.
Use a stable HTTPS origin for a deployed installation.

The same worker runs in development and published builds. Its version comes from
application content hashes, without a manual version bump; see
[PWA build assets](../build/README.md#pwa-build-assets). To reset a local experiment,
unregister YHAB's worker and clear its `yhab-offline-*` caches in browser developer
tools. Changing the local port creates a different service-worker/install origin.
See [browser validation](../tests/README.md#choosing-an-integration-layer) for the
automated checks and the remaining device-level installation checks.

## Fluent UI Blazor

`Microsoft.FluentUI.AspNetCore.Components` **5.0.1** is centrally pinned in
`Directory.Packages.props` and referenced by the server, client, and shared UI.
Both host `Program.cs` files call `AddFluentUIComponents()` so prerendering,
Interactive Server, and Interactive WebAssembly resolve the same services.
The shared kernel has no UI dependency. Fluent UI Blazor v5+ is the application's
layout, styling, and component framework. Use its components and design tokens
for new UI; do not introduce a parallel CSS framework or default to custom Grid/Flexbox.

`Microsoft.FluentUI.AspNetCore.Components.Icons` **5.0.1** is referenced by the
shared UI and flows to both hosts. Navigation uses `IconRest`/`IconActive` with
regular/filled system icons; actions use `IconStart`, as on the category-group
button. Use strongly typed
instances such as `new Icons.Regular.Size20.Home()` (the Razor imports define the
`Icons` alias) so publishing can retain referenced icons. Keep meaningful visible
labels; redundant icons are decorative and not extra tab stops. No icon font,
CDN, script, service registration, or separate emoji package is needed.

`App.razor` loads the Fluent baseline stylesheet, `YHAB.styles.css`, then
`app.css`. The generated solution stylesheet already imports the Fluent component
CSS bundle; do not add a second link to that bundle. `no-fuib-style` on the body
prevents Fluent's initializer from adopting another copy of the baseline after
the application overrides. The package's Blazor JavaScript initializer loads the
web components automatically, including static layout hamburger behavior. Do not
add a v4 web-components script or `FluentDesignTheme`. The body specifies a teal
brand palette with `data-theme-color`. The static header offers System, Light,
and Dark through v5's `Blazor.theme.setThemeMode` API. Fluent persists the
preference and follows OS changes in System mode. The shared initializer reapplies
the selected theme and restores the selector after enhanced navigation, which can
replace the body's client-side theme marker. Appearance does not start a .NET renderer.
Application styles use v5 CSS design tokens.

## Typography and design tokens

The UI face is Plus Jakarta Sans, self-hosted from
`src/YHAB.UI/wwwroot/lib/fonts/plus-jakarta-sans/` alongside its `OFL.txt`; no
CDN or npm package is involved. `app.css` declares the latin and latin-ext
subsets and exposes `--yh-font-family`, which is also assigned to Fluent's
`--fontFamilyBase` on `:root` so web components inherit it. Fluent applies its
theme tokens through an adopted stylesheet targeting `html`, and `:root`
outranks that selector. `App.razor` preloads the latin file. The offline
document stays on system fonts so the service worker does not precache the font.

The shell uses `FluentLayout`, `FluentNav`, and `FluentNavItem`; the home and
authenticated pages use Fluent cards and buttons with native content links.
Native anchors preserve accessible link names and navigation before JavaScript
initializes. Account management
has Fluent navigation and a card around its content. `FluentGrid`/`FluentGridItem`
provide responsive page columns and `FluentStack` provides content spacing, with
a mobile drawer below the Fluent layout's 768px breakpoint. Keep grid adaptive
rendering off for static SSR; responsive CSS does not need a .NET event handler.

Shared styles live in `src/YHAB/wwwroot/app.css`: sizing, typography, forms
(`account-form`, `form-field`, `checkbox-field`), action groups (`actions`),
notices (`notice` with a semantic `data-kind`), and table overflow
(`table-container`). The stylesheet documents one control scale: compact 32px
(`--yh-control-compact`) for dense in-row actions, standard 40px
(`--yh-control-standard`) for buttons and inputs, and touch 44px
(`--yh-control-touch`) for primary call-to-action targets. Controls use
`--yh-control-font` (16px) text so mobile browsers do not zoom on focus, and a
primary action is never smaller than the standard step. The type scale uses
`--yh-font-body` (15px, applied on `body`), `--yh-font-eyebrow` (12px), and the
capped `--yh-font-title`, `--yh-font-title-compact` (for dense page headers such
as the budget summary), and `--yh-font-display` heading ramps. Shape, elevation,
and color use `--yh-radius-*`, `--yh-shadow-*`, and the `--yh-surface-*`,
`--yh-text-*`, `--yh-border-*`, and `--yh-accent*` roles. Sizes are `rem`; `px`
is limited to hairlines, focus-ring widths, and the pill radius, and media
queries use `rem` so breakpoints follow the user's default font size. Use these
tokens for new controls and headings instead of ad hoc sizes.
Component-specific styles belong in adjacent `.razor.css` files. Use narrowly
scoped `::deep` selectors for child component markup.
Native button styling excludes slotted controls: Fluent dropdowns render a native
button in their `control` slot and own its border and padding. Filter toolbars use
explicit Fluent alignment and zero field margins so labels do not displace actions.
Editor fields also use zero margins, leaving spacing to their Fluent grid/stack;
the shared editor surface stretches text inputs, dropdowns, and text areas to the
available column width while keeping checkbox sizing intrinsic. On phone widths it
also keeps the heading, **Close**, and the `editor-actions` row on screen while the
rest of a long form scrolls.
All three navigation menus use transparent resting backgrounds and Fluent's subtle
hover token. The signed-in shell link reads **Your account**; account details keep
the email address. Money uses shared `data-money-tone` colors from Fluent tokens.

## Render boundaries

Render boundaries are deliberate:

- `Routes`, `MainLayout`, Home, plan selection, reports, plan settings, and Identity
  pages use static SSR. The budgeting workspace opts into `InteractiveAuto` for
  .NET event handling. Workspace actions are disabled during prerendering until
  the renderer becomes interactive. Static pages don't start a .NET interactive
  runtime simply to display navigation or cards.
- The budgeting workspace hosts `FluentProviders` inside its interactive
  boundary. Providers
  inherit its renderer and scoped services for dialogs, toasts, tooltips, and key
  handling. Add providers inside other interactive pages/subtrees when needed,
  once per active renderer/service scope. The layout's `Body` never crosses an
  interactive boundary. Shared CSS keeps dialog providers out of document flow
  and the global overlay hidden until its shadow dialog exists. Enhanced
  navigation can paint a registered custom element before that dialog is attached;
  its inactive spinner must not flash or shift the page. The initialized dialog
  remains visible when opened.
- Static navigation has ordinary `Href` links with `tabindex="0"`, since Fluent's
  interactive roving-tabindex setup does not run in SSR. Shell links use Blazor's
  enhanced navigation: the server renders the destination and Blazor updates the
  existing document without restarting the page or reloading the runtime.
  `YHAB.UI.lib.module.js` registers the collocated `MainLayout.razor.js` handler
  through `afterWebStarted`. It closes the Fluent mobile drawer on
  `enhancednavigationstart`, before the DOM update, including back/forward
  navigation. Stable layout/hamburger IDs preserve their JS wiring across updates.
  The desktop **Hide menu / Show menu** button stores its preference in local
  storage and reapplies it after enhanced navigation. With no stored preference,
  plan routes collapse the global menu and use their compact plan sidebar.
  **Plan & accounts** expands that sidebar on smaller screens. It uses the same static
  shell initializer, without starting a .NET renderer; the mobile drawer remains
  independent of that preference. Its explicit `tabindex="0"` survives SSR patches
  that would otherwise remove Fluent's browser-added keyboard focus attribute.
  The shell grows with its content and uses document scrolling, allowing Blazor
  to reset scroll position on navigation and restore it through browser history.
  `app.css` overrides Fluent's default fixed-height, overflow-hidden body so
  mouse-wheel and touch scrolling can reach the full document.
  The account settings menu does not use interactive categories or event callbacks.
- Identity forms retain native inputs, submit buttons, form names, antiforgery,
  and passkey hooks. They receive Fluent token styling while preserving static
  POST mapping, autofill, and cookie redirects. These POSTs keep normal navigation
  so cookie changes refresh authentication state. Shared notices retain their
  `notice`/`data-kind` contract. The reconnect/error UI retains native controls
  because it must function while the Blazor circuit is unavailable.
- `Workspace` keeps its first interactive paint identical to the prerendered page
  by carrying a display-only copy of the plan through `[PersistentState]` and by
  caching the last catalog and month per renderer. The persisted copy omits
  transactions and allocations, which stay server-owned, and is dropped when its
  protected, double-Base64 transport form would exceed a 28 KiB budget inside
  SignalR's 32 KiB circuit-start message; an oversized plan paints the structural
  skeleton instead of failing to connect. The cached month is restored into the
  board and the next read, and a register-only read keeps it while the revision is
  unchanged. Successful month changes update the cache without requiring a
  separate refresh. A supplied prerendered snapshot still takes precedence so
  the first interactive paint matches the server HTML. The workspace refreshes
  immediately after restoring, and an
  authorization or not-found response drops the cached copy. The actions status
  reads **Connecting…** until the renderer can accept input.
  Its budget/register selection belongs to the current workspace route: an
  outgoing transactions page stays a transactions page while reports, settings,
  plan selection, or account management is being fetched. Report-derived register
  filters remain attached to that outgoing view, preserving its rows and draft
  until another workspace route is accepted. Route matching compares parsed plan
  IDs so valid GUID URL formats select the same page.
- Enhanced-navigation feedback is centralized in the shell. When a navigation is
  still pending after 250 ms, `MainLayout` shows a slim progress bar without fading
  the content. The bar survives DOM patches and finishes at
  `enhancednavigationend`; fast navigations do not flash an indicator.
  Reduced motion disables its transitions, and forced-colors uses the system
  highlight.
  The same feedback covers enhanced form posts.
- The Reflect date filter uses `data-enhance` so a GET report refresh stays inside
  the enhanced-navigation document. Identity and logout POSTs keep normal
  navigation.
- Fluent resolves the theme on `body`; the shell mirrors the resolved mode onto
  `<html>` so the document scrollbar and native controls follow dark mode.
- Register revision refreshes keep the existing rows and their original labels
  painted until replacement data arrives; pending results are marked busy and
  row actions are disabled. Payee suggestions no longer delay painting rows.
  Initial register reads use a static structural placeholder. Month navigation
  keeps the displayed month label, figures, and category catalog together until
  the requested projection arrives.

## Amount inputs and mobile keyboards

A Fluent web component keeps its native control in a shadow root and does not
forward the host's `inputmode` attribute to it, so setting Fluent's `InputMode`
parameter alone cannot change a phone's virtual keyboard. The same
`afterWebStarted` initializer runs `input-mode.js`, which mirrors that attribute
onto the shadow control. Set `InputMode` on the Fluent component and let the
initializer supply the browser behavior. `AmountInput` asks for the decimal keypad
by default; because that keypad has no minus key, set `AllowNegative` where a field
accepts a negative amount so it renders a sign control, or keep
`InputMode="TextInputMode.Text"` where the surrounding layout cannot host one. A
positive-only field sets `RejectNegative`, which rejects typed negative results
while still displaying a stored negative value. The budget table's monthly
assignment field stays positive: moving money out of a category is a transfer to
Ready to assign or another category, offered by the row's Available pill.

## Styling conventions

Custom CSS is limited to application sizing, accessibility, and static form
compatibility, using Fluent tokens. Prefer Fluent layout/spacing parameters;
the library's generated styles are expected. Use normal flow for prose and
semantic tables. Do not recreate Bootstrap utilities or add `!important` or
ornamental animations. Use icons to clarify navigation/actions. Keep form
labels before their controls and preserve Blazor/Identity behavior hooks when
editing markup. Check narrow screens, keyboard focus, and text wrapping when
changing layouts. See the [UI instructions](../.github/instructions/ui.instructions.md)
for the authoring conventions and the
[account instructions](../.github/instructions/account.instructions.md) when changes
affect Identity. Build-time compatibility with the pinned Fluent bundle is
described in [build rules](../build/README.md#fluent-ui-lifetime-compatibility).

Setup references: [installation](https://www.fluentui-blazor.net/installation),
[layout](https://www.fluentui-blazor.net/layout), and the
[released package source](https://github.com/microsoft/fluentui-blazor/tree/358e449b08711f3343ce6c2ffbe6c9f77045bde5).
Navigation references: [enhanced navigation](https://learn.microsoft.com/aspnet/core/blazor/fundamentals/navigation?view=aspnetcore-10.0#enhanced-navigation-and-form-handling)
and [JavaScript with static SSR](https://learn.microsoft.com/aspnet/core/blazor/javascript-interoperability/static-server-rendering?view=aspnetcore-10.0).
