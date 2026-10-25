using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class TransactionEditor
{
    [Parameter, EditorRequired] public PlanSnapshot Plan { get; set; } = default!;
    [Parameter, EditorRequired] public IReadOnlyList<string> Payees { get; set; } = [];
    [Parameter] public TransactionData? Transaction { get; set; }
    [Parameter] public Guid? AccountId { get; set; }
    [Parameter, EditorRequired] public EventCallback<PlanCommand> OnCommand { get; set; }
    [Parameter, EditorRequired] public EventCallback OnClose { get; set; }
    [Parameter] public bool Busy { get; set; }
    private const string ChooseCategory = "Choose a category";
    private const string OutflowCategoryError = "An outflow needs a spending category. Choose one, or switch the direction to Inflow.";
    private const string BudgetCategoryError = "Money leaving your budget needs a spending category.";
    private static readonly string[] _directions = ["Outflow", "Inflow"];
    private static readonly string[] _flags = ["None", "Red", "Orange", "Yellow", "Green", "Blue", "Purple"];
    private static readonly RepeatFrequency[] _frequencies = Enum.GetValues<RepeatFrequency>();
    private static readonly CategoryData _readyToAssign = new(Guid.Empty, Guid.Empty, "Inflow: Ready to assign", "", 0, false, null, null);
    private AccountData[] _accounts = [];
    private AccountData[] _destinations = [];
    private CategoryData[] _categories = [];
    private CategoryData[] _spendingCategories = [];
    private readonly List<SplitInput> _splits = [];
    private Guid _account;
    private Guid _transfer;
    private Guid _category;
    private string _payee = "";
    private string _memo = "";
    private string _direction = "Outflow";
    private string _flag = "None";
    private DateOnly _date;
    private bool _amountValid = true;
    private bool _split;
    private bool _saving;
    private RepeatFrequency _repeat;
    private decimal Amount { get; set; }
    private string _initial = "";
    private AmountInput? _amountInput;
    private bool _focusCategory;
    private ElementReference _categoryError;
    private bool IsOutflow => string.Equals(_direction, "Outflow", StringComparison.Ordinal);
    private decimal SignedAmount => IsOutflow ? -Math.Abs(Amount) : Math.Abs(Amount);
    private int DirectionSign => IsOutflow ? -1 : 1;
    private bool BudgetOutflow => DirectionSign * BudgetDirection < 0;
    private CategoryData[] CategoryOptions => BudgetOutflow ? _spendingCategories : _categories;
    private string CategoryError => IsOutflow ? OutflowCategoryError : BudgetCategoryError;
    private int BudgetDirection
    {
        get
        {
            var source = _accounts.SingleOrDefault(item => item.Id == _account);
            return source is not null && BudgetFacts.IsBudget(source.Kind) ? 1 : -1;
        }
    }
    private bool CategoryMissing => NeedsCategory && !_split && BudgetOutflow && !_spendingCategories.Any(item => item.Id == _category);
    // A refresh can retire a category this draft still references, so a saved
    // split must point at a choice the editor can still offer.
    private bool SplitCategoryMissing(SplitInput split) => SplitNeedsCategory(split) && !SplitCategoryOptions(split).Any(item => item.Id == split.Category);
    private bool SplitNeedsCategory(SplitInput split) => split.Amount * BudgetDirection < 0;
    private CategoryData[] SplitCategoryOptions(SplitInput split) => SplitNeedsCategory(split) ? _spendingCategories : _categories;
    private bool NeedsCategory
    {
        get
        {
            var source = _accounts.SingleOrDefault(item => item.Id == _account);
            var destination = _accounts.SingleOrDefault(item => item.Id == _transfer);
            return source is not null && (BudgetFacts.IsBudget(source.Kind) || destination is not null && BudgetFacts.IsBudget(destination.Kind))
                && !(destination is not null && BudgetFacts.IsBudget(source.Kind) && BudgetFacts.IsBudget(destination.Kind));
        }
    }
    private bool CannotSave => Busy || _saving || !_amountValid || _account == Guid.Empty || (_transfer != Guid.Empty && _transfer == _account)
        || (_repeat == RepeatFrequency.None && _date > Plan.Today)
        || CategoryMissing
        || (NeedsCategory && _split && (_splits.Count == 0 || _splits.Any(item => !item.Valid || SplitCategoryMissing(item)) || _splits.Sum(item => item.Amount) != SignedAmount));
    // Amount text stays a draft until the editor saves, so the guard compares
    // the committed values plus each field's uncommitted text.
    private bool HasUnsavedChanges() => !string.Equals(Signature(), _initial, StringComparison.Ordinal)
        || _amountInput?.HasDraft is true
        || _splits.Any(item => item.AmountInput?.HasDraft is true);

    protected override void OnInitialized()
    {
        RebuildCatalog();
        _account = Transaction?.AccountId ?? AccountId ?? _accounts.FirstOrDefault()?.Id ?? Guid.Empty;
        _transfer = Transaction?.TransferAccountId ?? Guid.Empty;
        InitializeValues();
        if (Transaction is not null)
        {
            _splits.AddRange(Transaction.Splits.Select(item => new SplitInput { Id = item.Id, Category = item.CategoryId ?? Guid.Empty, Amount = item.Amount, Memo = item.Memo }));
        }

        _initial = Signature();
    }

    // A refresh can replace the catalog under an open draft. The typed values
    // stay; only the choices they select from are rebuilt.
    protected override void OnParametersSet() => RebuildCatalog();
    private void RebuildCatalog()
    {
        _accounts = Plan.Accounts.Where(item => !item.Closed).ToArray();
        _destinations = [new(Guid.Empty, "Not a transfer", AccountKind.Checking, 0, Plan.Today, false, ""), .. _accounts];
        _spendingCategories = Plan.Categories.Where(item => item.CreditAccountId is null)
            .OrderBy(item => item.Hidden).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        _categories = [_readyToAssign, .. _spendingCategories];
    }
    private string Signature()
    {
        string[] parts =
        [
            _payee,
            _memo,
            Amount.ToString(CultureInfo.InvariantCulture),
            _direction,
            _date.ToString("O", CultureInfo.InvariantCulture),
            _flag,
            _repeat.ToString(),
            _account.ToString(),
            _transfer.ToString(),
            _category.ToString(),
            _split.ToString(),
            string.Join(";", _splits.Select(item => $"{item.Id:N},{item.Category:N},{item.Amount.ToString(CultureInfo.InvariantCulture)},{item.Memo}")),
        ];
        return string.Join("|", parts);
    }
    private SplitData? FirstSplit => Transaction is { Splits.Count: > 0 } ? Transaction.Splits[0] : null;
    private void InitializeValues()
    {
        _date = Transaction?.Date ?? Plan.Today;
        _payee = Transaction?.Payee ?? "";
        _memo = Transaction?.Memo ?? "";
        Amount = Math.Abs(Transaction?.Amount ?? 0);
        _direction = Transaction?.Amount > 0 ? "Inflow" : "Outflow";
        _repeat = Transaction?.Repeat ?? RepeatFrequency.None;
        _flag = string.IsNullOrEmpty(Transaction?.Flag) ? "None" : Transaction.Flag;
        _category = FirstSplit?.CategoryId ?? Guid.Empty;
        _split = Transaction?.Splits.Count > 1;
    }

    internal static string FrequencyLabel(RepeatFrequency frequency) => frequency switch
    {
        RepeatFrequency.None => "Does not repeat",
        RepeatFrequency.EveryTwoWeeks => "Every two weeks",
        RepeatFrequency.TwiceMonthly => "Twice a month",
        RepeatFrequency.EveryTwoMonths => "Every two months",
        RepeatFrequency.EverySixMonths => "Every six months",
        _ => frequency.ToString(),
    };
    private void DirectionChanged() => _focusCategory = CategoryMissing;
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusCategory)
        {
            _focusCategory = false;
            await _categoryError.FocusAsync();
        }

        await base.OnAfterRenderAsync(firstRender);
    }
    private void StartSplit() { _split = true; if (_splits.Count == 0) { _splits.Add(new() { Category = _category, Amount = SignedAmount }); } }
    private void AddSplit() => _splits.Add(new() { Amount = SignedAmount - _splits.Sum(item => item.Amount) });
    private void RemoveSplit(SplitInput split) => _splits.Remove(split);
    private async Task SaveOnEnterAsync(KeyboardEventArgs args)
    {
        if (!string.Equals(args.Key, "Enter", StringComparison.Ordinal) || args.IsComposing)
        {
            return;
        }

        await SaveAsync();
    }
    private async Task SaveAsync()
    {
        if (CannotSave)
        {
            return;
        }

        _saving = true;
        try
        {
            var amount = await _amountInput!.CommitAsync();
            if (amount is null)
            {
                return;
            }

            Amount = amount.Value;
            if (NeedsCategory && _split)
            {
                foreach (var split in _splits)
                {
                    var splitAmount = await split.AmountInput!.CommitAsync();
                    if (splitAmount is null)
                    {
                        return;
                    }

                    split.Amount = splitAmount.Value;
                }
            }

            var splits = BuildSplits();
            await OnCommand.InvokeAsync(new SaveTransaction(Plan.Version, new(Transaction?.Id ?? Guid.Empty, _account, _date, _payee, _memo, SignedAmount,
                _transfer == Guid.Empty ? null : _transfer, string.Equals(_flag, "None", StringComparison.Ordinal) ? "" : _flag, splits, _repeat)));
        }
        finally
        {
            _saving = false;
        }
    }
    private SplitData[] BuildSplits()
    {
        if (!NeedsCategory) { return []; }
        if (_split) { return _splits.Select(item => new SplitData(item.Id, item.Category == Guid.Empty ? null : item.Category, item.Amount, item.Memo)).ToArray(); }
        return [new(FirstSplit?.Id ?? Guid.CreateVersion7(), _category == Guid.Empty ? null : _category, SignedAmount, "")];
    }
    private sealed class SplitInput
    {
        public Guid Id { get; set; } = Guid.CreateVersion7();
        public Guid Category { get; set; }
        public decimal Amount { get; set; }
        public string Memo { get; set; } = "";
        public bool Valid { get; set; } = true;
        public AmountInput? AmountInput { get; set; }
    }
}
