using Microsoft.AspNetCore.Components;

namespace YHAB.Components;

public sealed partial class App
{
    [CascadingParameter] private HttpContext HttpContext { get; set; } = default!;
}
