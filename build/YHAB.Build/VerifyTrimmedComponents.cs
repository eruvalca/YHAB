using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace YHAB.Build;

/// <summary>
/// Razor pages are discovered by reflection, so a trimmed component assembly that omits
/// them fails at runtime with "root component type could not be found".
/// </summary>
internal static class VerifyTrimmedComponents
{
    private const string RouteAttributeTypeName = "Microsoft.AspNetCore.Components.RouteAttribute";

    public static async Task<int> RunAsync(string sourcePath, string trimmedPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(trimmedPath))
        {
            await Console.Error.WriteLineAsync($"Expected the trimmed component assembly at '{trimmedPath}'.".AsMemory(), cancellationToken);
            return 2;
        }

        var expected = await ReadRoutedComponentsAsync(sourcePath, cancellationToken);
        var trimmed = await ReadRoutedComponentsAsync(trimmedPath, cancellationToken);
        var missing = expected.Except(trimmed, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length == 0)
        {
            return 0;
        }

        await Console.Error.WriteLineAsync(
            ($"Trimming removed routed Razor components from '{Path.GetFileName(trimmedPath)}': {string.Join(", ", missing)}. " +
             "Keep the component assembly rooted in the browser host so its pages remain discoverable.").AsMemory(),
            cancellationToken);
        return 1;
    }

    private static async Task<HashSet<string>> ReadRoutedComponentsAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var components = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            if (HasRouteAttribute(reader, type))
            {
                components.Add($"{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return components;
    }

    private static bool HasRouteAttribute(MetadataReader reader, TypeDefinition type)
    {
        foreach (var handle in type.GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (string.Equals(GetAttributeTypeName(reader, attribute), RouteAttributeTypeName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetAttributeTypeName(MetadataReader reader, CustomAttribute attribute)
    {
        switch (attribute.Constructor.Kind)
        {
            case HandleKind.MemberReference:
                var member = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                if (member.Parent.Kind != HandleKind.TypeReference)
                {
                    return null;
                }

                var reference = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
                return $"{reader.GetString(reference.Namespace)}.{reader.GetString(reference.Name)}";
            case HandleKind.MethodDefinition:
                var method = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
                var definingType = reader.GetTypeDefinition(method.GetDeclaringType());
                return $"{reader.GetString(definingType.Namespace)}.{reader.GetString(definingType.Name)}";
            default:
                return null;
        }
    }
}
