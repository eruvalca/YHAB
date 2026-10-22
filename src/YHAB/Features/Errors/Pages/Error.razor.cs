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

    // The error handler re-executes the original request, so the browser keeps
    // its failing URL and this path lets the retry action target it again.
    private string RetryUrl =>
        HttpContext?.Features.Get<IExceptionHandlerPathFeature>()?.Path is { Length: > 0 } path ? path : "/";

    protected override void OnInitialized() =>
        RequestId = Activity.Current?.Id ?? HttpContext?.TraceIdentifier;
}
