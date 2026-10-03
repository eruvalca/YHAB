---
name: csharp-extension-members
description: Author, review, or migrate C# extension members in YHAB using C# 14 extension blocks. Use for extension API design, classic extension-method migration, and receiver-focused helpers; not for unrelated C# modernization.
---

# C# extension members

Use extension blocks for handwritten extensions when the selected language version
supports C# 14. YHAB targets .NET 10; its selected SDK already defaults to C# 14.
Check `global.json` and the consuming project's evaluated `LangVersion` before
using these instructions elsewhere. Do not enable preview or upgrade dependencies
to adopt this syntax.

## Choose the API deliberately

- Keep core behavior and factories on types we own. Extensions suit scoped
  functionality on framework or external types, or functionality belonging to a
  different application layer. A static helper is not automatically an extension.
- Use a property for an inexpensive, side-effect-free fact. Keep formatting,
  conversion, enumeration, async work, and side effects as methods. Do not turn
  a method into a property as part of a compatibility-only syntax migration.
- Group related members by receiver and concern in top-level, nongeneric static
  classes. Prefer `internal static` containers and internal members for local
  helpers. Preserve public containers and members needed across assemblies.
- Keep extensions in their feature namespace; import it at the actual call sites.
  Avoid global catch-all extensions on `object`, unconstrained `T`, or `string`
  with generic names such as `Normalize` or `Format`.
- Static extensions and operators require a useful type-level concept or
  unsurprising algebra. Do not introduce them just to demonstrate syntax.

## Declare and migrate

1. Identify the receiver, callers, and any public or generated contracts. Inspect
   explicit static calls, named arguments, method groups, and reflection usage.
2. Declare `extension(Receiver receiver)` inside the existing static class. Members
   acting on that receiver are declared without `static`; type-level extensions
   retain `static`. An unnamed receiver is suitable for static-only blocks.
3. Move receiver-dependent generic parameters and constraints onto the block;
   leave additional method type parameters on the method. The emitted static
   signature concatenates block parameters first, then method parameters. Preserve
   the original order; if a migration would change it, keep the classic form and
   document the compatibility reason instead of silently breaking callers.
4. Preserve the containing type/namespace, visibility, method and parameter names,
   defaults, nullability, attributes, return types, and behavior. Existing instance
   extension methods still expose compatible static implementation methods; do not
   add duplicate forwarding wrappers with the same signature.
5. Keep receiver attributes on the receiver declaration and member attributes on
   the member. Use separate blocks when receiver contracts differ. Keep public
   argument guards, including their receiver names; annotations do not validate
   values at runtime. Nullable receivers are valid when handling null is intended.
6. Migrate actual bindings, inspect the diff, and build with the repository's
   analyzers enabled. Use the existing build/test workflow in AGENTS.md; do not
   suppress diagnostics broadly or introduce a custom syntax gate.

## Boundaries that affect correctness

- Real instance/static members take precedence over extensions. Namespace imports
  affect lookup and can introduce ambiguity. Preserve generic inference and check
  both fluent and explicit static calls when changing an existing API.
- Extensions do not gain access to private members of the receiver. Keep owned
  initialization inside a constructor or an appropriate member rather than
  exposing mutable state to make an extension work.
- Extension properties add behavior, not per-instance storage: use explicit
  accessors or expression bodies, not auto-properties, `field`, or `init`.
- C# 14 extension members cannot be `partial`, `virtual`, `override`, `abstract`,
  or `protected`. Keep source-generated `[LoggerMessage]` partial declarations
  outside extension blocks. Do not edit generated source.
- Extension-property access is prohibited in expression trees (CS9296), including
  expression-based form bindings or query expressions. An ordinary extension
  method also does not automatically become translatable by EF. Keep domain helpers
  outside database expressions unless the provider explicitly supports translation.
- Value-type receivers are passed by value by default. Use `ref` only for deliberate
  mutation of a struct (or a generic receiver constrained to `struct`). C# 14 `in`
  and `ref readonly` extension receivers require a concrete value type. Preserve
  receiver modifiers and caller mutation behavior during migration.
- C# 14 includes extension methods, properties, and operators. Extension indexers
  are C# 15; current Learn pages may mix released and preview examples. Do not copy
  a tutorial's .NET 11/preview project settings into this repository.

Read [Examples and YHAB decisions](references/examples-and-decisions.md) when
writing a new block or checking migration details. It contains a standalone C# 14
fixture and the reasons for the repository's adoption boundaries.

## Authoritative references and targeted lookup

Verified against the selected .NET 10 SDK on 2026-09-27:

- [Programming guide](https://learn.microsoft.com/dotnet/csharp/programming-guide/classes-and-structs/extension-methods)
- [Extension declaration reference](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/extension)
- [C# 14 feature specification](https://learn.microsoft.com/dotnet/csharp/language-reference/proposals/csharp-14.0/extensions)
- [Declaration diagnostics](https://learn.microsoft.com/dotnet/csharp/language-reference/compiler-messages/extension-declarations)
- [Expression-tree restrictions](https://learn.microsoft.com/dotnet/csharp/language-reference/compiler-messages/expression-tree-restrictions)

For unfamiliar cases, use Learn search with the specific question, for example
`C# 14 extension block generic parameter order compatibility`,
`C# 14 extension receiver nullability attributes`, or
`C# 14 extension properties expression trees CS9296`. Fetch the relevant page and
verify examples against the selected compiler rather than relying on search snippets.

| Learn MCP tool | CLI fallback |
| --- | --- |
| `microsoft_docs_search(query: "...")` | `npx @microsoft/learn-cli search "..."` |
| `microsoft_code_sample_search(query: "...", language: "csharp")` | `npx @microsoft/learn-cli code-search "..." --language csharp` |
| `microsoft_docs_fetch(url: "...")` | `npx @microsoft/learn-cli fetch "..."` |

No extra tool installation is required for normal use of this skill. Invoke the
CLI fallback only when documentation lookup is needed and Learn MCP is unavailable.
