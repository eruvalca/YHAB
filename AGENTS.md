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
  directory. Keep each one a short pointer to the owning `AGENTS.md` rather than a
  second copy of its rules, because Copilot combines instruction sources and
  defines no precedence between them. See
  [instruction discovery](build/agent-hooks.md#instruction-discovery).
- Put detailed, reusable workflows in repository-owned `.agents/skills`, with
  clear activation descriptions and short conditional routing instructions when
  their use is mandatory. This also suits file-type guidance spanning several
  directories. Reference existing setup, feature, build, and test documentation
  instead of copying it into instructions or skills.
- During documentation review, check new or changed instructions for appropriate
  scope, discoverability, and duplication. Apply this to the current task; do not
  reorganize unrelated existing guidance solely to complete the review.

## Read the guidance for the task

The root rules apply throughout the repository. Before planning, reading, or
changing the areas below, read the linked guide as well as applicable ancestor
`AGENTS.md` files. Follow these routes even when the affected code, configuration,
or tests live outside the guide's own directory. Apply the relevant rules to that
work; load only the rows needed for the task. Copilot loads this root file and the
nested guides on the path from the repository root to the session working
directory, but not sibling subtrees, so this table stays the authoritative router
for the areas below and for operations that edit no file in their own scope. The
`.github/instructions` routers reinforce the file-scoped rows for Copilot. Bare
paths and commands in these guides are relative to the repository root unless
explicitly stated otherwise; Markdown links are relative to their containing file.

| Area or operation | Required guidance |
| --- | --- |
| Razor components/code-behind, UI/CSS/JavaScript, layouts/navigation, render modes, Fluent providers/assets/registration in either host, UI tests, or changes to Razor authoring checks | [UI and rendering](src/YHAB.UI/AGENTS.md) |
| Identity/account pages, forms, redirects, authorization, cookies, passkeys, or changes to host/shared rendering that can affect account behavior; tests of those contracts | [Account](src/YHAB/Features/Account/AGENTS.md) |
| Server feature boundaries, persistence, PostgreSQL/EF configuration, schema/migrations, readiness/liveness, or their wiring in AppHost, service defaults, deployment, and tests | [Server and persistence](src/YHAB/AGENTS.md) |
| Running/debugging/stopping the application, Aspire resources/tooling/configuration, migration operations, deployment, or infrastructure/browser test orchestration | [Aspire and infrastructure](src/YHAB.AppHost/AGENTS.md) |
| Selecting, writing, changing, or running tests, including validation of production-only changes | [Tests](tests/AGENTS.md) |

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

## Visibility and inheritance

- Use file-scoped namespaces and braces around C# control-flow bodies.
- When authoring or changing classes, use the narrowest practical visibility and
  seal concrete classes unless inheritance has a purpose. Default to
  `private sealed` for nested implementation classes and `internal sealed` for
  namespace-level implementation classes; namespace-level classes cannot be
  `private`.
- Expose types across assemblies or allow inheritance when a current contract,
  framework requirement, or deliberate base-class design calls for it. Document
  the reason for analyzer exceptions on the affected type.
- Preserve generated Razor component accessibility and Blazor parameter contracts.
  Concrete components can be `public sealed partial class`; intentional base
  components remain inheritable. Assess cross-project usage before sealing a
  public type or reducing its visibility.
- S3260 and MA0053 are warnings for eligible classes; MA0053 includes public
  types. MA0053 sees inheritance only within the current project. Use a justified
  type-level exception for an intentional base class consumed by another project.
  The authoring convention also applies where the analyzers cannot enforce it.
- CA1515 is a warning for public types in executable projects. Keep its default
  exclusion of class libraries; review their exported contracts deliberately.
  `.razor.cs` files are exempt from CA1515 because generated components are public,
  but still use private implementation members and the other C# rules. Other
  required public types need a type-level `SuppressMessage` with a concrete
  justification, as on `PasskeyOperation`.

## Constructors

- Use primary constructors whenever a handwritten class or struct needs an
  explicit instance constructor, including Razor code-behind. Additional
  constructor overloads must delegate to the primary constructor with `this(...)`.
  Types that need no explicit constructor do not need an empty primary constructor.
  Static constructors are unaffected.
- Keep Blazor `[Parameter]` and `[CascadingParameter]` inputs as properties. Perform
  initialization that depends on these inputs in the appropriate lifecycle method.
- IDE0290 is an error for conversions supported by the SDK analyzer. The authoring
  requirement also applies where the analyzer cannot suggest a conversion. This
  rule does not migrate existing `@inject` or `[Inject]` property injection.

## OneOf and operation outcomes

- Use unions for meaningful alternatives that need different handling or payloads,
  or to replace fields and flags representing mutually exclusive states. Keep
  ordinary optional values, independent booleans, and adequate existing contracts
  such as simple `IdentityResult` operations. Do not introduce a universal result
  abstraction or convert every operation to OneOf.
- Prefer named, source-generated `OneOfBase` types for reusable service outcomes:
  `[GenerateOneOf] internal sealed partial class ... : OneOfBase<...>`. Use
  descriptive case records for payloads and marker structs for cases without data.
  Let the generator supply constructors and conversions. A small local helper does
  not automatically need a named union class.
- Prefer exhaustive `Match` when producing a value and `Switch` for synchronous
  side effects. Avoid routine control flow through the union's `.Value`, `IsTn`,
  or `AsTn`. `TryPick` is appropriate when deliberately separating one case and
  handling or forwarding the remaining union; direct case inspection is also
  appropriate in tests.
- Return and await task-returning `Match` for asynchronous branches
  (`Match<Task>` or `Match<Task<T>>`); let the compiler infer the type arguments
  when they are redundant under IDE0001. Never pass asynchronous lambdas to
  `Switch`, whose callbacks return `void`.
- Represent anticipated failures and partial completion accurately, including
  earlier writes that succeeded. Preserve unexpected exceptions and cancellation;
  do not catch every exception into a generic failure outcome.
- Keep outcomes close to their feature with the narrowest practical visibility.
  Share cases only when their meaning is identical. Keep transport models separate;
  do not expose OneOf directly through forms, cookies, persistence models, or HTTP
  contracts.
- Test each meaningful case, its payload, and its observable handling. For
  multi-step operations, verify that failures skip later operations and that
  partial completion does not produce complete-success messages or logs.

Examples: [PasskeySubmission](src/YHAB/Features/Account/Models/PasskeySubmission.cs)
models mutually exclusive inputs;
[EnableAuthenticatorOutcome](src/YHAB/Features/Account/Models/EnableAuthenticatorOutcome.cs)
distinguishes complete success from partial completion.

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
  `dotnet format YHAB.slnx --severity warn` from the repository root and review
  its fixes. Before completion, require a clean
  `dotnet format YHAB.slnx --severity warn --verify-no-changes` pass. This covers
  whitespace, code style (including IDE0001 name simplification), and analyzer
  fixes at warning/error severity; suggestions remain review guidance. Do not
  suppress diagnostics or exclude maintained source just to obtain a clean pass.
  Keep generated files under their owning tools' control.
  Resolve remaining findings manually without weakening `.editorconfig`. The
  formatter does not replace builds, tests, or review of non-C# files. See
  [build/README.md](build/README.md#formatting) for scope and targeted commands.
- After changing the policy or upgrading the SDK, also run
  `pwsh ./scripts/Test-RazorCodeBehind.ps1`.
- Fix violations rather than disabling checks or adding unrelated placeholder
  files. An empty matching partial class is valid for a markup-only component.

## C# extension members

- Prefer C# 14 extension blocks for handwritten extensions. Group related members
  by receiver and concern, using internal static containers for feature-local APIs.
  Preserve existing public contracts and document any compatibility or generator
  requirement that needs classic extension-method syntax.
- Use properties for inexpensive, side-effect-free facts and methods for
  transformations, enumeration, async work, or side effects. Keep core behavior
  and factories on types we own; do not convert every static helper.
- Follow the [C# extension members skill](.agents/skills/csharp-extension-members/SKILL.md)
  for receiver/generic rules, migration checks, and compiled examples. Keep this
  repository on C# 14; do not copy preview extension indexers or SDK settings.

## Agent documentation review

- Review documentation as part of implementation work, before any authorized
  commit and before reporting completion. Update affected instructions and docs;
  leave accurate documentation unchanged. In the final response, state
  `Documentation review: complete.` followed by the outcome once review is done.
  Do not report completion for a pending or blocked review.
  This does not authorize a commit or broaden a read-only task.
- Keep durable agent conventions in root or nested `AGENTS.md` files according to
  the instruction scope rules above, setup and runtime workflows in `README.md`,
  build rules in `build/README.md`, and test conventions in `tests/README.md`.
  Update existing feature and workflow documentation rather than duplicating it.
- The project-owned Codex `UserPromptSubmit`/`Stop` hooks and their GitHub Copilot
  `userPromptSubmitted`/`agentStop` equivalents supply this reminder and record a
  workspace baseline. The companion finishing hook requests at most one finishing
  review when the workspace changes during a turn and the final response lacks a
  completed-review acknowledgement. These hooks are advisory, not proof of
  documentation accuracy or a Git commit gate.
  See [agent hook maintenance](build/agent-hooks.md) for setup and validation.
