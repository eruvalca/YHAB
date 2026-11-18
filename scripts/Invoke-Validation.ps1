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
$validationLockModule = Join-Path $PSScriptRoot 'ValidationLock.psm1'
Import-Module -Name $validationLockModule -Force
$validationMutex = $null

function Invoke-CapturedDotnet([string[]] $Arguments) {
    Write-Host "> dotnet $($Arguments -join ' ')"
    $captured = [Text.StringBuilder]::new()
    & dotnet @Arguments 2>&1 | ForEach-Object {
        $line = $_.ToString()
        [void] $captured.AppendLine($line)
        [Console]::Out.WriteLine($line)
    }
    $exitCode = $LASTEXITCODE
    return [pscustomobject] @{
        ExitCode = $exitCode
        Output = $captured.ToString()
    }
}

function Invoke-SolutionBuild([string] $Configuration) {
    Invoke-CheckedCommand 'dotnet' @('build-server', 'shutdown')
    $buildArguments = @('build', 'YHAB.slnx', '--configuration', $Configuration, '--disable-build-servers')
    $result = Invoke-CapturedDotnet $buildArguments
    if ($result.ExitCode -eq 0) {
        return
    }

    Write-Host $result.Output
    if (Test-WidespreadRazorParserFailure $result.Output) {
        Write-Warning 'Widespread Razor parser failures detected. Cleaning, restoring, and rebuilding before reporting failure.'
        Invoke-CheckedCommand 'dotnet' @('build-server', 'shutdown')
        Invoke-CheckedCommand 'dotnet' @('clean', 'YHAB.slnx', '--configuration', $Configuration)
        Invoke-CheckedCommand 'dotnet' @('restore', 'YHAB.slnx', '--force', '--no-cache')
        Invoke-CheckedCommand 'dotnet' @('build', 'YHAB.slnx', '--configuration', $Configuration, '--no-restore', '--disable-build-servers')
        return
    }

    throw "dotnet build failed with exit code $($result.ExitCode)."
}

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
            $validationMutex = Enter-ValidationLock -RepoRoot $repoRoot -TimeoutSeconds 60
            Invoke-CheckedCommand 'git' @('diff', '--check')
            Invoke-CheckedCommand 'dotnet' @('build-server', 'shutdown')
            $formatArguments = @('format', 'YHAB.slnx', '--severity', 'warn', '--verify-no-changes')
            $formatResult = Invoke-CapturedDotnet $formatArguments
            if ($formatResult.ExitCode -ne 0) {
                Write-Host $formatResult.Output
                if (Test-WidespreadRazorParserFailure $formatResult.Output) {
                    Write-Warning 'Widespread Razor parser failures detected during formatting. Cleaning, restoring, and rebuilding before retrying verification.'
                    Invoke-CheckedCommand 'dotnet' @('build-server', 'shutdown')
                    Invoke-CheckedCommand 'dotnet' @('clean', 'YHAB.slnx', '--configuration', $Configuration)
                    Invoke-CheckedCommand 'dotnet' @('restore', 'YHAB.slnx', '--force', '--no-cache')
                    $formatResult = Invoke-CapturedDotnet $formatArguments
                    if ($formatResult.ExitCode -ne 0) {
                        Write-Host $formatResult.Output
                        throw "dotnet format failed with exit code $($formatResult.ExitCode)."
                    }
                }
                else {
                    throw "dotnet format failed with exit code $($formatResult.ExitCode)."
                }
            }
            Invoke-SolutionBuild $Configuration

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
        if ($null -ne $validationMutex) {
            Exit-ValidationLock $validationMutex
        }
        Remove-Module ValidationLock -Force -ErrorAction SilentlyContinue
    }
}
catch {
    [Console]::Error.WriteLine("Validation failed ($Scope): $($_.Exception.Message)")
    exit 1
}
