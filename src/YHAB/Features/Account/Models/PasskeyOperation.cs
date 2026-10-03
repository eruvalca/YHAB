using System.Diagnostics.CodeAnalysis;

namespace YHAB.Features.Account.Models;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "PasskeySubmit.Operation exposes this enum as a public Blazor component parameter.")]
public enum PasskeyOperation
{
    Create = 0,
    Request = 1,
}
