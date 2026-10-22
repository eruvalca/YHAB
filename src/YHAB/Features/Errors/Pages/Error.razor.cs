using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;

namespace YHAB.Features.Errors.Pages;

public sealed partial class Error
{
    [CascadingParameter]
    private HttpContext? HttpContext { get; set; }

    private string? RequestId { get; set; }
    private bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    // The error handler re-executes the original request with its path, path base,
    // and query string preserved, so the retry action can target the failing URL.
    private string RetryUrl
    {
        get
        {
            if (HttpContext is null)
            {
                return "/";
            }

            var originalPath = HttpContext.Features.Get<IExceptionHandlerPathFeature>()?.Path;
            return string.IsNullOrEmpty(originalPath)
                ? "/"
                : $"{HttpContext.Request.PathBase}{originalPath}{HttpContext.Request.QueryString}";
        }
    }

    protected override void OnInitialized() =>
        RequestId = Activity.Current?.Id ?? HttpContext?.TraceIdentifier;
}
