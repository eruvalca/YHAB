using System.Security.Cryptography;
using System.Text;

namespace YHAB.Build;

internal static class PatchFluentModule
{
    private const string ExpectedHash = "OsbgDd/2/UVtrhxUSt5/oqRMuNjDEUoRky40UXA1Z3E=";
    private const string ThemeCache = "Dd=new Map,Hd=new Map";

    public static async Task<int> RunAsync(string source, string destination, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(source, cancellationToken);
        if (!string.Equals(Convert.ToBase64String(SHA256.HashData(bytes)), ExpectedHash, StringComparison.Ordinal))
        {
            await Console.Error.WriteLineAsync("YHAB Fluent compatibility fix expects the pinned 5.0.1 module. Review/remove the fix when upgrading Fluent; do not patch an unknown bundle.");
            return 1;
        }
        var original = Encoding.UTF8.GetString(bytes);
        if (original.IndexOf(ThemeCache, StringComparison.Ordinal) < 0
            || original.IndexOf(ThemeCache, StringComparison.Ordinal) != original.LastIndexOf(ThemeCache, StringComparison.Ordinal))
        {
            await Console.Error.WriteLineAsync("Expected exactly one theme-string cache declaration in the Fluent module.");
            return 1;
        }
        // Theme objects are immutable cache keys, not application-owned history.
        var patched = original.Replace(ThemeCache, "Dd=new WeakMap,Hd=new Map", StringComparison.Ordinal);
        if (!File.Exists(destination) || !string.Equals(await File.ReadAllTextAsync(destination, cancellationToken), patched, StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
            await File.WriteAllTextAsync(destination, patched, cancellationToken);
        }
        return 0;
    }
}
