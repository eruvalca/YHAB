using System.Net.Http.Json;
using System.Text.Json;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Client.Services;

internal sealed class HttpBudgetClient(HttpClient http) : IBudgetClient
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private string? _token;

    public async Task<IReadOnlyList<PlanSummary>> ListAsync(CancellationToken cancellationToken)
        => await ReadAsync<IReadOnlyList<PlanSummary>>("api/plans", cancellationToken);

    public async Task<Guid> CreateAsync(CreatePlanRequest request, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/plans", JsonContent.Create(request), cancellationToken);
        return await response.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    public async Task<PlanSnapshot> ReadAsync(Guid planId, CancellationToken cancellationToken)
        => await ReadAsync<PlanSnapshot>($"api/plans/{planId}", cancellationToken);

    public Task<PlanView> ReadViewAsync(Guid planId, CancellationToken cancellationToken)
        => ReadAsync<PlanView>($"api/plans/{planId}/view", cancellationToken);

    public Task<PlanMonthView> ReadWorkspaceAsync(Guid planId, DateOnly? month, CancellationToken cancellationToken)
        => ReadAsync<PlanMonthView>(month is { } date ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"api/plans/{planId}/workspace?month={date:yyyy-MM-dd}") : $"api/plans/{planId}/workspace", cancellationToken);

    public Task<long> ReadRevisionAsync(Guid planId, CancellationToken cancellationToken)
        => ReadAsync<long>($"api/plans/{planId}/revision", cancellationToken);

    public Task<BudgetMonth> ReadMonthAsync(Guid planId, DateOnly month, long version, CancellationToken cancellationToken)
        => ReadAsync<BudgetMonth>(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"api/plans/{planId}/months/{month:yyyy-MM-dd}?version={version}"), cancellationToken);

    public Task<RegisterPage> ReadRegisterAsync(Guid planId, RegisterQuery query, CancellationToken cancellationToken)
        => ReadAsync<RegisterPage>($"api/plans/{planId}/register?query={Uri.EscapeDataString(JsonSerializer.Serialize(query, _json))}", cancellationToken);

    public Task<ReportView> ReadReportsAsync(Guid planId, DateOnly from, DateOnly through, CancellationToken cancellationToken)
        => ReadAsync<ReportView>(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"api/plans/{planId}/reports?from={from:yyyy-MM-dd}&through={through:yyyy-MM-dd}"), cancellationToken);

    public Task<IReadOnlyList<string>> ReadPayeesAsync(Guid planId, CancellationToken cancellationToken)
        => ReadAsync<IReadOnlyList<string>>($"api/plans/{planId}/payees", cancellationToken);

    public async Task ExecuteAsync(Guid planId, PlanCommand command, CancellationToken cancellationToken)
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
        ReorderGroup => (HttpMethod.Post, "groups/reorder"),
        ReorderCategory => (HttpMethod.Post, "categories/reorder"),
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
        cancellationToken.ThrowIfCancellationRequested();
        using var response = await http.GetAsync(new Uri(route, UriKind.Relative), cancellationToken);
        await CheckAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new HttpRequestException("The server returned an empty response.");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, HttpContent content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, route) { Content = content };
        cancellationToken.ThrowIfCancellationRequested();
        _token ??= (await ReadAsync<RequestToken>("api/plans/token", cancellationToken)).Token;
        cancellationToken.ThrowIfCancellationRequested();
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
