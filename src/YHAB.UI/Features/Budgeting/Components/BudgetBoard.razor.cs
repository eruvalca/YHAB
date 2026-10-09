using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class BudgetBoard
{
    [Parameter, EditorRequired] public PlanSnapshot Plan { get; set; } = default!;
    [Parameter, EditorRequired] public EventCallback<PlanCommand> OnCommand { get; set; }
    [Parameter] public bool Busy { get; set; }
    [Parameter] public Func<DateOnly, CancellationToken, Task<BudgetMonth>>? LoadMonth { get; set; }

    private string? _error;
    private int _request;
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Borrowed reference: CalculateAsync owns and disposes the source after its work completes; component disposal cancels its linked lifetime.")]
    private CancellationTokenSource? _loadCancellation;
    private bool IsBusy { get => Busy || field || _error is not null; set; }
    private DateOnly _month;
    private BudgetMonth _budget = default!;
    private long _version = -1;
    private PlanSnapshot? _loadedPlan;
    private bool _move;
    private CategoryData? _category;
    private CategoryMonth? EditingMonth => _budget.Categories.SingleOrDefault(item => item.Category.Id == _category?.Id);

    protected override async Task OnParametersSetAsync()
    {
        if (_month == default) { _month = BudgetFacts.Month(Plan.Today); }
        if (_loadedPlan?.Id != Plan.Id || _version != Plan.Version || _budget is null || (_error is not null && !ReferenceEquals(_loadedPlan, Plan)))
        {
            if (_loadedPlan?.Id != Plan.Id) { CloseEditors(); }
            else { _move = false; _category = Plan.Categories.SingleOrDefault(item => item.Id == _category?.Id); }
            _version = Plan.Version;
            _loadedPlan = Plan;
            await CalculateAsync();
        }
    }
    private async Task CalculateAsync()
    {
        using var operation = CreateOperation();
        var token = operation.Token;
        var previous = _loadCancellation;
        _loadCancellation = operation;
        var request = ++_request;
        IsBusy = true;
        _error = null;
        try
        {
            if (previous is not null) { await previous.CancelAsync(); }
            token.ThrowIfCancellationRequested();
            var result = LoadMonth is null ? BudgetCalculator.Calculate(Plan, _month, Plan.Today) : await LoadMonth(_month, token);
            token.ThrowIfCancellationRequested();
            if (request == _request) { _budget = result; }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { /* Disposed or superseded read. */ }
        catch (OperationCanceledException) { if (request == _request) { _error = "Loading this month timed out. Refresh your plan to try again."; } }
        catch (BudgetRequestException exception) { if (!token.IsCancellationRequested && request == _request) { _error = exception.Message; } }
        catch (HttpRequestException) { if (!token.IsCancellationRequested && request == _request) { _error = "Unable to load this month. Refresh your plan to try again."; } }
        finally
        {
            if (ReferenceEquals(_loadCancellation, operation)) { _loadCancellation = null; }
            if (!IsDisposed && request == _request) { IsBusy = false; }
        }
    }
    private async Task PreviousAsync() { _month = _month.AddMonths(-1); await CalculateAsync(); }
    private async Task NextAsync() { _month = _month.AddMonths(1); await CalculateAsync(); }
    private async Task CurrentAsync() { _month = BudgetFacts.Month(Plan.Today); await CalculateAsync(); }
    private Task AutoAssignAsync() => OnCommand.InvokeAsync(new AutoAssign(Plan.Version, _month));
    private void ShowMove() { CloseEditors(); _move = true; }
    private void EditCategory(CategoryData category) { _move = false; _category = category; }
    private void CloseEditors() { _move = false; _category = null; }
}
