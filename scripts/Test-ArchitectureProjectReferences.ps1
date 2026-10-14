[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot
$fixtureRoot = Join-Path $repoRoot ('artifacts/architecture-reference-tests/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
$importPath = Join-Path $fixtureRoot 'References.targets'

# Exercise the real evaluated projects without editing their source or project files.
Set-Content -LiteralPath $importPath -Encoding utf8 -Value @'
<Project>
  <ItemGroup Condition="'$(MSBuildProjectFullPath)' == '$(ArchitectureProbeProject)'">
    <ProjectReference Include="$(ArchitectureProbeReference)"
                      ReferenceOutputAssembly="$(ArchitectureProbeAssembly)"
                      Condition="'$(ArchitectureProbeReference)' != '' And ('$(ArchitectureProbeConfiguration)' == '' Or '$(Configuration)' == '$(ArchitectureProbeConfiguration)')" />
  </ItemGroup>
</Project>
'@

$kernel = Join-Path $repoRoot 'src/YHAB.SharedKernel/YHAB.SharedKernel.csproj'
$ui = Join-Path $repoRoot 'src/YHAB.UI/YHAB.UI.csproj'
$client = Join-Path $repoRoot 'src/YHAB.Client/YHAB.Client.csproj'
$server = Join-Path $repoRoot 'src/YHAB/YHAB.csproj'
$buildTool = Join-Path $repoRoot 'build/YHAB.Build/YHAB.Build.csproj'

function Assert-ReferencePolicy {
    param(
        [string] $Name,
        [string] $Project,
        [string] $Reference = '',
        [switch] $Reject,
        [string] $Configuration = 'Debug',
        [string] $ReferenceConfiguration = '',
        [string] $ReferenceOutputAssembly = 'true',
        [string] $Target = 'ValidateArchitectureProjectReferences'
    )

    $arguments = @(
        'msbuild', $Project, '-nologo', '-verbosity:quiet', "-target:$Target",
        "-property:CustomAfterMicrosoftCommonTargets=$importPath",
        "-property:ArchitectureProbeProject=$Project",
        "-property:ArchitectureProbeReference=$Reference",
        "-property:ArchitectureProbeAssembly=$ReferenceOutputAssembly",
        "-property:ArchitectureProbeConfiguration=$ReferenceConfiguration",
        "-property:Configuration=$Configuration"
    )
    $output = & dotnet @arguments 2>&1
    $exitCode = $LASTEXITCODE
    $text = $output -join "`n"
    Set-Content -LiteralPath (Join-Path $fixtureRoot "$Name.log") -Value $text -Encoding utf8
    if ($Reject) {
        if ($exitCode -eq 0 -or $text -notmatch 'error YHABARCH001\b') {
            throw "${Name}: expected YHABARCH001, got exit code $exitCode.`n$text"
        }
    }
    elseif ($exitCode -ne 0) {
        throw "${Name}: expected success, got exit code $exitCode.`n$text"
    }
    Write-Host "PASS: $Name"
}

Assert-ReferencePolicy 'KernelBaseline' $kernel
Assert-ReferencePolicy 'UiBaselineWithBuildTool' $ui
Assert-ReferencePolicy 'ClientBaselineWithBuildTool' $client
Assert-ReferencePolicy 'KernelRejectsServer' $kernel $server -Reject
Assert-ReferencePolicy 'UiRejectsServer' $ui $server -Reject
Assert-ReferencePolicy 'ClientRejectsServer' $client $server -Reject
Assert-ReferencePolicy 'UiRejectsClient' $ui $client -Reject
Assert-ReferencePolicy 'KernelRejectsUi' $kernel $ui -Reject
Assert-ReferencePolicy 'UnusedBuildOnlyReferenceStillRejected' $ui $server -ReferenceOutputAssembly 'false' -Reject
Assert-ReferencePolicy 'BuildToolCannotBecomeApplicationReference' $ui $buildTool -Reject
Assert-ReferencePolicy 'SameFilenameElsewhereIsRejected' $client (Join-Path $fixtureRoot 'YHAB.UI.csproj') -Reject
Assert-ReferencePolicy 'NormalizedAllowedPath' $ui '../YHAB.SharedKernel/../YHAB.SharedKernel/YHAB.SharedKernel.csproj'
Assert-ReferencePolicy 'InactiveConditionalReference' $kernel $server -ReferenceConfiguration 'Release'
Assert-ReferencePolicy 'ActiveConditionalReference' $kernel $server -ReferenceConfiguration 'Release' -Configuration 'Release' -Reject
Assert-ReferencePolicy 'ServerCompositionIsUnrestricted' $server $ui
Assert-ReferencePolicy 'NormalBuildRunsValidation' $kernel $server -Target 'Build' -Reject

Write-Host "All 16 project-reference policy checks passed. Logs: $fixtureRoot"

# The last probe fails by design. Do not let GitHub's pwsh wrapper propagate its
# native exit code after every assertion has passed; unexpected results throw above.
exit 0
