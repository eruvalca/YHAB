using System.Net.Http.Json;
using System.Text.Json;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Client.Services;

internal sealed class HttpBudgetClient(HttpClient http) : IBudgetClient
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private string? _token;

    public async Task<IReadOnlyList<PlanSummary>> ListAsync(CancellationToken cancellationToken = default)
        => await ReadAsync<IReadOnlyList<PlanSummary>>("api/plans", cancellationToken);

    public async Task<Guid> CreateAsync(CreatePlanRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/plans", JsonContent.Create(request), cancellationToken);
        return await response.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    public async Task<PlanSnapshot> ReadAsync(Guid planId, CancellationToken cancellationToken = default)
        => await ReadAsync<PlanSnapshot>($"api/plans/{planId}", cancellationToken);

    public async Task ExecuteAsync(Guid planId, PlanCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (method, route) = Route(command);
        // Endpoint contracts are concrete commands; the discriminator is needed only for shared command serialization.
        using var content = new StringContent(JsonSerializer.Serialize(command, command.GetType(), _json), System.Text.Encoding.UTF8, "application/json");
        using var response = await SendAsync(method, $"api/plans/{planId}/{route}", content, cancellationToken);
    }

    private static (HttpMethod Method, string Route) Route(PlanCommand command) => command switch
    {
        SaveAccount change => ResourceRoute("accounts", change.Account.Id),
        SaveGroup change => ResourceRoute("groups", change.Group.Id),
        SaveCategory change => ResourceRoute("categories", change.Category.Id),
        SaveTransaction change => ResourceRoute("transactions", change.Transaction.Id),
        DeleteTransactions => (HttpMethod.Post, "transactions/delete"),
        UpdateTransactionStates => (HttpMethod.Patch, "transactions/status"),
        AssignMoney => (HttpMethod.Put, "assignments"),
        MoveMoney => (HttpMethod.Post, "money-moves"),
        AutoAssign => (HttpMethod.Post, "auto-assign"),
        ReconcileAccount => (HttpMethod.Post, "reconciliations"),
        UpdatePlan => (HttpMethod.Put, "settings"),
        RenamePayee => (HttpMethod.Post, "payees/rename"),
        RemoveCategory => (HttpMethod.Post, "categories/merge"),
        UndoChange => (HttpMethod.Post, "undo"),
        RedoChange => (HttpMethod.Post, "redo"),
        PostRecurring => (HttpMethod.Post, "recurring/post-due"),
        _ => throw new ArgumentException("Unknown budgeting command.", nameof(command)),
    };

    private static (HttpMethod Method, string Route) ResourceRoute(string collection, Guid id)
        => id == Guid.Empty ? (HttpMethod.Post, collection) : (HttpMethod.Put, $"{collection}/{id}");

    private async Task<T> ReadAsync<T>(string route, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(new Uri(route, UriKind.Relative), cancellationToken);
        await CheckAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new HttpRequestException("The server returned an empty response.");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, HttpContent content, CancellationToken cancellationToken)
    {
        _token ??= (await ReadAsync<RequestToken>("api/plans/token", cancellationToken)).Token;
        using var request = new HttpRequestMessage(method, route) { Content = content };
        request.Headers.Add("X-CSRF-TOKEN", _token);
        var response = await http.SendAsync(request, cancellationToken);
        try
        {
            await CheckAsync(response, cancellationToken);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static async Task CheckAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            throw new BudgetRequestException((int)response.StatusCode, "Your session expired. Please sign in again.");
        }
        if (response.Content.Headers.ContentType?.MediaType?.EndsWith("json", StringComparison.OrdinalIgnoreCase) != true)
        {
            throw new HttpRequestException($"The server could not complete the request ({(int)response.StatusCode}). Refresh and try again.");
        }
        var error = await response.Content.ReadFromJsonAsync<BudgetError>(cancellationToken);
        throw new BudgetRequestException((int)response.StatusCode, error?.Detail ?? error?.Title ?? "Unable to complete the request. Please refresh and try again.");
    }
}
