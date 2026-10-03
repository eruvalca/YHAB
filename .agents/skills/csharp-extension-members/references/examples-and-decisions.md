# Examples and YHAB decisions

## Standalone C# 14 fixture

This original example demonstrates instance methods, properties, method type
parameters, nullable and by-reference receivers, and static members/operators.
The vector conveniences illustrate syntax; they are not proposed application APIs.

Compile this block as `Program.cs` in a .NET 10 console project with
`LangVersion` set to `14.0` and nullable analysis enabled. It uses only framework
references. A successful run prints `C# 14 extension examples passed.`

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace ExtensionMemberExamples;

internal static class ExampleExtensions
{
    extension<T>(IReadOnlyList<T> values)
    {
        internal bool HasValues => values.Count != 0;

        internal TResult[] MapToArray<TResult>(Func<T, TResult> map) => values.Select(map).ToArray();
    }

    extension([NotNullWhen(true)] string? text)
    {
        internal bool HasText => !string.IsNullOrWhiteSpace(text);
    }

    extension(ref TimeSpan duration)
    {
        internal void AdvanceBy(TimeSpan delta) => duration += delta;
    }

    extension(Vector2)
    {
        internal static Vector2 FromOffset((float X, float Y) offset) => new(offset.X, offset.Y);

        internal static Vector2 DiagonalUnit => new(1, 1);

        public static Vector2 operator +(Vector2 vector, (float X, float Y) offset) =>
            vector + new Vector2(offset.X, offset.Y);
    }
}

internal static class Program
{
    private static void Main()
    {
        IReadOnlyList<int> values = [1, 2];
        var mapped = values.MapToArray(value => value.ToString(CultureInfo.InvariantCulture));
        // Static invocation keeps the block's T before the method's TResult.
        var explicitlyMapped = ExampleExtensions.MapToArray<int, string>(
            values, value => value.ToString(CultureInfo.InvariantCulture));
        string? text = null;
        var duration = TimeSpan.Zero;
        duration.AdvanceBy(TimeSpan.FromSeconds(1));
        var vector = Vector2.FromOffset((2, 3)) + (4f, 5f);

        if (!values.HasValues || !mapped.SequenceEqual(new[] { "1", "2" })
            || !explicitlyMapped.SequenceEqual(mapped) || text.HasText
            || duration != TimeSpan.FromSeconds(1) || vector != new Vector2(6, 8)
            || Vector2.DiagonalUnit != Vector2.One)
        {
            throw new InvalidOperationException("Extension behavior changed.");
        }

        Console.WriteLine("C# 14 extension examples passed.");
    }
}
```

The operator must be public even though its containing implementation class is
internal. Prefer internal visibility for ordinary members used only locally.
Changing a method into a property changes its API; compatible syntax migration
alone does not authorize that change.

## Decisions in this repository

| Concern | Chosen shape and invariant |
| --- | --- |
| Service defaults | Keep the public `YHAB.ServiceDefaults.Extensions` container. Four builder methods share the constrained `TBuilder` block; the `WebApplication` endpoint mapper has its own block. Fluent and static calls retain their signatures. |
| Identity endpoints | Keep the internal partial container and mapping return type. Generated logging remains a normal partial method outside the block. |
| Sign-in classification | `SignInResult.ToSignInOutcome()` owns the conversion; success precedes two-factor, lockout, not-allowed, then failure. The service still owns awaited Identity operations. |
| Error descriptions | `IEnumerable<IdentityError>.FormatDescriptions(separator)` requires the original separator at each call site. Preserve order, duplicates, prefixes, and null handling. |
| Tokens | `string.EncodeIdentityToken()` performs UTF-8 followed by Base64Url encoding. Callers still compose and HTML-encode links. Decoding stays on `TokenDecodeOutcome`. |
| Authentication codes | `NormalizeAuthenticatorCode()` removes literal spaces and hyphens; `NormalizeRecoveryCode()` removes literal spaces only. Other whitespace is significant. |
| Authenticator keys | `FormatAuthenticatorKey()` groups the display value in fours and lowercases it. Its narrow CA1308 exception describes display-only use; provisioning uses the original key. |
| Request facts | `HttpRequest.IsGet` delegates to `HttpMethods.IsGet`. Keep it in the Account namespace rather than creating a global HTTP convenience API. |
| bUnit setup | Extensions on `BunitContext` configure fresh per-test account state and log capture. `AccountTestContext` receives that state through its primary constructor. |
| Form helpers | Generic extensions on `TComponent : IComponent` preserve reflection against the compile-time component type, without widening production property accessibility. |
| Log inspection | `ILogger.GetLoggedEventIds()` remains a method with the existing deferred filtering/projection. It is not a property or a cached snapshot. |

## SDK analyzer compatibility

The selected SDK's CA1034 analyzer flags the public extension blocks in service
defaults. The existing public container has a justified type-level suppression
for the generated nested metadata types. Its CA1515 analyzer also reports
extension blocks inside internal containers without a source location.

The approved global fallback suppresses unlocated CA1515 diagnostics, while
`.editorconfig` still enforces the rule on ordinary C# source types. Isolated
compiler probes verify both the extension-block success case and an ordinary
public type's CA1515 failure. This fallback affects every unlocated CA1515
diagnostic, not just this bug. See the [build conventions](../../../../build/README.md)
for its removal condition. Retest these exceptions when updating the SDK;
do not add file-wide or project-wide `NoWarn` entries for new extensions.

## Intentional non-candidates

- `TokenDecodeOutcome.Decode`, `CredentialIdOutcome.Decode`, and
  `PasskeySubmission.From` are factories on types owned by the feature.
- Account services and redirect management coordinate dependencies and side
  effects. Component lifecycle methods, parameters, and state stay on components.
- `[LoggerMessage]` partial methods rely on generation; C# 14 does not support
  partial members inside extension blocks.
- The build validator's namespace helper has one local caller. Extraction would
  not create a useful reusable API.
- No current application operation needs a static extension or extension operator.
  Generated source and migrations are not migration targets.

When revisiting these decisions, use a concrete new caller or capability as the
reason for change. Do not move code simply because extension syntax is available.
