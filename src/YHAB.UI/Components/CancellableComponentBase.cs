using Microsoft.AspNetCore.Components;

namespace YHAB.UI.Components;

/// <summary>Owns cancellation for component work without depending on a server HTTP context.</summary>
public abstract class CancellableComponentBase : ComponentBase, IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _disposal;

    // The static App supplies this cascade. Cascades do not cross an interactive renderer boundary.
    [CascadingParameter(Name = nameof(RequestAborted))]
    public CancellationToken RequestAborted { get; set; }

    protected bool IsDisposed { get; private set; }
    protected CancellationToken LifetimeToken => IsDisposed ? new CancellationToken(canceled: true) : _lifetime.Token;

    // Each asynchronous operation owns its linked source until its work has finished.
    protected CancellationTokenSource CreateOperation()
        => CancellationTokenSource.CreateLinkedTokenSource(LifetimeToken, RequestAborted);

    public async ValueTask DisposeAsync()
    {
        _disposal ??= DisposeComponentAsync();
        await _disposal;
        GC.SuppressFinalize(this);
    }

    protected virtual ValueTask DisposeCoreAsync() => ValueTask.CompletedTask;

    private async Task DisposeComponentAsync()
    {
        IsDisposed = true;
        try
        {
            try { await _lifetime.CancelAsync(); }
            finally { await DisposeCoreAsync(); }
        }
        finally
        {
            _lifetime.Dispose();
        }
    }
}
