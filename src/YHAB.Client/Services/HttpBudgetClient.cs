using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Client.Services;

internal sealed class HttpBudgetClient(HttpClient http) : IBudgetClient
{
    private string? _token;

    public async Task<IReadOnlyList<PlanSummary>> ListAsync(CancellationToken cancellationToken)
        => await ReadAsync("api/plans", BudgetJsonContext.Default.PlanSummaryArray, cancellationToken);

    public async Task<Guid> CreateAsync(CreatePlanRequest request, CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(request, BudgetJsonContext.Default.CreatePlanRequest);
        using var response = await SendAsync(HttpMethod.Post, "api/plans", content, cancellationToken);
        return await ReadContentAsync(response.Content, BudgetJsonContext.Default.Guid, cancellationToken);
    }

    public async Task<PlanSnapshot> ReadAsync(Guid planId, CancellationToken cancellationToken)
        => await ReadAsync($"api/plans/{planId}", BudgetJsonContext.Default.PlanSnapshot, cancellationToken);

    public Task<PlanView> ReadViewAsync(Guid planId, CancellationToken cancellationToken)
        => ReadAsync($"api/plans/{planId}/view", BudgetJsonContext.Default.PlanView, cancellationToken);

    public Task<PlanMonthView> ReadWorkspaceAsync(Guid planId, DateOnly? month, CancellationToken cancellationToken)
        => ReadAsync(month is { } date ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"api/plans/{planId}/workspace?month={date:yyyy-MM-dd}") : $"api/plans/{planId}/workspace", BudgetJsonContext.Default.PlanMonthView, cancellationToken);

    public Task<long> ReadRevisionAsync(Guid planId, CancellationToken cancellationToken)
        => ReadAsync($"api/plans/{planId}/revision", BudgetJsonContext.Default.Int64, cancellationToken);

    public Task<BudgetMonth> ReadMonthAsync(Guid planId, DateOnly month, long version, CancellationToken cancellationToken)
        => ReadAsync(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"api/plans/{planId}/months/{month:yyyy-MM-dd}?version={version}"), BudgetJsonContext.Default.BudgetMonth, cancellationToken);

    public Task<RegisterPage> ReadRegisterAsync(Guid planId, RegisterQuery query, CancellationToken cancellationToken)
        => ReadAsync($"api/plans/{planId}/register?query={Uri.EscapeDataString(JsonSerializer.Serialize(query, BudgetJsonContext.Default.RegisterQuery))}", BudgetJsonContext.Default.RegisterPage, cancellationToken);

    public Task<ReportView> ReadReportsAsync(Guid planId, DateOnly from, DateOnly through, CancellationToken cancellationToken)
        => ReadAsync(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"api/plans/{planId}/reports?from={from:yyyy-MM-dd}&through={through:yyyy-MM-dd}"), BudgetJsonContext.Default.ReportView, cancellationToken);

    public async Task<IReadOnlyList<string>> ReadPayeesAsync(Guid planId, CancellationToken cancellationToken)
        => await ReadAsync($"api/plans/{planId}/payees", BudgetJsonContext.Default.StringArray, cancellationToken);

    public Task<PlanShareSummary> ReadSharingAsync(Guid planId, CancellationToken cancellationToken)
        => ReadAsync($"api/plans/{planId}/sharing", BudgetJsonContext.Default.PlanShareSummary, cancellationToken);

    public async Task<PlanShareCode> CreateShareCodeAsync(Guid planId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, $"api/plans/{planId}/sharing/codes", null, cancellationToken);
        return await ReadContentAsync(response.Content, BudgetJsonContext.Default.PlanShareCode, cancellationToken);
    }

    public async Task<Guid> JoinAsync(string code, CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(new JoinPlanRequest(code), BudgetJsonContext.Default.JoinPlanRequest);
        using var response = await SendAsync(HttpMethod.Post, "api/plans/join", content, cancellationToken);
        return await ReadContentAsync(response.Content, BudgetJsonContext.Default.Guid, cancellationToken);
    }

    public async Task<PlanShareSummary> UnshareAsync(Guid planId, string memberId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"api/plans/{planId}/sharing/{Uri.EscapeDataString(memberId)}", null, cancellationToken);
        return await ReadSummaryAsync(response, cancellationToken);
    }

    public async Task<PlanShareSummary> TransferOwnershipAsync(Guid planId, string memberId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, $"api/plans/{planId}/sharing/{Uri.EscapeDataString(memberId)}/owner", null, cancellationToken);
        return await ReadSummaryAsync(response, cancellationToken);
    }

    public async Task LeaveAsync(Guid planId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, $"api/plans/{planId}/leave", null, cancellationToken);
    }

    public async Task DeleteAsync(Guid planId, string planName, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"api/plans/{planId}?confirm={Uri.EscapeDataString(planName)}", null, cancellationToken);
    }

    private static Task<PlanShareSummary> ReadSummaryAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        => ReadContentAsync(response.Content, BudgetJsonContext.Default.PlanShareSummary, cancellationToken);

    public async Task ExecuteAsync(Guid planId, PlanCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (method, route) = Route(command);
        // Endpoint contracts are concrete commands; the discriminator is needed only for shared command serialization.
        using var content = new StringContent(JsonSerializer.Serialize(command, CommandTypeInfo(command)), System.Text.Encoding.UTF8, "application/json");
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
        AssignMoney => (HttpMethod.Put, "assignments"),
        MoveMoney => (HttpMethod.Post, "money-moves"),
        AutoAssign => (HttpMethod.Post, "auto-assign"),
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

    // Endpoint contracts are concrete commands, so serialize with the derived type's metadata
    // rather than the polymorphic base. Source-generated info keeps this trim- and AOT-safe.
    private static JsonTypeInfo CommandTypeInfo(PlanCommand command) => command switch
    {
        SaveAccount => BudgetJsonContext.Default.SaveAccount,
        SaveGroup => BudgetJsonContext.Default.SaveGroup,
        SaveCategory => BudgetJsonContext.Default.SaveCategory,
        ReorderGroup => BudgetJsonContext.Default.ReorderGroup,
        ReorderCategory => BudgetJsonContext.Default.ReorderCategory,
        SaveTransaction => BudgetJsonContext.Default.SaveTransaction,
        DeleteTransactions => BudgetJsonContext.Default.DeleteTransactions,
        AssignMoney => BudgetJsonContext.Default.AssignMoney,
        MoveMoney => BudgetJsonContext.Default.MoveMoney,
        AutoAssign => BudgetJsonContext.Default.AutoAssign,
        UpdatePlan => BudgetJsonContext.Default.UpdatePlan,
        RenamePayee => BudgetJsonContext.Default.RenamePayee,
        RemoveCategory => BudgetJsonContext.Default.RemoveCategory,
        UndoChange => BudgetJsonContext.Default.UndoChange,
        RedoChange => BudgetJsonContext.Default.RedoChange,
        PostRecurring => BudgetJsonContext.Default.PostRecurring,
        _ => throw new ArgumentException("Unknown budgeting command.", nameof(command)),
    };

    private async Task<T> ReadAsync<T>(string route, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var response = await http.GetAsync(new Uri(route, UriKind.Relative), cancellationToken);
        await CheckAsync(response, cancellationToken);
        return await ReadContentAsync(response.Content, typeInfo, cancellationToken);
    }

    private static async Task<T> ReadContentAsync<T>(HttpContent content, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
        => await content.ReadFromJsonAsync(typeInfo, cancellationToken)
            ?? throw new HttpRequestException("The server returned an empty response.");

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, route) { Content = content };
        cancellationToken.ThrowIfCancellationRequested();
        _token ??= (await ReadAsync("api/plans/token", BudgetJsonContext.Default.RequestToken, cancellationToken)).Token;
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
        var error = await response.Content.ReadFromJsonAsync(BudgetJsonContext.Default.BudgetError, cancellationToken);
        throw new BudgetRequestException((int)response.StatusCode, error?.Detail ?? error?.Title ?? "Unable to complete the request. Please refresh and try again.");
    }
}
