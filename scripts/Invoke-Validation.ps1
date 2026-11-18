#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('Quick', 'Fast', 'Infrastructure', 'Full')]
    [string] $Scope = 'Fast',
    [ValidateSet('Integration', 'Aspire', 'Browser')]
    [string[]] $Suite = @('Integration'),
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path $PSScriptRoot

function Invoke-CheckedCommand([string] $Command, [string[]] $Arguments) {
    Write-Host "> $Command $($Arguments -join ' ')"
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command failed with exit code $LASTEXITCODE."
    }
}

try {
    if ($PSBoundParameters.ContainsKey('Suite') -and $Scope -ne 'Infrastructure') {
        throw '-Suite applies only to -Scope Infrastructure; Full always runs all suites.'
    }

    Push-Location $repoRoot
    try {
        Invoke-CheckedCommand 'git' @('diff', '--cached', '--check')
        if ($Scope -ne 'Quick') {
            Invoke-CheckedCommand 'git' @('diff', '--check')
            Invoke-CheckedCommand 'dotnet' @('format', 'YHAB.slnx', '--severity', 'warn', '--verify-no-changes')
            Invoke-CheckedCommand 'dotnet' @('build', 'YHAB.slnx', '--configuration', $Configuration)

            if ($Scope -eq 'Full') {
                Invoke-CheckedCommand 'dotnet' @('test', '--solution', 'YHAB.slnx', '--configuration', $Configuration, '--no-build', '--max-parallel-test-modules', '1')
            }
            else {
                $projects = @('YHAB.UnitTests', 'YHAB.ComponentTests')
                if ($Scope -eq 'Infrastructure') {
                    $suiteProjects = @{
                        Integration = 'YHAB.IntegrationTests'
                        Aspire = 'YHAB.AspireIntegrationTests'
                        Browser = 'YHAB.PlaywrightTests'
                    }
                    $projects += $Suite | Select-Object -Unique | ForEach-Object { $suiteProjects[$_] }
                }
                foreach ($project in $projects) {
                    $testArguments = @('test', '--project', "tests/$project/$project.csproj", '--configuration', $Configuration, '--no-build')
                    if ($project -eq 'YHAB.PlaywrightTests') {
                        $testArguments += @('--filter-not-trait', 'Category=Performance')
                    }
                    Invoke-CheckedCommand 'dotnet' $testArguments
                }
            }
        }
        Write-Host "Validation succeeded: $Scope."
    }
    finally {
        Pop-Location
    }
}
catch {
    [Console]::Error.WriteLine("Validation failed ($Scope): $($_.Exception.Message)")
    exit 1
}
