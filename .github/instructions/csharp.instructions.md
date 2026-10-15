---
applyTo:
  - "**/*.cs"
  - "**/*.razor"
---

# C# and Razor authoring conventions

Handwritten C# and Razor conventions for every project. These are path-scoped to
the files they govern so changes to documentation, workflows, or configuration do
not carry them. The [repository-wide instructions](../copilot-instructions.md)
rules for project boundaries, dependencies, and validation apply on top.

## Visibility and inheritance

- When authoring or changing classes, use the narrowest practical visibility and
  seal concrete classes unless inheritance has a purpose. Default to
  `private sealed` for nested implementation classes and `internal sealed` for
  namespace-level implementation classes; namespace-level classes cannot be
  `private`.
- Expose types across assemblies or allow inheritance when a current contract,
  framework requirement, or deliberate base-class design calls for it. Document
  the reason for a type-level analyzer exception on the affected type.
- Preserve generated Razor component accessibility and Blazor parameter contracts.
  Concrete components can be `public sealed partial class`; intentional base
  components remain inheritable. Assess cross-project usage before sealing a
  public type or reducing its visibility. A required public type in an executable
  project needs a type-level `SuppressMessage` with a concrete justification, as
  on `PasskeyOperation`; `.razor.cs` files keep private implementation members.
- The sealing and accessibility analyzers (S3260, MA0053, CA1515) and their
  project-scope limits are documented in
  [build rules](../../build/README.md#sealing-and-accessibility). This authoring
  convention applies where those analyzers cannot enforce it too.

## Constructors

- Use primary constructors whenever a handwritten class or struct needs an
  explicit instance constructor, including Razor code-behind. Additional
  constructor overloads must delegate to the primary constructor with `this(...)`.
  Types that need no explicit constructor do not need an empty primary constructor.
  Static constructors are unaffected.
- Keep Blazor `[Parameter]` and `[CascadingParameter]` inputs as properties. Perform
  initialization that depends on these inputs in the appropriate lifecycle method.
- The primary-constructor requirement also applies where the analyzer cannot
  suggest the conversion, and it does not migrate existing `@inject` or `[Inject]`
  property injection. `IDE0290` enforcement is documented in
  [build rules](../../build/README.md#sealing-and-accessibility).

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
- Keep outcomes close to their feature and out of unrelated layers. Share cases
  only when their meaning is identical. Keep transport models separate;
  do not expose OneOf directly through forms, cookies, persistence models, or HTTP
  contracts.
- Test each meaningful case, its payload, and its observable handling. For
  multi-step operations, verify that failures skip later operations and that
  partial completion does not produce complete-success messages or logs.

Examples: [PasskeySubmission](../../src/YHAB/Features/Account/Models/PasskeySubmission.cs)
models mutually exclusive inputs;
[EnableAuthenticatorOutcome](../../src/YHAB/Features/Account/Models/EnableAuthenticatorOutcome.cs)
distinguishes complete success from partial completion.

## C# extension members

- Prefer C# 14 extension blocks for handwritten extensions. Group related members
  by receiver and concern, using internal static containers for feature-local APIs.
  Preserve existing public contracts and document any compatibility or generator
  requirement that needs classic extension-method syntax.
- Use properties for inexpensive, side-effect-free facts and methods for
  transformations, enumeration, async work, or side effects. Keep core behavior
  and factories on types we own; do not convert every static helper.
- Follow the [C# extension members skill](../../.agents/skills/csharp-extension-members/SKILL.md)
  for receiver/generic rules, migration checks, and compiled examples. Keep this
  repository on C# 14; do not copy preview extension indexers or SDK settings.
