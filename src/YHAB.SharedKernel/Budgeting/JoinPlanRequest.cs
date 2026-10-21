namespace YHAB.SharedKernel.Budgeting;

/// <summary>Redeems a one-time share code for the signed-in account.</summary>
public sealed record JoinPlanRequest(string Code);
