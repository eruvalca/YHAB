#requires -Version 7.0
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
    & git -C $fixtureRoot init --quiet
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not initialize lock-test fixture as a Git repository.'
    }
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

    $subdirectory = Join-Path $fixtureRoot 'subdirectory'
    New-Item -ItemType Directory -Path $subdirectory -Force | Out-Null
    $rootLockPath = Get-ValidationLockPath $fixtureRoot
    Assert-Result 'Checkout subdirectories resolve to the same lock path' (
        $rootLockPath -eq (Get-ValidationLockPath $subdirectory))

    $validationScript = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Invoke-Validation.ps1') -Raw
    Assert-Result 'Validation build shuts down reusable build servers' (
        $validationScript -match "build-server', 'shutdown" -and $validationScript -match '--disable-build-servers')

    Write-Host "Validation lock checks: $passed passed, 0 failed."
}
finally {
    Remove-Module ValidationLock -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
