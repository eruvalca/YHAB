using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Intermediate;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace YHAB.Build;

internal sealed class ValidateRazorCodeBehind
{
    private bool _hasErrors;

    public string[] Components { get; set; } = [];

    public string[] CompileFiles { get; set; } = [];

    public string ProjectDirectory { get; set; } = string.Empty;

    public string RootNamespace { get; set; } = string.Empty;

    public string DefineConstants { get; set; } = string.Empty;

    public bool Execute(CancellationToken cancellationToken)
    {
        var paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var compilePaths = CompileFiles.ToHashSet(paths);
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest)
            .WithPreprocessorSymbols(DefineConstants.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var capture = new ComponentStructurePass();
        var fileSystem = RazorProjectFileSystem.Create(ProjectDirectory);
        var engine = RazorProjectEngine.Create(RazorConfiguration.Default, fileSystem, builder =>
        {
            builder.SetRootNamespace(RootNamespace);
            builder.ConfigureParserOptions(options =>
            {
                options.UseRoslynTokenizer = true;
                options.CSharpParseOptions = parseOptions;
            });
            builder.Features.Add(capture);
        });

        foreach (var path in Components)
        {
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable S1075 // Razor uses project-relative virtual paths with a leading forward slash, independent of OS path separators.
            var relativePath = "/" + Path.GetRelativePath(ProjectDirectory, path).Replace('\\', '/');
#pragma warning restore S1075
            capture.Reset();
            engine.Process(fileSystem.GetItem(relativePath), cancellationToken);

            foreach (var directive in capture.MemberBlocks)
            {
                // Imports are also processed for each component. Report each file's own blocks once.
                if (paths.Equals(directive.Source.FilePath, path))
                {
                    Report("RUV002", path, directive.Source.LineIndex + 1, directive.Source.CharacterIndex + 1,
                        $"Move @{directive.Name} members into '{Path.GetFileName(path)}.cs'. Inline component member blocks are not allowed.");
                }
            }

            if (Path.GetFileName(path).Equals("_Imports.razor", StringComparison.Ordinal))
            {
                continue;
            }

            var companion = path + ".cs";
            if (!File.Exists(companion))
            {
                Report("RUV001", path, 1, 1, $"Add '{Path.GetFileName(companion)}' with the matching partial component class. Code-behind is required even for markup-only components.");
                continue;
            }

            if (!compilePaths.Contains(companion))
            {
                Report("RUV004", companion, 1, 1, "The component code-behind file must be included in the project's Compile items.");
                continue;
            }

            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(companion), parseOptions, companion, cancellationToken: cancellationToken).GetRoot(cancellationToken);
            var matchingClass = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Any(declaration =>
                !declaration.Ancestors().OfType<TypeDeclarationSyntax>().Any()
                && string.Equals(declaration.Identifier.ValueText, capture.ClassName, StringComparison.Ordinal)
                && declaration.Modifiers.Any(SyntaxKind.PartialKeyword)
                && (declaration.TypeParameterList?.Parameters.Count ?? 0) == capture.TypeParameterCount
                && string.Equals(GetNamespace(declaration), capture.Namespace, StringComparison.Ordinal));

            if (!matchingClass)
            {
                Report("RUV003", companion, 1, 1,
                    $"Declare the matching partial class '{capture.Namespace}.{capture.ClassName}' with {capture.TypeParameterCount} type parameter(s) in this code-behind file.");
            }
        }

        return !_hasErrors;
    }

    private static string GetNamespace(ClassDeclarationSyntax declaration) =>
        string.Join(".", declaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse()
            .Select(item => string.Concat(item.Name.DescendantTokens().Select(token => token.ValueText))));

    private void Report(string code, string path, int line, int column, string message)
    {
        _hasErrors = true;
        Console.Error.WriteLine($"{path}({line},{column}): error {code}: {message}");
    }

    private sealed class ComponentStructurePass : IntermediateNodePassBase, IRazorDirectiveClassifierPass
    {
        // Inspect directives before Razor lowers/removes @code and @functions nodes.
        public override int Order => -10000;

        public List<(string Name, SourceSpan Source)> MemberBlocks { get; } = [];
        private NamespaceDeclarationIntermediateNode? _componentNamespace;
        private ClassDeclarationIntermediateNode? _componentClass;

        // Later Razor passes populate generic parameters on these same nodes.
        public string? Namespace => _componentNamespace?.Name;
        public string? ClassName => _componentClass?.Name;
        public int TypeParameterCount => _componentClass?.TypeParameters.Length ?? 0;

        public void Reset()
        {
            MemberBlocks.Clear();
            _componentNamespace = null;
            _componentClass = null;
        }

        protected override void ExecuteCore(RazorCodeDocument codeDocument, DocumentIntermediateNode documentNode, CancellationToken cancellationToken)
        {
            Visit(documentNode);
        }

        private void Visit(IntermediateNode node)
        {
            if (node is DirectiveIntermediateNode { DirectiveName: "code" or "functions", Source: { } source } directive)
            {
                MemberBlocks.Add((directive.DirectiveName, source));
            }
            else if (node is NamespaceDeclarationIntermediateNode { IsPrimaryNamespace: true } ns)
            {
                _componentNamespace = ns;
            }
            else if (node is ClassDeclarationIntermediateNode { IsPrimaryClass: true } type)
            {
                _componentClass = type;
            }

            foreach (var child in node.Children)
            {
                Visit(child);
            }
        }
    }
}
