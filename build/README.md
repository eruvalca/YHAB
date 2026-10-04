# Build conventions

## .NET SDK

`global.json` selects the highest installed stable .NET 10.0 SDK at or above
10.0.401 (`rollForward: latestFeature`, `allowPrerelease: false`). Later feature
bands within 10.0 are allowed; a compatible SDK must be installed separately.
This selects build tooling rather than changing the projects' target framework.
SDK updates also update the compiler, built-in analyzers, and Razor parser used by
the code-behind validator. Run the solution build and Razor policy checks after
an SDK update.

## Nullable reference types

`Directory.Build.props` enables nullable reference type annotations and analysis
for every project, including Razor code-behind and the build tooling. New projects
inherit `<Nullable>enable</Nullable>` without repeating it in their project files.
Nullable warnings fail builds through the shared `TreatWarningsAsErrors` setting.
Address nullable contracts and flow analysis instead of disabling the setting in
individual projects. This compile-time analysis does not validate runtime inputs.

## Central package management

The root `Directory.Packages.props` enables NuGet Central Package Management and
holds the versions of explicitly referenced packages in alphabetical order. Each
consuming project uses a `PackageReference` without a `Version`; shared analyzer
references stay in `Directory.Build.props`. Adding a central `PackageVersion` only
defines its version and does not add that dependency to every project.

When adding or upgrading a package, update its central entry and keep reference
metadata such as `PrivateAssets`, `IncludeAssets`, and conditions on the consuming
reference. The xUnit analyzer remains conditional on `IsTestProject`; it does not
introduce a test framework. All existing package versions and target frameworks
were preserved during adoption. Transitive pinning is not enabled.

SDK declarations, including `Aspire.AppHost.Sdk` and `global.json`, retain their
own version settings. SDK-provided implicit package references remain under SDK
control. Do not introduce nested `Directory.Packages.props` files or per-project
`VersionOverride` entries without a documented need: NuGet automatically imports
only the nearest central package file.

## Fluent UI lifetime compatibility

The server imports `build/FluentAsset.targets` to serve a generated copy of the
pinned Fluent UI 5.0.0 initializer. Its bundled FAST definition observers retain
detached elements, and its theme-string cache retains old theme objects. The
patch substitutes weak element subscriptions with finalizer cleanup and a weak
theme-object cache. Live definition-change notifications are preserved.

`YHAB.Build patch-fluent` verifies the original bundle's SHA-256 before applying
the narrowly scoped replacements. A package upgrade with a different bundle
fails explicitly: reassess the upstream behavior and remove the compatibility
fix when it is no longer needed. Do not weaken the checksum check or edit the
installed NuGet package. The generated module lives under the server's
`IntermediateOutputPath`, is registered for cleaning, and receives new static
asset fingerprint/integrity metadata. `FluentDefinitionObservers.js` is the
maintained callback implementation. Browser validation must use the served asset
and verify collection of removed controls, themes, and extended navigation.

## PWA build assets

The server imports `build/PwaAssets.targets`. Before `AssignTargetPaths`,
`GeneratePwaWorker` hashes maintained application C#, Razor, JavaScript, CSS,
HTML, JSON, manifest, PNG, and project files under `src`, plus central build and
package configuration and build targets/scripts. It excludes `bin` and `obj`.
New/deleted inputs participate on the next build; output is rewritten only when
the content changes. Design-time builds skip generation.

The target prepends that version to the worker source in `build/PwaWorker.js`.
The generated `obj/<configuration>/<framework>/pwa/service-worker.js` is registered
as a static web asset before discovery and copied into published `wwwroot`.
`RegisterPwaWorker` corrects the discovered asset's content root after
`ResolveCoreStaticWebAssets`, so the development file provider resolves the
generated file in `obj` instead of looking for a nonexistent file in `wwwroot`.
Its input list and worker file are registered with `FileWrites` for cleanup.
Do not edit generated files or bump a handwritten worker version. If new asset
types or build inputs are introduced, extend the input list deliberately.

The stable `/service-worker.js` URL includes the generated version, so
application-only changes also trigger browser update detection. Registration uses
`updateViaCache: none` to revalidate the worker; deployment proxies/CDNs must also
permit fresh responses at that URL. Deploy the complete publish output together.
The worker caches only the three public offline fallback assets, removes only
old `yhab-offline-*` caches on activation, and waits for user consent while an
existing worker still controls open tabs. Do not apply the standalone Blazor
WebAssembly template's cached `index.html` strategy to this mixed SSR/Auto app.

Validate both a solution build and `dotnet publish src/YHAB/YHAB.csproj
--configuration Release --output artifacts/pwa-publish`. Confirm the manifest,
icons, generated worker, and fallback assets are present. Browser
checks run against the actual Aspire-hosted application with disposable storage;
see the [test guide](../tests/README.md#choosing-an-integration-layer).

## Formatting

For every change set, including configuration and documentation changes, run from
the repository root after stopping any running Aspire instance:

```powershell
dotnet format YHAB.slnx --severity warn
dotnet format YHAB.slnx --severity warn --verify-no-changes
```

The first command applies whitespace, code-style, and analyzer fixes. Review the
diff for preserved behavior, then require the verification command to exit zero
before completion. Resolve remaining actionable findings manually and rerun the
check; do not silence diagnostics or omit files to manufacture a clean result.
Use `--no-restore` only when restore assets already match the current project and
package configuration. Do not pass `--include-generated` or edit generated code.

During development, `dotnet format whitespace YHAB.slnx`,
`dotnet format style YHAB.slnx --severity warn`, and
`dotnet format analyzers YHAB.slnx --severity warn` can target an individual
category. A focused `--include` is useful while editing, but the final verification
must cover the whole solution. Warning and error rules are mandatory; rules set
to suggestion remain review guidance rather than bulk refactoring requirements.

`dotnet format` is not a universal EditorConfig validator: it operates on the
solution's supported source documents and available analyzer fixes. Review Razor
markup, scripts, documentation, and configuration separately for their applicable
EditorConfig settings. A clean formatting check does not replace the solution
build, the Razor policy checks when required, or the relevant test suites after code
changes. [Command reference](https://learn.microsoft.com/dotnet/core/tools/dotnet-format).

Text files use UTF-8 without a byte-order mark (`charset = utf-8`), including C#,
Razor markup and companions, configuration, project files, scripts, and documentation.
This preserves Unicode text and follows the existing C# convention in YHAB rather
than Nova's C# BOM preference. Encoding is an editor setting, not a build diagnostic.
Keep generated and third-party files under their owning tools' control. A file that
requires another encoding needs a documented file-specific EditorConfig override.

Text files use LF line endings. EditorConfig's `end_of_line = lf` and the repository
`.gitattributes` default of `* text=auto eol=lf` align editor saves with Git checkouts,
including on Windows with `core.autocrlf=true`. Git automatically distinguishes text
from binary files. These settings do not themselves produce build diagnostics.
Normalize maintained files without changing literal content; generated and third-party
files remain under their owning tools' control. If a file requires CRLF, document
matching `end_of_line = crlf` and `text eol=crlf` overrides in EditorConfig and Git
attributes. Byte-exact fixtures may instead need `-text` to prevent Git conversion.
Explicit escape sequences such as `\r\n` inside strings are unaffected.

C# and Razor files use spaces with four spaces per indentation level. This includes
`.razor.cs` code-behind and `_Imports.razor`. The EditorConfig indentation settings
guide editors and formatters; they do not themselves emit build diagnostics or
automatically reformat existing files.

`IDE0055` is a warning for C# formatting, including handwritten `.razor.cs`
code-behind. With `EnforceCodeStyleInBuild` and `TreatWarningsAsErrors`, reported
spacing, indentation, and line-layout violations fail builds. The formatter uses
the agreed EditorConfig values and SDK defaults for unspecified formatting options.
Its severity applies to the formatting rule as a whole. This does not enforce
formatting of `.razor` markup, JavaScript, CSS, JSON, or XML; those files retain their
editor guidance. Apply formatting fixes to handwritten C# without editing generated
code or changing literals, contracts, or behavior.

JSON and YAML files (`.json`, `.yml`, and `.yaml`) use spaces with two spaces per
indentation level, including server and Blazor client configuration files.

XML and .NET project/build files (`.xml`, `.csproj`, `.props`, `.targets`, and
`.slnx`) use spaces with two spaces per indentation level. This matches the existing
project/build layout, including Razor SDK projects. Razor markup and C# code-behind
retain four-space indentation. These settings guide editors and formatters without
emitting build diagnostics.

JavaScript, TypeScript, and CSS use spaces with four spaces per indentation level,
including `.razor.js` and `.razor.css` companion files. This follows YHAB' existing
JavaScript and CSS layout rather than Nova's two-space preference.

Trailing whitespace is trimmed by default in editors that honor the setting.
Markdown (`.md` and `.markdown`) and Razor markup (`.razor`, including `_Imports.razor`)
disable automatic trimming to preserve hard line breaks and meaningful output
whitespace. Razor companions (`.razor.cs`, `.razor.js`, and `.razor.css`) use the
default. If another file needs literal trailing spaces, such as in a multiline
string or test fixture, add a documented file-specific EditorConfig section with
`trim_trailing_whitespace = false`. This setting has no analyzer severity and does
not itself fail builds; it does not automatically clean up existing files.

Text files end with a newline (`insert_final_newline = true`), including Markdown,
Razor markup, and Razor companions. This terminates the last line without requiring
an additional blank line. It is an editor setting rather than a build diagnostic
and does not select LF versus CRLF line endings. If a component or fixture needs
an exact ending without a newline, add a documented file-specific exception with
`insert_final_newline = false`.

Use language-aware formatting and preserve meaningful whitespace in Razor output,
including preformatted content, and in string literals. Never edit generated C#.

## Analyzers

`Directory.Build.props` adds Nova's analyzer packages to all C# projects, including
the build tooling; `Directory.Packages.props` holds their versions. References use
`PrivateAssets="all"` so consumers of YHAB packages do not inherit them.

| Package | Purpose |
| --- | --- |
| Meziantou.Analyzer | Correctness, performance, API usage, and Blazor checks |
| SonarAnalyzer.CSharp | Bugs, security, and maintainability checks |
| Roslynator.Analyzers | C# simplification and style checks |
| xunit.analyzers | xUnit test checks; included only when `IsTestProject` is `true` |

`YHAB.UnitTests` and `YHAB.ComponentTests` use xUnit with native
Microsoft.Testing.Platform integration. Shared build props include xUnit analyzers
for these projects; analyzer references alone do not add a test framework or
select a runner. See [tests/README.md](../tests/README.md) for the test stack and
conventions.

Compiler and analyzer warnings emitted during builds fail through
`TreatWarningsAsErrors`. `.editorconfig` records the agreed style preferences and
severity overrides. Suggestions do not fail builds. Rules without an override use
their defaults; adopting the packages does not enable every available rule.

`AnalysisLevel` is explicitly `latest`, following the selected SDK's latest stable
analysis baseline. Review new diagnostics when updating the SDK. This includes
built-in analysis of handwritten Razor code-behind, with the EditorConfig
overrides below still applied. Analyzer package versions are pinned separately.

`AnalysisMode` is `All`, enabling the SDK's broadest built-in rule set as build
warnings. Explicit per-rule severity settings and exceptions in `.editorconfig`
still apply. Some legacy and code-metrics rules require individual opt-in even
with `All`. Review new findings one rule at a time, including Razor and framework
contracts, and record severity decisions in `.editorconfig`. Future code and SDK
updates can introduce additional build-blocking findings. This mode does not
change the separately installed third-party analyzer packages.

Method cyclomatic complexity must not exceed 25 (`CA1502`, warning). The root
`CodeMetricsConfig.txt` records this threshold, and `Directory.Build.props` supplies
it to every C# project's analyzers through `AdditionalFiles`. This rule needs
explicit opt-in even with `AnalysisMode=All`.

Complexity measures branching independently of the `MA0051` method-length guidance,
which remains a suggestion. Apply the limit to handwritten C#, including Razor
code-behind. Review markup branching separately where generated-code analyzer
coverage is limited, and never edit generated Razor output. Resolve findings by
separating meaningful responsibilities rather than splitting methods solely to
reduce the score.

The composite maintainability-index rule (`CA1505`) is explicitly disabled,
matching Nova. It combines code size, operator/operand measurements, and branching
into one score. Prefer the more specific `CA1502` complexity warning and `MA0051`
method-length suggestion, plus review of actual responsibilities. This decision
also applies to Razor code-behind, where lifecycle and binding code need context
that an aggregate score cannot provide.

Class coupling (`CA1506`) is a suggestion across handwritten C#, including Razor
code-behind. `CodeMetricsConfig.txt` records the default limits: 95 referenced types
per type and 40 per method, field, property, or event. The metric counts distinct
referenced types, including framework types; review the responsibilities and reasons
for those dependencies before deciding whether to refactor. Suggestions do not fail
builds.

At adoption, the three findings were Identity endpoint registration (66 types),
application startup (65), and the Razor validator's `Execute` method (44). These
methods coordinate many framework types. No Razor code-behind findings were reported;
future component findings still need lifecycle and binding context. Nova's feature-only
warning scope is not copied because YHAB also keeps Razor components inside its
feature folders.

Remove unused private methods, fields, properties, and events (`IDE0051`, warning).
Check Razor markup, framework binding, and reflection before removing a reported
member. Keep intentional indirect uses with a narrowly scoped, documented exception
when needed. The rule applies to handwritten Razor code-behind; required empty
component partial classes remain valid because the diagnostic targets members.

Review private fields and properties that are assigned but never read (`IDE0052`,
warning). A finding may indicate obsolete state or a missing use of the value.
Preserve any required side effects of the assigned expression when removing state.
In Razor code-behind, check markup and framework/reflection-based access before
changing a reported member; document a narrow exception for a required indirect use.

Remove assignments whose values are never used (`IDE0059`, warning). Prefer an
explicit discard (`_ = ...`) when a necessary call produces an intentionally unused
result. Preserve required side effects, exceptions, and any `await`; discarding a
result must not turn awaited work into fire-and-forget work. Apply this convention
in Razor code-behind and rendering code, where generated-code analyzer coverage
can be incomplete.

Review unused parameters in public and non-public methods (`IDE0060`, warning;
`dotnet_code_quality_unused_parameters = all`). Preserve required callback and
interface signatures, framework-bound names, and other external contracts. Use
an intentionally unused parameter name such as `_` only when renaming is safe;
otherwise document a narrowly scoped exception when needed. This applies to
Razor code-behind methods, including callbacks, but not Blazor `[Parameter]`
properties.

Consume `ValueTask` instances correctly (`CA2012`, warning), normally by directly
awaiting each returned instance once. Repeated consumption, ignoring the operation,
or reading its result before completion can be unsafe. This applies to handwritten
Razor code-behind too. The explicit-discard preference does not justify discarding
the asynchronous operation itself; preserve any required `await`. The explicit
severity records the warning already enabled by the SDK's `All` analysis mode.

### Cancellation enforcement

Cancellation diagnostics are warnings and therefore fail builds:

| Rule | Enforcement |
| --- | --- |
| `CA2016` | Forward the enclosing method's final token parameter to supporting calls. |
| `MA0040` | Use an available token, including accessible fields/properties, for cancellable calls. |
| `MA0032` | Identify cancellable calls in methods that have no available token. |
| `CA1068` | Put token parameters last, subject to the rule's framework/signature exceptions. |
| `MA0079`, `MA0080` | Forward tokens to async enumeration, including methods without an available token. |

Keep application I/O contracts explicit: `IBudgetClient` and both implementations
require a final token without a default value. Omitting the argument is then a C#
compile error. A contract regression test prevents optional defaults reappearing.
New application I/O services follow the same convention; request-bound Identity
workflows use the documented scoped `IdentityCancellation` bridge. Framework overrides,
event handlers and callbacks retain their required signatures; obtain their token
from the appropriate lifetime and pass it to helpers. MA0032 deliberately excludes
some constrained signatures, so it is not a universal declaration rule.

These analyzers do not prove cancellation ownership, token identity, propagation
through every abstraction, disposal, or behavior after a database commit. Explicit
`CancellationToken.None`/`default` can bypass forwarding rules. Use such opt-outs
only at a documented boundary, such as host-owned shutdown or independent cleanup;
do not silence a diagnostic by supplying an unrelated token. APIs that do not
accept cancellation (some Identity, authentication-state, and Playwright APIs)
need framework-specific handling; canceling `WaitAsync` only cancels the wait.

See [application cancellation lifetimes](../docs/budgeting.md#cancellation-lifetimes)
and the regression coverage in [tests/README.md](../tests/README.md).
Rule references: [CA2016](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca2016),
[Meziantou rules](https://github.com/meziantou/Meziantou.Analyzer/tree/main/docs/Rules).

Eligible private fields must be `readonly` (`IDE0044`, warning) when assigned only
at declaration or during construction. This includes handwritten Razor code-behind;
fields changed by lifecycle methods or events remain mutable. Component parameters
and injected properties are unaffected. A readonly reference prevents replacing
the referenced object, not modifying the object's contents.

Primary constructors can initialize readonly fields and remain required. Primary
constructor parameters themselves are outside IDE0044; the installed Meziantou
analyzer already checks their reassignment through `MA0143` (warning by default).
Do not introduce extra fields solely to satisfy IDE0044 or move parameter-dependent
component initialization out of lifecycle methods.

Use available tuple element names (`IDE0033`, warning), such as `result.Success`
instead of `result.Item1`. This does not require names on every tuple or prohibit
deconstruction. The rule applies to handwritten Razor code-behind; follow the same
convention in markup, where generated-code analyzer coverage can be incomplete.
The Razor validator already uses the `Name` and `Source` tuple element names.

Prefer auto-properties for trivial backing-field/property pairs (`IDE0032`,
warning). Preserve get-only and private-set contracts, and retain accessors that
perform meaningful work. This rule does not require ordinary fields to become
properties and is compatible with primary constructors and readonly fields.

In Razor code-behind, Blazor separately checks that `[Parameter]` properties are
auto-properties (`BL0007`). Keep parameter-dependent work in `OnParametersSet` or
`OnParametersSetAsync`, rather than parameter accessors. Changing the IDE0032
preference does not change this independent framework check.

Prefer compound assignments (`IDE0054` and `IDE0074`, warning), such as
`total += amount` and `value ??= fallback`, where the analyzer identifies a valid
simplification. The latter assigns only when the existing value is null. Apply
the same preference in Razor code-behind; no component-specific exception is
needed. This preference does not change where lifecycle work belongs or permit
writes to readonly fields or reassignment of primary-constructor parameters.

Simplify redundant boolean conditional expressions (`IDE0075`, warning), such as
`isReady ? true : false` to `isReady`, or `isReady ? false : true` to `!isReady`.
This does not prohibit ternaries that select different text, CSS classes, or
other values. The rule applies to handwritten Razor code-behind; follow the same
convention in markup, where generated-code analyzer coverage can be incomplete.

Prefer object initializers (`IDE0017`, warning) for eligible property assignments
immediately following object creation, such as `new Options { Enabled = true }`.
Primary constructor arguments still belong in the constructor call; additional
initial property values can follow in an initializer.

Apply this preference to objects created in Razor code-behind while retaining
parameter-dependent initialization in the appropriate lifecycle method. Do not
manually construct Blazor components or replace Identity's user-store APIs with
direct property assignments to satisfy this rule.

Prefer collection initializers for eligible `Add` calls immediately after creating
a collection (`IDE0028`, warning via `dotnet_style_collection_initializer`). For
example, use `new List<int> { 1, 2 }` to group its initial contents. Later updates
to existing collections remain normal. Retain constructor arguments such as
explicit comparers, including in Razor code-behind, and keep initialization in
the appropriate lifecycle method.

Collection-expression conversions to `[]` remain suggestions with
`when_types_exactly_match`. Keep these two option severities separate rather than
adding a blanket diagnostic severity override for collection initialization.

Omit unnecessary `this.` qualification on fields, properties, methods, and events
(`IDE0003`, warning), including in Razor code-behind. Keep qualification when it
is needed to distinguish a member from a same-named parameter or local variable.
This preference is compatible with primary constructors and does not apply to
passing `this` as an argument.

`IDE0001` (warning) requires simplifying redundant namespace/type qualification
and inferable generic type arguments where binding is unchanged. Keep qualification
or type arguments when they resolve an ambiguity. In particular, an inferred
task-returning `Match` preserves the asynchronous OneOf contract; it must still be
returned or awaited, and asynchronous lambdas must never be passed to `Switch`.
IDE0001 and IDE0003 are editor/formatter diagnostics in the current SDK; an
ordinary build alone does not establish compliance. The required `dotnet format`
pass checks these conventions, including handwritten Razor code-behind. Generated
Razor output must remain untouched.

Methods returning awaitable types, including `Task` and `ValueTask`, must end in
`Async` (`MA0137`, warning). This includes methods that forward an awaitable
without using the `async` keyword. Keep framework-required method names intact.

Razor event handlers in code-behind follow the same convention. Update their
markup bindings when renaming them, while preserving form names, posted field
names, action values, and routes. These submission contracts are independent of
the C# handler's name. Generated Razor output remains untouched.

Interface names must use PascalCase with an `I` prefix, such as `IEventService`,
at every accessibility level, including internal and nested interfaces. The
naming rule is a warning and is enforced during builds through `IDE1006`.
Configured naming rules share this diagnostic's build severity; account for this
when adding later naming preferences rather than assuming each naming rule's
editor severity independently controls build enforcement.

This convention applies to interface declarations in handwritten Razor
code-behind too. It does not rename component classes or change how they
implement framework interfaces. Generated Razor output remains untouched.

Generic type parameters must use `T` or a descriptive `T`-prefixed PascalCase
name, such as `TItem`, `TResult`, or `TBuilder` (`IDE1006`, warning). This applies
to generic types and methods at every accessibility level, not ordinary method
or primary-constructor parameters.

For generic Razor components, keep `@typeparam` names and the `.razor.cs` partial
class declaration aligned. Renames must also update usages that name the type
parameter, including explicit component type arguments and cascading type-parameter
attributes. Generated Razor output remains untouched.

Private, protected, and private-protected fields use `_camelCase` (`IDE1006`,
warning), including static and readonly fields. Constants at these accessibilities
retain PascalCase through a more-specific naming rule. This convention does not
encourage exposing fields; keep the narrowest practical visibility.

Apply the field convention to handwritten Razor code-behind and update the
corresponding markup references. Keep component parameters, injected properties,
form field names, and other external contracts intact. Class/struct
primary-constructor parameters remain camelCase; no extra backing field is needed
just to use an underscore name. The build's Razor code-behind validator follows
the same field convention.

All constant fields, regardless of accessibility, and constants declared inside
methods use PascalCase (`IDE1006`, warning), for example `const int MaxAttempts = 3`.
The more-specific private/protected constant rule keeps these fields from matching
the `_camelCase` field style. Ordinary locals and class/struct primary-constructor
parameters are unaffected.

This applies to constants in handwritten Razor code-behind. Follow the same
convention for constants in Razor rendering blocks, where generated-code analyzer
coverage can be incomplete.

Both `""` and `string.Empty` are allowed (`RCS1078` is disabled). Explicit
`ConfigureAwait` calls are not required (`CA2007` and `MA0004` are disabled).
Choose context behavior deliberately, especially for Blazor component code.

C# filenames must match their type names (`MA0048`, warning). The only filename
exceptions are `src/YHAB.AppHost/AppHost.cs`, whose top-level statements produce
an implicit `Program` type, and digit-prefixed migration filenames containing an
underscore in `src/YHAB/Data/Migrations`. Other files in those projects remain
checked. `Component.razor.cs` naturally matches the `Component` type.

Use `string.Equals` with an explicit comparison instead of string `==` or `!=`
(`MA0006`, warning). `StringComparison.Ordinal` preserves the operators' existing
case-sensitive and null-handling behavior, including in Razor code-behind.

Choose an explicit string comparer for collections and operations that accept one
(`MA0002`, warning). Use `StringComparer.Ordinal` for case-sensitive keys, or select
another comparer deliberately for the use case. Follow the same convention in
Razor rendering expressions, where build analyzer coverage may be incomplete.

Specify comparison culture and case behavior explicitly at API calls flagged by
`MA0074` (warning). The health-check tracing filters use
`StringComparison.OrdinalIgnoreCase`, preserving `PathString.StartsWithSegments`'s
default behavior. Choose the comparison for each use case rather than applying
case-sensitive or case-insensitive matching everywhere.

Method length is review guidance (`MA0051`, suggestion), using limits of 60 lines
or 40 statements. Prefer focused responsibilities when extracting helpers; length
alone does not determine complexity. Timestamp-prefixed EF migration files are
exempt from this rule because schema operations naturally become long. The rule
applies to handwritten Razor code-behind methods, not the length of Razor markup.

Empty classes are checked (`S2094`, warning). For an intentional empty type, use a
`SuppressMessage` attribute on that class with rule ID `S2094` and a specific
`Justification`. `ApplicationUser` and `UiAssemblyMarker` document the current
exceptions. Keep exceptions scoped to individual classes; do not disable the rule
for an entire file or add dummy members. Required empty Razor partial companions
remain valid and currently produce no findings from this rule.

Commented-out code is checked (`S125`, warning). Keep explanatory comments in
source, and put optional setup examples in documentation. The template's inactive
service-discovery, gRPC, and Azure Monitor snippets are preserved in
[Service defaults options](service-defaults-options.md); they are not enabled.

Unnecessary null-forgiving operators are checked (`S8969`, warning). Remove `!`
when the compiler already considers the expression non-null. This rule does not
ban justified suppressions, including the current `default!` initializers for
framework-populated Razor properties. The operator does not perform a runtime
null check; runtime validation remains a separate concern.

Eligible private classes must be sealed (`S3260`, warning), including nested
helper types in Razor code-behind. `MA0053` (warning) extends sealing checks to
eligible concrete classes and records, with public-type checking enabled.
Concrete Razor components use `public sealed partial class` in code-behind;
generated partial declarations remain untouched. The non-designer part of an EF
migration can likewise declare its class sealed. MA0053 recognizes subclasses
within the same project, so review usage in other projects and document a
type-level exception when a public class is intentionally a base class there.
Abstract classes and the analyzer's default exceptions for exception types and
types declaring virtual members remain unchanged. `AGENTS.md` records the broader
authoring convention of narrow visibility and sealing by default; these sealing
rules do not enforce accessibility.

Public types in executable projects are checked (`CA1515`, warning). Use
`internal` for application implementation types; keep the rule's default exclusion
of class libraries and review their public contracts deliberately. `.razor.cs`
files are exempt only from CA1515 because component accessibility must match
Razor's generated public partial declarations. Keep component implementation
members private, and place independent helper types in ordinary C# files so they
receive the accessibility check. Other required public contracts use a type-level
`SuppressMessage` with a specific `Justification`: `PasskeyOperation` stays public
because it is the type of the public `PasskeySubmit.Operation` parameter.

The .NET 10.0.401 SDK analyzer misidentifies C# 14 extension blocks as public
types and reports CA1515 without a source location. Type-level suppressions cannot
attach to those diagnostics. `AnalyzerCompatibility.globalconfig`, included by
`Directory.Build.props`, disables the fallback severity: the existing
`.editorconfig` source-file CA1515 warning takes precedence, so ordinary public
types still fail the build. Isolated compiler probes verified both cases.
This approved workaround suppresses all unlocated CA1515 diagnostics, not just
extension-block false positives. Recheck it after SDK updates and remove it once
extension-block analysis is fixed. See Microsoft's
[analyzer configuration precedence](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/configuration-files#precedence).

The public service-defaults `Extensions` container has a justified CA1034
type-level suppression because C# 14 extension blocks generate public nested
metadata types. No handwritten nested types were added. Keep the public extension
APIs available to their existing consumers.

Members that do not use instance state should be static where reported by
`S2325` (warning). Private helpers stay private, including in Razor code-behind.
Lifecycle overrides and methods using component state or injected services remain
instance methods. Preserve required framework or reflection contracts and document
any necessary exception on the affected member.

Unused return values are checked where reported by `S3241` (warning). Preserve
meaningful fluent APIs by using their results; helpers with unnecessary return
values can return `void`. `ConfigureOpenTelemetry` forwards the builder returned
by `AddOpenTelemetryExporters`. Review callers before removing a return contract,
including framework-required and awaitable return types in Razor code-behind.

LINQ loop simplifications are review guidance (`S3267`, suggestion). Prefer LINQ
when filtering or projection makes the intent clearer; readable explicit loops
remain acceptable. This also applies to handwritten Razor code-behind helpers.
Review rendering markup separately because generated-code analyzer coverage varies.

Hardcoded paths and URIs are checked (`S1075`, warning). Put deployment-specific
locations in configuration. Fixed protocol or parser syntax can use a narrow,
documented exception: the authenticator's `otpauth` URI constant has a field-level
`SuppressMessage`, and the validator's Razor virtual-path construction has a
single-statement pragma. The surrounding methods and Razor code-behind remain
checked; preserve the authenticator URI format and Razor's virtual-path convention.

Prefer awaited asynchronous alternatives where reported by `S6966` (warning),
including host and build-tool entry points. Razor code-behind should use awaitable
event handlers and asynchronous lifecycle overrides when awaiting I/O, preserving
framework contracts. Ordinary `await` remains appropriate for component code;
this rule does not introduce a `ConfigureAwait` requirement.

Unnecessary default field initialization is checked (`CA1805`, warning). Omit
redundant initializers such as `= 0` for an `int` field, including in Razor
code-behind. Keep meaningful initial values and initialization that depends on
component inputs in the appropriate lifecycle method. Do not remove nullable
initialization suppressions mechanically.

Cross-language keyword naming checks are disabled (`CA1716`). This C# solution
does not publish libraries for consumption from other .NET languages. Razor
components can retain clear C# names such as `Error`, even when those names are
keywords in another language.

Use known concrete types where reported by `CA1859` (warning) when an abstraction
serves no purpose. `IdentityNoOpEmailSender` owns a fixed `NoOpEmailSender`, so its
private field uses that concrete type. Preserve interfaces at dependency injection
and component contract boundaries where they provide an actual abstraction; use a
narrow, justified exception if necessary. The same convention applies to private
implementation details in handwritten Razor code-behind.

Cache reusable composite format strings where reported by `CA1863` (warning).
Use a `private static readonly CompositeFormat` parsed once for a fixed template,
including in Razor code-behind. The authenticator URI template is shared, while
each user's email, secret, and resulting URI remain per-call values. Preserve
the format string, argument encoding, and culture provider when converting it.

Use source-generated logging (`CA1848`, warning). Declare `[LoggerMessage]` on
typed partial methods and let the compiler generate their implementations. Keep
component-specific helpers `private static partial` in `.razor.cs`, passing the
existing injected logger explicitly so its category is preserved. Keep message
templates, levels, placeholder names, and argument values intact when migrating.
Razor markup and logger injection do not need to change.

The migrated account events have explicit IDs 1001–1018 and use their helper
method names as event names. The template previously logged every event with
ID 0 and no event name. Keep these assigned IDs stable when renaming or moving
helpers, and choose an unused ID for a new account event.

Avoid unnecessary work preparing log arguments (`CA1873`, warning), including in
Razor code-behind. Generated logging methods check the level internally, but C#
evaluates their arguments before entering the method. Guard expensive log-only
computations with `logger.IsEnabled` at the matching level. Ordinary calls with
existing values do not need an extra guard. Keep operations needed for application
behavior outside logging guards so changing the log level cannot skip them.

Validate reference arguments before dereferencing them in externally visible
methods (`CA1062`, warning). Keep the default public/protected API scope, including
extension-method receivers. `MapDefaultEndpoints` uses
`ArgumentNullException.ThrowIfNull(app)` to report an invalid caller argument
immediately. Non-nullable annotations do not insert runtime checks. This rule also
applies to eligible methods in handwritten Razor code-behind; it does not validate
Blazor `[Parameter]` properties or require guards in every private helper.

Specify `StringComparison` where a matching overload exists (`CA1307`, warning),
even when the overload's default behavior is suitable. The authentication-code
cleanup in `AuthenticationCodeExtensions` explicitly uses `StringComparison.Ordinal`,
preserving the existing literal removal of spaces and hyphens. Choose comparison
behavior for each use case; this rule does not require ordinal matching everywhere.

Lowercase string normalization is checked (`CA1308`, warning). Prefer an explicit
`StringComparison` or `StringComparer` for comparisons instead of changing case.
Intentional display formatting can use a method-level `SuppressMessage` with a
specific justification. `AuthenticatorKeyExtensions.FormatAuthenticatorKey`
preserves the existing lowercase grouping for display; the stored key and QR-code
URI use the original value. The exception applies only to this method, not the
other account helpers.

Type/namespace naming overlaps are review guidance (`CA1724`, suggestion). Keep
the current `Home` and `Counter` component names and the service-defaults
`Extensions` class. Review overlaps that make usage confusing; a match alone does
not require a rename. Component renames must keep `.razor` and `.razor.cs` files,
partial class names, and references aligned while preserving intended routes.

Uninstantiated internal-class checks are disabled (`CA1812`). Dependency injection,
reflection, and EF create application types without constructor calls the analyzer
can reliably observe. `ApplicationDbContext`, the Identity services,
`ApplicationUser`, and the EF migration are used through these mechanisms. Keep
their narrow visibility and framework registrations; review actual usages before
removing an apparently unused class. Public Razor components have no current
findings from this rule, but their injected internal services do.

Avoid array-valued public properties (`CA1819`, warning). Display-only collection
parameters such as `ShowRecoveryCodes.RecoveryCodes` use `IReadOnlyList<T>` while
retaining public get/set accessors for Blazor binding. This expresses read-only
access through the parameter, not immutable underlying data. Private storage may
still use arrays. The two recovery-code parent components materialize Identity's
result once in code-behind, preserving a null result, and pass the snapshot
directly instead of allocating a new array during each render.

Writable mutable collection properties are checked (`CA2227`, warning). Prefer
read-only collection contracts when consumers only need to read the contents.
`PasskeySubmit.AdditionalAttributes` uses `IReadOnlyDictionary<string, object>`;
Blazor supports this type for `CaptureUnmatchedValues` and attribute splatting.
Keep its public get/set accessors so Blazor can supply each parameter value. Do
not mechanically remove a component parameter's setter or change it to `init` to
satisfy this rule. A read-only interface does not freeze the underlying collection.

Insecure randomness is checked (`CA5394`, warning). The rule flags `System.Random`
usage, including `Random.Shared`, in ordinary C# and Razor code-behind. Use
framework security APIs or `RandomNumberGenerator` when values must be
unpredictable. A future non-security use, such as a simulation, can use a narrow,
justified exception after review. No exception is currently needed.

Handwritten `.razor.cs` files receive the C# rules. Coverage of generated Razor C#
depends on the analyzer and rule; do not edit generated output to satisfy a rule.
The separate policy below enforces component code-behind structure.

## Required component parameters

Use `[EditorRequired]` alongside `[Parameter]` for inputs a parent must supply for
a component to serve its purpose. Razor reports omitted required inputs as
`RZ2012`; the shared `TreatWarningsAsErrors` setting makes these findings fail
builds, including attributes declared in `.razor.cs` code-behind. Keep parameters
as ordinary auto-properties; do not use C# `required` or `init` for Blazor inputs.

`ShowRecoveryCodes.RecoveryCodes` is required because displaying recovery codes is
the component's purpose. Its optional `StatusMessage` remains optional.
`PasskeySubmit` requires `Operation`, `Name`, and `ChildContent`: callers must
select the operation, name the form value, and provide the button's content.
`EmailName` remains optional because passkey creation does not need it, and
`AdditionalAttributes` remains optional. `StatusMessage.Message` is optional
because the component can consume the redirect status cookie instead.

Do not apply the attribute to cascading, query, or form-supplied properties.
Assess routed parameters separately; `RenamePasskey.Id` is supplied by its required
`{Id}` route segment. `[EditorRequired]` checks Razor call sites, not runtime values:
it does not guarantee non-null/non-empty values, validate dynamically supplied
parameters, or replace appropriate runtime validation. An empty child fragment
also does not guarantee a useful button label.

See the [Blazor component parameter documentation](https://learn.microsoft.com/aspnet/core/blazor/components/?view=aspnetcore-10.0#component-parameters)
for the distinction between required call-site inputs and runtime validation.

## Razor code-behind policy

Every Blazor page and component must have a sibling `Component.razor.cs` file,
including components containing only markup. `_Imports.razor` is exempt.

The companion must be compiled and declare the matching top-level partial class,
with the component's namespace and number of generic type parameters. A placeholder
file or a class in the wrong namespace does not satisfy the policy.

Declare fields, properties, parameters, lifecycle methods, event handlers, and other
members in code-behind. Inline `@code` and `@functions` blocks are forbidden, even
when empty. Rendering expressions and control flow such as `@if`, `@foreach`, and
local variables used for rendering remain allowed. Razor directives such as
`@page`, `@inject`, `@inherits`, `@implements`, and `@attribute` remain supported.

`Directory.Build.targets` imports the policy for Razor projects. The build-only
`YHAB.Build` project references the Razor and C# parsers shipped with the selected
.NET SDK; no runtime application dependency or extra NuGet package is added.
The validation target runs before compilation, including incremental builds. It
skips IDE design-time builds and reports MSBuild errors with source file locations:

| Code | Violation |
| --- | --- |
| RUV001 | Missing code-behind file |
| RUV002 | Inline `@code` or `@functions` member block |
| RUV003 | Missing matching partial class declaration |
| RUV004 | Code-behind file excluded from compilation |

These are build errors rather than compiler warnings: `TreatWarningsAsErrors`
does not promote arbitrary MSBuild task warnings. Do not suppress the checks or
create an unrelated class to bypass the policy. An empty matching partial class
is appropriate for a component that only contains markup.

The validator runs in a separate process so reusable MSBuild nodes do not lock its
assemblies while agents edit the validator. The Razor parser handles comments,
strings, inherited namespaces, and directives;
validation does not use regular expressions to recognize member blocks. The C#
parser honors the project's conditional compilation symbols. Because the validator
uses SDK compiler APIs, run the policy checks when changing the selected SDK:

```powershell
pwsh ./scripts/Test-RazorCodeBehind.ps1
dotnet build YHAB.slnx
```

The check script creates isolated fixtures under the ignored `artifacts/` directory.
