# YHAB

This solution contains a .NET 10 Blazor server, WebAssembly client, shared UI and
shared kernel, plus Aspire hosting and service defaults.

## Instruction scope

- Before reading or modifying files in a subtree, discover applicable nested
  `AGENTS.md` files along the path from the repository root to the target directory
  and read those not already in context. Repeat when work moves to another area;
  do not preload unrelated guidance. More specific instructions govern their
  subtree and take precedence over broader guidance.
- When adding or changing agent instructions, keep repository-wide rules,
  cross-project invariants, and short routing instructions in the root `AGENTS.md`.
  Put subtree-specific rules in the nearest `AGENTS.md` whose directory scope
  covers the affected files, creating one when needed. Avoid repeating inherited
  rules or moving a shared requirement into a scope that excludes its consumers.
- Path-scoped GitHub Copilot instruction files in `.github/instructions` carry an
  `applyTo` glob and activate for matching files whatever the session working
  directory. Use them for rules that belong to a file type or subtree when no
  single nested `AGENTS.md` scope fits, as with the C# and Razor conventions. A
  file may route the reader or own its scoped rules, but it must never repeat a
  rule another `AGENTS.md` or instruction file already states, because Copilot
  combines instruction sources and defines no precedence between them. List a file
  that owns rules in the task routing table so agents that do not read Copilot
  instruction sources still find it. See
  [instruction discovery](build/agent-hooks.md#instruction-discovery).
- Put detailed, reusable workflows in repository-owned `.agents/skills`, with
  clear activation descriptions and short conditional routing instructions when
  their use is mandatory. This also suits file-type guidance spanning several
  directories. Reference existing setup, feature, build, and test documentation
  instead of copying it into instructions or skills.
- Keep instructions hygienic: the smallest set of high-signal information that
  reliably changes the outcome. Keep non-obvious system facts, architectural
  boundaries, deliberate local decisions, hard constraints, authoritative
  validation commands, and pointers to the source of truth. Remove generic
  engineering advice, prompt coaxing or persona text, rules a formatter, analyzer,
  or test already enforces, exhaustive inventories, and obsolete workarounds.
  Verify commands, versions, and workarounds that may have changed. Reserve
  `always`, `never`, and `must` for rules that are genuinely absolute. Treat every
  added line as competing for attention with the task and the code itself.
- During documentation review, apply the keep/remove/move/verify test to new or
  changed instructions: keep information that is true, consequential, and hard to
  infer; remove or move the rest; and verify anything version- or command-shaped.
  Apply this to the current task; do not reorganize unrelated existing guidance
  solely to complete the review.

## Read the guidance for the task

The root rules apply throughout the repository. Before planning, reading, or
changing the areas below, read the linked guide or scoped instruction file as well
as applicable ancestor `AGENTS.md` files. Follow these routes even when the
affected code, configuration, or tests live outside the guide's own directory.
Apply the relevant rules to that work; load only the rows needed for the task.
Copilot loads this root file and the nested guides on the path from the repository
root to the session working directory, but not sibling subtrees, so this table
stays the authoritative router for the areas below and for operations that edit no
file in their own scope. The `.github/instructions` files reinforce the file-scoped
rows for Copilot. Bare paths and commands in these guides are relative to the
repository root unless explicitly stated otherwise; Markdown links are relative to
their containing file.

| Area or operation | Required guidance |
| --- | --- |
| Razor components/code-behind, UI/CSS/JavaScript, layouts/navigation, render modes, Fluent providers/assets/registration in either host, UI tests, or changes to Razor authoring checks | [UI and rendering](src/YHAB.UI/AGENTS.md) |
| Identity/account pages, forms, redirects, authorization, cookies, passkeys, or changes to host/shared rendering that can affect account behavior; tests of those contracts | [Account](src/YHAB/Features/Account/AGENTS.md) |
| Server feature boundaries, persistence, PostgreSQL/EF configuration, schema/migrations, readiness/liveness, or their wiring in AppHost, service defaults, deployment, and tests | [Server and persistence](src/YHAB/AGENTS.md) |
| Running/debugging/stopping the application, Aspire resources/tooling/configuration, migration operations, deployment, or infrastructure/browser test orchestration | [Aspire and infrastructure](src/YHAB.AppHost/AGENTS.md) |
| Selecting, writing, changing, or running tests, including validation of production-only changes | [Tests](tests/AGENTS.md) |
| Authoring or changing C# or Razor: class visibility and sealing, primary constructors, OneOf outcomes, and extension members | [C# and Razor conventions](.github/instructions/csharp.instructions.md) |

## Execution and validation entry points

- Use Aspire as the application run/debug entry point; do not launch the web or
  WebAssembly project alone. Read the Aspire guide before lifecycle operations,
  including stopping Aspire before formatting/full builds on Windows. Ordinary
  shutdown must preserve local data; destructive cleanup requires explicit intent.
- Do not commit local credentials, hard-code ports, or make development containers
  persistent processes.
- Run relevant tests during implementation and both unit and component projects
  before completing code changes, plus affected infrastructure suites. Read the
  test guide before choosing or running them, even if no test files change.
  Headless unit/component tests do not need Aspire. The build and formatting
  requirements below still apply.

## Project boundaries and rendering

- Keep browser-compatible shared pages, components, and layouts in `YHAB.UI`.
  Identity, persistence, and server services belong in `YHAB`; WebAssembly
  startup belongs in `YHAB.Client`. Preserve the dependency direction:
  `YHAB.Client` and `YHAB.UI` must not reference the server project.
  Keep `YHAB.SharedKernel` usable by both server and browser code.
- SharedKernel/UI/Client project references are checked during builds, and compiled
  dependencies are checked by [architecture tests](tests/README.md#architecture-boundaries).
  Update the documented design and its checks together when intentionally changing
  them. Server feature and persistence rules live in the server guide.
- Fluent UI Blazor services are registered in both hosts. Keep `MainLayout` and
  Identity navigation static. Place `FluentProviders` inside the interactive
  page or subtree that uses them, inheriting its renderer and service scope;
  do not start an interactive runtime on every static page just for providers.
  Do not pass the layout's `Body` across a render-mode boundary.

- Default to static SSR with enhanced navigation; opt into `InteractiveAuto` where
  interaction needs a .NET renderer. Account pages remain static SSR. Read the
  account guide before changing their contracts, including through host startup
  or shared rendering.

## Project settings and dependencies

- Nullable reference type analysis is enabled centrally in `Directory.Build.props`.
  New projects inherit it; do not repeat or disable it in individual projects.
- NuGet package versions belong in the root `Directory.Packages.props`. Add
  versionless `PackageReference` items to consuming projects and preserve metadata
  such as `PrivateAssets`. Shared analyzer references live in `Directory.Build.props`.
- Keep SDK versions in their SDK declarations or `global.json`; they are not
  NuGet `PackageReference` versions. Do not introduce per-project version overrides
  or nested central package files without a deliberate, documented need.
- In application code, obtain the current instant from an injected `TimeProvider`;
  server budgeting services use `BudgetClock` for budget-local dates. Pass explicit
  dates/instants into shared pure calculations. `RS0030` bans direct system-clock
  properties in the four application projects. See
  [build rules](build/README.md#banned-apis) for scope.

## Validation

- Application I/O methods require an explicit final `CancellationToken` without
  an optional default. The request-bound Identity workflows use the documented
  scoped `IdentityCancellation` bridge for tokenless manager APIs. Forward the
  caller's token through HTTP, services, EF and execution-strategy retries;
  preserve framework override/event signatures.
  CA2016, CA1068, MA0032, MA0040, MA0079 and MA0080 are build-enforced. Explicit
  `None`/`default` needs a concrete lifetime/commit/cleanup reason, not a workaround.
- Use request cancellation for static SSR, component lifetime for interactivity,
  and host shutdown for background workers. Never serialize tokens or reuse a
  completed prerender request's token in a circuit.
- Cancellation is not rollback. Preserve uncertain-write IDs and confirmed-save
  refresh states; allow required follow-up after an irreversible Identity write
  through `IdentityCancellation.CompleteWrite()`. See
  [cancellation lifetimes](docs/budgeting.md#cancellation-lifetimes) for boundaries.
- Compiler and analyzer warnings fail builds through `TreatWarningsAsErrors`.
  `.editorconfig`, `Directory.Build.props`, and `CodeMetricsConfig.txt` govern
  diagnostics. Fix findings without weakening shared settings; keep necessary
  exceptions narrow and justified. See [build/README.md](build/README.md) for
  analyzer rules and the documented SDK compatibility workaround.
- Run `dotnet build YHAB.slnx` after code changes. The Razor code-behind policy
  is checked automatically during builds; see `build/README.md` for diagnostics.
- For every change set, including configuration and documentation changes, run
  `dotnet format YHAB.slnx --severity warn` from the repository root, review its
  fixes, and require the `--verify-no-changes` pass to exit zero before completing.
  Do not suppress diagnostics or exclude maintained source to obtain a clean pass.
  [build/README.md](build/README.md#formatting) owns the formatter's scope,
  targeted commands, and the non-C# limits it does not cover.
- After changing the policy or upgrading the SDK, also run
  `pwsh ./scripts/Test-RazorCodeBehind.ps1`.
- Fix violations rather than disabling checks or adding unrelated placeholder
  files. An empty matching partial class is valid for a markup-only component.

## Agent documentation review

- Review documentation as part of implementation work, before any authorized
  commit and before reporting completion. Update affected instructions and docs;
  leave accurate documentation unchanged. In the final response, state
  `Documentation review: complete.` followed by the outcome once review is done.
  Do not report completion for a pending or blocked review.
  This does not authorize a commit or broaden a read-only task.
- Keep durable agent conventions in root or nested `AGENTS.md` files, or in a
  path-scoped `.github/instructions` file, following the instruction scope rules
  above. Setup and runtime workflows belong in `README.md`, build rules in
  `build/README.md`, and test conventions in `tests/README.md`.
  Update existing feature and workflow documentation rather than duplicating it.
- The project-owned Codex `UserPromptSubmit`/`Stop` hooks and their GitHub Copilot
  `userPromptSubmitted`/`agentStop` equivalents supply this reminder and record a
  workspace baseline. The companion finishing hook requests at most one finishing
  review when the workspace changes during a turn and the final response lacks a
  completed-review acknowledgement. These hooks are advisory, not proof of
  documentation accuracy or a Git commit gate.
  See [agent hook maintenance](build/agent-hooks.md) for setup and validation.
