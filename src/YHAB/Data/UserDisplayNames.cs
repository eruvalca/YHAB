namespace YHAB.Data;

// Collaborators see a display name, never the account's email address or username.
internal static class UserDisplayNames
{
    public const int MaximumLength = 80;
    public const string Default = "Member";

    public static string? Normalize(string? displayName)
        => string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();

    public static string Resolve(string? displayName) => Normalize(displayName) ?? Default;
}
