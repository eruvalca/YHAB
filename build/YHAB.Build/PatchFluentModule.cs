using System.Security.Cryptography;
using System.Text;

namespace YHAB.Build;

internal static class PatchFluentModule
{
    private const string ExpectedHash = "bfW5YG7VW5dDEqT/Edj/X+VSp4tLUYtM/80VK2zyUbo=";
    private const string Subscription = """
        z.getNotifier(n).subscribe({handleChange:()=>{t.forCustomElement(e,!0),e.$fastController.connect()}},"template"),z.getNotifier(n).subscribe({handleChange:()=>{t.forCustomElement(e,!0),e.$fastController.connect()}},"shadowOptions")
        """;

    public static async Task<int> RunAsync(string source, string observers, string destination)
    {
        var bytes = await File.ReadAllBytesAsync(source);
        if (!string.Equals(Convert.ToBase64String(SHA256.HashData(bytes)), ExpectedHash, StringComparison.Ordinal))
        {
            await Console.Error.WriteLineAsync("YHAB Fluent compatibility fix expects the pinned 5.0.0 module. Review/remove the fix when upgrading Fluent; do not patch an unknown bundle.");
            return 1;
        }
        var original = Encoding.UTF8.GetString(bytes);
        if (original.IndexOf(Subscription, StringComparison.Ordinal) < 0
            || original.IndexOf(Subscription, StringComparison.Ordinal) != original.LastIndexOf(Subscription, StringComparison.Ordinal))
        {
            await Console.Error.WriteLineAsync("Expected exactly one FAST definition observer registration in the Fluent module.");
            return 1;
        }
        var patched = await File.ReadAllTextAsync(observers) + "\n"
            + original.Replace(Subscription, "observeYhabDefinition(t,e,n,z)", StringComparison.Ordinal)
                // Theme objects are immutable cache keys, not application-owned history.
                .Replace("Ld=new Map,zd=new Map", "Ld=new WeakMap,zd=new Map", StringComparison.Ordinal);
        if (!File.Exists(destination) || !string.Equals(await File.ReadAllTextAsync(destination), patched, StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
            await File.WriteAllTextAsync(destination, patched);
        }
        return 0;
    }
}
