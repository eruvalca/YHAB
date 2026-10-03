[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot
$fixtureRoot = Join-Path $repoRoot ('artifacts/razor-codebehind-tests/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
$projectPath = Join-Path $fixtureRoot 'PolicyProbe.csproj'
# Exercise the real parent targets, independently of unrelated analyzer/style choices.
Set-Content -LiteralPath (Join-Path $fixtureRoot 'Directory.Build.props') -Value '<Project />' -Encoding utf8
Set-Content -LiteralPath (Join-Path $fixtureRoot '.editorconfig') -Value 'root = true' -Encoding utf8
$projectText = @'
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>PolicyProbe</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /></ItemGroup>
</Project>
'@
Set-Content -LiteralPath $projectPath -Value $projectText -Encoding utf8

function Set-Fixture([string] $Markup, [AllowNull()] $CodeBehind) {
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'Probe.razor') -Value $Markup -Encoding utf8
    $companion = Join-Path $fixtureRoot 'Probe.razor.cs'
    if ($null -eq $CodeBehind) {
        if (Test-Path -LiteralPath $companion) {
            Remove-Item -LiteralPath $companion
        }
    }
    else {
        Set-Content -LiteralPath $companion -Value $CodeBehind -Encoding utf8
    }
}

function Assert-Build([string] $Name, [string] $ExpectedCode = '') {
    $output = & dotnet build $projectPath --nologo --verbosity quiet 2>&1
    $exitCode = $LASTEXITCODE
    $text = $output -join "`n"
    if ($ExpectedCode) {
        if ($exitCode -eq 0 -or $text -notmatch "error $ExpectedCode\b") {
            throw "${Name}: expected a build failure with $ExpectedCode, got exit code $exitCode.`n$text"
        }
    }
    elseif ($exitCode -ne 0) {
        throw "${Name}: expected a successful build, got exit code $exitCode.`n$text"
    }
    Write-Host "PASS: $Name"
}

$validClass = 'namespace PolicyProbe; public partial class Probe { }'
Set-Fixture '<h1>Markup-only component</h1>' $validClass
Assert-Build 'Markup-only component with matching companion'

Set-Fixture '<h1>Markup-only component</h1>' $null
Assert-Build 'Deleted companion fails an incremental build' 'RUV001'

Set-Fixture '<h1>Markup-only component</h1>' '// An empty file cannot satisfy the policy.'
Assert-Build 'Empty companion' 'RUV003'

Set-Fixture '<h1>Markup-only component</h1>' 'namespace WrongNamespace; public partial class Probe { }'
Assert-Build 'Wrong namespace' 'RUV003'

Set-Fixture '<h1>Markup-only component</h1>' 'namespace PolicyProbe; public partial class OtherComponent { }'
Assert-Build 'Wrong class name' 'RUV003'

Set-Fixture '<h1>Markup-only component</h1>' 'namespace PolicyProbe; public class Probe { }'
Assert-Build 'Missing partial modifier' 'RUV003'

Set-Fixture '<h1>Markup-only component</h1>' 'namespace PolicyProbe; public class Container { public partial class Probe { } }'
Assert-Build 'Nested class cannot satisfy the policy' 'RUV003'

Set-Fixture '<h1>Markup-only component</h1>' "#if false`n$validClass`n#endif"
Assert-Build 'Inactive conditional declaration cannot satisfy the policy' 'RUV003'

Set-Fixture "<h1>Inline</h1>`n@code { }" $validClass
Assert-Build 'Empty code block is forbidden' 'RUV002'

Set-Fixture "<h1>Inline</h1>`n@functions { private int Value => 1; }" $validClass
Assert-Build 'Functions block is forbidden' 'RUV002'

Set-Fixture @'
@* @code { } and @functions { } are examples in a Razor comment. *@
@{
    var example = """
        @code { }
        @functions { }
        """;
}
<p>@example</p>
<p>@("@code { }")</p>
@foreach (var value in new[] { 1, 2 })
{
    @if (value > 0)
    {
        <span>@value</span>
    }
}
'@ $validClass
Assert-Build 'Comments, strings, render expressions and control flow remain allowed'

Set-Content -LiteralPath (Join-Path $fixtureRoot '_Imports.razor') -Value '@using Microsoft.AspNetCore.Components' -Encoding utf8
Set-Fixture '<h1>Imports need no companion</h1>' $validClass
Assert-Build 'Imports file is exempt'

Set-Fixture "@namespace Custom.Pages`n<h1>Explicit namespace</h1>" 'namespace Custom.Pages; public partial class Probe { }'
Assert-Build 'Explicit Razor namespace is respected'

Set-Content -LiteralPath (Join-Path $fixtureRoot '_Imports.razor') -Value '@namespace Custom.Pages' -Encoding utf8
Set-Fixture '<h1>Inherited namespace</h1>' 'namespace Custom.Pages; public partial class Probe { }'
Assert-Build 'Namespace from imports is respected'
Set-Content -LiteralPath (Join-Path $fixtureRoot '_Imports.razor') -Value '@using Microsoft.AspNetCore.Components' -Encoding utf8

Set-Fixture "@typeparam TItem`n<p>Generic component</p>" 'namespace PolicyProbe; public partial class Probe<TItem> { }'
Assert-Build 'Generic component has matching arity'
Set-Fixture "@typeparam TItem`n<p>Generic component</p>" $validClass
Assert-Build 'Wrong generic arity' 'RUV003'

Set-Fixture '<h1>Excluded companion</h1>' $validClass
$excludedProject = $projectText.Replace('</Project>', '<ItemGroup><Compile Remove="Probe.razor.cs" /></ItemGroup></Project>')
Set-Content -LiteralPath $projectPath -Value $excludedProject -Encoding utf8
Assert-Build 'Companion must be compiled' 'RUV004'

Write-Host "All Razor code-behind checks passed. Fixtures: $fixtureRoot"
