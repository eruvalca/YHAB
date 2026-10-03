using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Components;
using YHAB.Features.Account.Models;

namespace YHAB.Features.Account.Components;

public sealed partial class PasskeySubmit
{
    private AntiforgeryTokenSet? _tokens;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    [Parameter]
    [EditorRequired]
    public PasskeyOperation Operation { get; set; }

    [Parameter]
    [EditorRequired]
    public string Name { get; set; } = default!;

    [Parameter]
    public string? EmailName { get; set; }

    [Parameter]
    [EditorRequired]
    public RenderFragment? ChildContent { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    protected override void OnInitialized()
    {
        _tokens = Services.GetService<IAntiforgery>()?.GetTokens(HttpContext);
    }
}
