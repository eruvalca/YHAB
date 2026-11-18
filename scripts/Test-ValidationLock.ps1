#requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path $PSScriptRoot
$modulePath = Join-Path $PSScriptRoot 'ValidationLock.psm1'
$passed = 0

function Assert-Result([string] $Name, [bool] $Condition) {
    if (-not $Condition) {
        throw "FAIL: $Name"
    }
    $script:passed++
    Write-Host "PASS: $Name"
}

$fixtureRoot = Join-Path $repoRoot ('artifacts/validation-lock-tests/' + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
    Import-Module -Name $modulePath -Force

    $holder = Enter-ValidationLock -RepoRoot $fixtureRoot -TimeoutSeconds 600
    $childScript = Join-Path $fixtureRoot 'check-lock.ps1'
    [IO.File]::WriteAllText($childScript, @'
param([string] $ModulePath, [string] $RepoRoot)
Import-Module -Name $ModulePath -Force
try {
    $lock = Enter-ValidationLock -RepoRoot $RepoRoot -TimeoutSeconds 0.5
    Exit-ValidationLock $lock
    exit 0
}
catch {
    exit 23
}
'@)
    $pwsh = (Get-Command pwsh -CommandType Application | Select-Object -First 1).Source
    & $pwsh -NoProfile -NonInteractive -File $childScript -ModulePath $modulePath -RepoRoot $fixtureRoot
    $blockedExit = $LASTEXITCODE
    Assert-Result 'Concurrent validation lock rejects a second process' ($blockedExit -eq 23)

    Exit-ValidationLock $holder
    & $pwsh -NoProfile -NonInteractive -File $childScript -ModulePath $modulePath -RepoRoot $fixtureRoot
    $freeExit = $LASTEXITCODE
    Assert-Result 'Validation lock succeeds after the holder releases it' ($freeExit -eq 0)

    $parserOutput = @'
src/YHAB.UI/Features/Account.razor(1,1): error RZ1021
src/YHAB.UI/Features/Budget.razor(2,2): error RZ1009
src/YHAB.UI/Features/Plan.razor(3,3): error RZ9981
'@
    Assert-Result 'Widespread Razor parser failures trigger recovery' (Test-WidespreadRazorParserFailure $parserOutput)
    Assert-Result 'A single Razor parser failure does not trigger broad recovery' (
        -not (Test-WidespreadRazorParserFailure 'src/YHAB.UI/Features/Account.razor(1,1): error RZ1021'))

    $validationScript = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Invoke-Validation.ps1') -Raw
    Assert-Result 'Validation build shuts down reusable build servers' (
        $validationScript -match "build-server', 'shutdown" -and $validationScript -match '--disable-build-servers')
    Assert-Result 'Validation build has scoped parser recovery' (
        $validationScript -match 'Test-WidespreadRazorParserFailure' -and
        $validationScript -match "--force', '--no-cache" -and
        $validationScript -match "'clean'")
    Assert-Result 'Validation formatting has scoped parser recovery' (
        $validationScript -match 'formatArguments' -and
        $validationScript -match 'formatResult' -and
        $validationScript -match 'Widespread Razor parser failures detected during formatting')

    Push-Location $repoRoot
    try {
        & dotnet build YHAB.slnx --configuration Debug --disable-build-servers
        Assert-Result 'Solution build accepts build-server isolation' ($LASTEXITCODE -eq 0)
    }
    finally {
        Pop-Location
    }

    Write-Host "Validation lock checks: $passed passed, 0 failed, 0 skipped."
}
finally {
    Remove-Module ValidationLock -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
