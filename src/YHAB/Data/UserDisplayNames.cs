namespace YHAB.Data;

// Collaborators see a display name, never the account's email address or username.
internal static class UserDisplayNames
{
    public const int MaximumLength = 80;
    public const string Default = "Member";

    public static string? Normalize(string? displayName)
        => string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();

    public static bool ContainsIdentity(string displayName, string? identity)
        => !string.IsNullOrWhiteSpace(identity)
            && displayName.Contains(identity.Trim(), StringComparison.OrdinalIgnoreCase);

    // Resolve at read time as well as when Profile saves so an email change cannot turn an
    // existing display name into an address that sharing would expose.
    public static string Resolve(string? displayName, string? email, string? username)
    {
        var name = Normalize(displayName);
        return name is null || ContainsIdentity(name, email) || ContainsIdentity(name, username) ? Default : name;
    }
}
