using System.Text.Json.Serialization;

namespace YHAB.SharedKernel.Budgeting;

[JsonConverter(typeof(JsonStringEnumConverter<AccountKind>))]
public enum AccountKind
{
    Checking,
    Savings,
    Cash,
    CreditCard,
    LineOfCredit,
    Mortgage,
    AutoLoan,
    StudentLoan,
    PersonalLoan,
    MedicalDebt,
    OtherDebt,
    Asset,
    Liability
}
