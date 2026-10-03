namespace YHAB.SharedKernel.Budgeting;

/// <summary>Create Plan Request exchanged between the budgeting service and its clients.</summary>
public sealed record CreatePlanRequest(string Name, bool StarterCategories = true);
