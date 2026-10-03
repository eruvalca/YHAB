using Microsoft.AspNetCore.Components;
using YHAB.SharedKernel.Budgeting;
namespace YHAB.UI.Features.Budgeting.Components;

public sealed partial class TransactionEditor
{
    [Parameter, EditorRequired] public PlanSnapshot Plan { get; set; } = default!;
    [Parameter] public TransactionData? Transaction { get; set; }
    [Parameter] public Guid? AccountId { get; set; }
    [Parameter, EditorRequired] public EventCallback<PlanCommand> OnCommand { get; set; }
    [Parameter, EditorRequired] public EventCallback OnClose { get; set; }
    [Parameter] public bool Busy { get; set; }
    private static readonly string[] _directions = ["Outflow", "Inflow"];
    private static readonly string[] _flags = ["None", "Red", "Orange", "Yellow", "Green", "Blue", "Purple"];
    private static readonly RepeatFrequency[] _frequencies = Enum.GetValues<RepeatFrequency>();
    private AccountData[] _accounts = [];
    private AccountData[] _destinations = [];
    private CategoryData[] _categories = [];
    private readonly List<SplitInput> _splits = [];
    private Guid _account;
    private Guid _transfer;
    private Guid _category;
    private string _payee = "";
    private string _memo = "";
    private string _direction = "Outflow";
    private string _flag = "None";
    private DateOnly _date;
    private bool _cleared;
    private bool _amountValid = true;
    private bool _split;
    private RepeatFrequency _repeat;
    private decimal Amount { get; set; }
    private decimal SignedAmount => string.Equals(_direction, "Outflow", StringComparison.Ordinal) ? -Math.Abs(Amount) : Math.Abs(Amount);
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
    private bool IsReconciled => Transaction is { State: ClearingState.Reconciled } or { TransferState: ClearingState.Reconciled };
    private bool CannotSave => Busy || IsReconciled || !_amountValid || _account == Guid.Empty || (_transfer != Guid.Empty && _transfer == _account)
        || (_repeat == RepeatFrequency.None && _date > Plan.Today)
        || (NeedsCategory && _split && (_splits.Count == 0 || _splits.Any(item => !item.Valid) || _splits.Sum(item => item.Amount) != SignedAmount));

    protected override void OnInitialized()
    {
        _accounts = Plan.Accounts.Where(item => !item.Closed).ToArray();
        _destinations = [new(Guid.Empty, "Not a transfer", AccountKind.Checking, 0, Plan.Today, false, ""), .. _accounts];
        _categories = [new(Guid.Empty, Guid.Empty, "Inflow: Ready to assign", "", 0, false, null, null),
            .. Plan.Categories.Where(item => item.CreditAccountId is null).OrderBy(item => item.Hidden).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)];
        _account = Transaction?.AccountId ?? AccountId ?? _accounts.FirstOrDefault()?.Id ?? Guid.Empty;
        _transfer = Transaction?.TransferAccountId ?? Guid.Empty;
        InitializeValues();
        if (Transaction is not null)
        {
            _splits.AddRange(Transaction.Splits.Select(item => new SplitInput { Id = item.Id, Category = item.CategoryId ?? Guid.Empty, Amount = item.Amount, Memo = item.Memo }));
        }
    }
    private SplitData? FirstSplit => Transaction is { Splits.Count: > 0 } ? Transaction.Splits[0] : null;
    private void InitializeValues()
    {
        _date = Transaction?.Date ?? Plan.Today;
        _payee = Transaction?.Payee ?? "";
        _memo = Transaction?.Memo ?? "";
        Amount = Math.Abs(Transaction?.Amount ?? 0);
        _direction = Transaction?.Amount > 0 ? "Inflow" : "Outflow";
        _cleared = Transaction?.State is ClearingState.Cleared or ClearingState.Reconciled;
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
    private void StartSplit() { _split = true; if (_splits.Count == 0) { _splits.Add(new() { Category = _category, Amount = SignedAmount }); } }
    private void AddSplit() => _splits.Add(new() { Amount = SignedAmount - _splits.Sum(item => item.Amount) });
    private void RemoveSplit(SplitInput split) => _splits.Remove(split);
    private Task SaveAsync()
    {
        var splits = BuildSplits();
        return OnCommand.InvokeAsync(new SaveTransaction(Plan.Version, new(Transaction?.Id ?? Guid.Empty, _account, _date, _payee, _memo, SignedAmount,
            _transfer == Guid.Empty ? null : _transfer, _cleared ? ClearingState.Cleared : ClearingState.Uncleared,
            Transaction?.TransferState ?? ClearingState.Uncleared, false, string.Equals(_flag, "None", StringComparison.Ordinal) ? "" : _flag, splits, _repeat)));
    }
    private SplitData[] BuildSplits()
    {
        if (!NeedsCategory) { return []; }
        if (_split) { return _splits.Select(item => new SplitData(item.Id, item.Category == Guid.Empty ? null : item.Category, item.Amount, item.Memo)).ToArray(); }
        return [new(FirstSplit?.Id ?? Guid.NewGuid(), _category == Guid.Empty ? null : _category, SignedAmount, "")];
    }
    private sealed class SplitInput
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid Category { get; set; }
        public decimal Amount { get; set; }
        public string Memo { get; set; } = "";
        public bool Valid { get; set; } = true;
    }
}
