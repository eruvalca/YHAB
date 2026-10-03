using Microsoft.AspNetCore.Identity;

namespace YHAB.Features.Account.Extensions;

internal static class IdentityErrorExtensions
{
    extension(IEnumerable<IdentityError> errors)
    {
        internal string FormatDescriptions(string separator) => string.Join(separator, errors.Select(error => error.Description));
    }
}
