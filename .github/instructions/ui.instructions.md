---
applyTo: "src/YHAB.UI/**"
---

# UI and rendering

These rules govern shared UI and, through the repository-wide instructions,
Razor, styling, navigation, and rendering work in the server, browser host, and
tests. Keep the repository-wide project boundaries and validation requirements in
force, and the [C# and Razor conventions](csharp.instructions.md) for code-behind.
For account behavior, also read the [account guide](account.instructions.md) and
the [server guide](server.instructions.md), including when shared UI changes can
affect static Identity pages. See the [root README](../../README.md) for Fluent
assets/provider setup and [build rules](../../build/README.md) for enforcement.

## Razor components

- Every page/component must have a matching `.razor.cs` partial class, including
  markup-only components. `_Imports.razor` does not need code-behind.
- Put component members and logic in code-behind. Do not add `@code` or `@functions`
  blocks. Rendering expressions and control flow remain in `.razor` markup.
- Match the generated component's namespace, class name, and generic parameters.
  `_Imports.razor` using directives do not apply to `.razor.cs` files; add the C#
  imports the code-behind needs.
- Preserve Razor routes, rendering, authorization, injection, and component
  parameter contracts when moving code. Do not edit generated C# files.
- Mark caller-supplied `[Parameter]` properties with `[EditorRequired]` when the
  component needs them to serve its purpose. Keep optional inputs optional and
  assess route, query, form, and cascading inputs separately. Use ordinary
  auto-properties, not C# `required` or `init`. This is a Razor call-site check,
  not runtime validation or a guarantee of non-null values.
- Treat `[Parameter]` properties as caller-owned inputs. Keep editable local state
  separate and use `EventCallback` to request parent updates; reconcile incoming
  parameter changes deliberately. `MA0121` checks parameter overwrites.

## Fluent UI, styling, and layout

- Fluent UI Blazor v5+ is this solution's UI framework: use its layout,
  navigation, controls, icons, typography, spacing, and theme tokens. Prefer
  `FluentLayout`, `FluentGrid`/`FluentGridItem`, and `FluentStack` over hand-built
  CSS Grid/Flexbox layouts. Verify APIs against the centrally pinned release and
  Fluent MCP documentation; v4 examples are not compatible and v5+ does not mean
  floating package versions. Do not introduce a second styling/component framework.
- Keep the shared baseline and reusable form, action, notice, and table patterns
  in `src/YHAB/wwwroot/app.css`. Keep component-specific rules in adjacent
  `.razor.css` files, with narrowly scoped `::deep` selectors only when child
  component markup requires them. Custom CSS supplements Fluent for application
  sizing, accessibility, and static Identity forms; it is not a competing design
  system. Use Fluent design tokens instead of unrelated colors and spacing.
- Size controls and headings through the shared scale in `app.css`:
  `--yh-control-compact` for dense in-row actions, `--yh-control-standard` for
  buttons and inputs, and `--yh-control-touch` for primary call-to-action
  targets, with `--yh-control-font` for form text. Headings and eyebrows use
  `--yh-font-title`, `--yh-font-title-compact` for dense page headers,
  `--yh-font-display`, and `--yh-font-eyebrow`. Do not add
  one-off sizes or leave a primary action at Fluent's 24px small size.
- A malformed `.razor.css` rule is not a build error. It reaches the generated
  scoped-CSS bundle as written, and an unterminated block swallows every following
  rule in the assembly, breaking unrelated pages. Verify scoped-CSS edits with a
  browser test.
- Use descriptive classes rather than framework utilities. Do not reintroduce
  Bootstrap, `!important`, or ornamental animations. Prefer Fluent layout and
  spacing parameters over handwritten inline styles. Fluent's own generated
  styles are expected. Use the v5 icons package for meaningful navigation/action
  icons, keep visible labels, and leave redundant icons nonfocusable and hidden
  from assistive technology. Keep native Identity inputs and submit buttons:
  their static SSR form mapping, browser autofill, and passkey submitter contracts
  must work without an interactive form. Do not bulk-convert them to web components.
- A decimal keypad has no minus key, so a field that must accept a negative amount
  sets `AllowNegative` on `AmountInput` and uses the `±` control it renders; a
  dense column with no room for that control keeps `InputMode="Text"` instead.
  Leaving it unset silently removes touch entry for that field. A positive-only
  field sets `RejectNegative`, which rejects typed negative literals and expression
  results without hiding a stored negative value. The budget table's monthly
  Assigned field is positive-only: moving money out of a category is a transfer to
  Ready to assign or another category, offered by the row's Available pill
  (`AvailablePill`), not a signed assignment.
- Keyboard actions that read a typed Fluent value must commit the field before
  the component handles the key. Fluent's `Immediate` text binding is debounced,
  so `budget-workspace.js` blurs the focused field on Enter/Escape first and
  fields opt in with `data-enter-saves`; clear that marker and the key acts on the
  previous value. That handler skips keys belonging to an active IME composition,
  because the later C# `IsComposing` check cannot undo a blur that already ended
  the composition. Editors report unsaved changes through the `HasUnsavedChanges`
  callback rather than a rendered bool, because the key event can arrive before
  the change that made the draft dirty has re-rendered the editor.
- Static `FluentNavItem` links and `FluentButton` controls need `tabindex="0"`:
  interactive navigation initialization does not run, and enhanced navigation
  can remove a web component's browser-added tabindex. Keep enhanced navigation enabled
  for shell links. The shared UI's JS initializer closes the static Fluent mobile
  drawer on `enhancednavigationstart`; do not force full reloads to close it.
  Keep the layout and hamburger IDs stable across server renders. Identity POSTs
  still perform normal navigation to refresh cookies and authentication state.
  Keep the shell on document scrolling so enhanced navigation can reset and
  restore scroll position; a separate layout scrollbar needs its own handling.
  See `README.md` for Fluent assets and provider setup.
- Make layouts shrink and wrap on narrow screens, preserve visible keyboard
  focus, and put form labels before their controls. Preserve semantic links,
  buttons, tables, and all Blazor/Identity form and reconnect hooks.
- Shared notices use `class="notice"` with `data-kind="success"`, `"error"`,
  `"warning"`, or `"information"`. Keep meaning in the message, not just color.

## Asynchronous component lifetime

- Components owning asynchronous I/O use `CancellableComponentBase` and linked
  operation sources; cancel superseded reads, retain stale-result checks, and
  dispose each source when its operation finishes. Override `DisposeCoreAsync`
  for cleanup instead of hiding disposal. Component lifetime is the cancellation
  owner for interactive work; the repository-wide
  [cancellation rules](../copilot-instructions.md#validation) own the shared token
  requirements, including the ban on serializing tokens or reusing a completed
  prerender request's token in a circuit.
