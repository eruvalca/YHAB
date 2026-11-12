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

## Union types and operation outcomes

- Use unions for meaningful alternatives that need different handling or payloads,
  or to replace fields and flags representing mutually exclusive states. Keep
  ordinary optional values, independent booleans, and adequate existing contracts
  such as simple `IdentityResult` operations. Do not introduce a universal result
  abstraction or convert every operation to a union.
- Declare a named union per reusable service outcome:
  `internal union Name(Name.CaseA, Name.CaseB);`. Nest the case declarations in the
  union body so they stay namespaced, use descriptive records for payloads and
  marker structs for cases without data, and let the generated constructors and
  implicit conversions create values. The generated union boxes value-type cases,
  so prefer reference-type cases where allocation matters. A small local helper
  does not automatically need a named union.
- Dispatch with a `switch` expression so the compiler checks every case (`CS8509`).
  Use one that returns a value or `Task`, including for side effects: a `switch`
  statement is not exhaustiveness-checked and silently ignores a new case. Name
  every case, and reserve `_` for a deliberate merge that an `or` pattern should
  state explicitly. Avoid routine control flow through the union's `.Value`;
  direct case inspection is appropriate in tests.
- Add a `null` arm wherever default-initialized union values can reach a switch.
  Union contents are stored in `object?`, so `default` holds null and an otherwise
  exhaustive switch throws `SwitchExpressionException` at run time.
- Keep behavior that inspects the union's own cases out of the union body: a
  type-pattern switch there trips `S3060`, so put it in a nearby extension member.
  `CA1508` also reports union case patterns as dead code; the affected files carry
  a narrow, justified `.editorconfig` exception instead of per-site pragmas.
- Represent anticipated failures and partial completion accurately, including
  earlier writes that succeeded. Preserve unexpected exceptions and cancellation;
  do not catch every exception into a generic failure outcome.
- Keep outcomes close to their feature and out of unrelated layers. Share cases
  only when their meaning is identical. Keep transport models separate; do not
  expose unions directly through forms, cookies, persistence models, or HTTP
  contracts, which also cannot round-trip a union through serialization.
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
  repository on the language version its target framework selects (C# 15 for
  `net11.0`); do not copy preview-only features or SDK settings.

## C# 14 features without analyzer coverage

No SDK rule reports these two conventions, so the build does not catch a
regression.

- Use `field` when exactly one property accessor needs a body and no other member
  reads the value: `private bool IsBusy { get => Busy || field || _error is not null; set; }`
  in `AccountRegister` and `BudgetBoard`. Keep a real `_field` when other members
  read or write it, as `InlineNameEditor.Name` keeps `_name`.
- Write parsing and character-scanning helpers against `ReadOnlySpan<char>` and
  let the implicit string conversion supply the receiver; do not add
  `string`/`char[]` overload triples. `AmountExpression` is the model.
