namespace YHAB.SharedKernel.Budgeting;

/// <summary>The antiforgery request token used alongside the existing Identity cookie.</summary>
public sealed record RequestToken(string Token);
