#requires -Version 7.0
[CmdletBinding()]
param([switch] $BuildSmoke)

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

function Invoke-RecoveryFixture([string] $Mode) {
    $recoveryRoot = Join-Path $fixtureRoot "recovery-$Mode"
    New-Item -ItemType Directory -Path (Join-Path $recoveryRoot 'scripts'),
        (Join-Path $recoveryRoot 'bin') -Force | Out-Null
    & git -C $recoveryRoot init --quiet
    if ($LASTEXITCODE -ne 0) {
        throw "Could not initialize $Mode recovery fixture."
    }

    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Invoke-Validation.ps1') -Destination (Join-Path $recoveryRoot 'scripts/Invoke-Validation.ps1')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ValidationLock.psm1') -Destination (Join-Path $recoveryRoot 'scripts/ValidationLock.psm1')
    [IO.File]::WriteAllText((Join-Path $recoveryRoot 'YHAB.slnx'), '<Solution />')
    [IO.File]::WriteAllText((Join-Path $recoveryRoot 'source.txt'), 'Fixture source')

    $fakeScript = Join-Path $recoveryRoot 'bin/dotnet-fake.ps1'
    [IO.File]::WriteAllText($fakeScript, @'
param([Parameter(ValueFromRemainingArguments = $true)][string[]] $Arguments)
$root = [Environment]::GetEnvironmentVariable('FAKE_REPO_ROOT')
$log = Join-Path $root 'dotnet-args.log'
[IO.File]::AppendAllText($log, ($Arguments -join ' ') + [Environment]::NewLine)
$text = $Arguments -join ' '

if ($Arguments.Count -gt 0 -and $Arguments[0] -eq 'format') {
    $count = @(Get-Content -LiteralPath $log | Where-Object { $_ -like 'format *' }).Count
    if ([Environment]::GetEnvironmentVariable('FAKE_FORMAT_RAZOR') -eq '1' -and $count -eq 1) {
        [Console]::Error.WriteLine('src/YHAB.UI/Features/Account.razor(1,1): error RZ1021')
        [Console]::Error.WriteLine('src/YHAB.UI/Features/Budget.razor(2,2): error RZ1009')
        [Console]::Error.WriteLine('src/YHAB.UI/Features/Plan.razor(3,3): error RZ9981')
        exit 1
    }
    [Console]::Out.WriteLine('fake formatter output')
    exit 0
}

if ($Arguments.Count -gt 0 -and $Arguments[0] -eq 'build') {
    $count = @(Get-Content -LiteralPath $log | Where-Object { $_ -like 'build YHAB.slnx*' }).Count
    if ([Environment]::GetEnvironmentVariable('FAKE_BUILD_RAZOR') -eq '1' -and $count -eq 1) {
        [Console]::Error.WriteLine('src/YHAB.UI/Features/Account.razor(1,1): error RZ1021')
        [Console]::Error.WriteLine('src/YHAB.UI/Features/Budget.razor(2,2): error RZ1009')
        [Console]::Error.WriteLine('src/YHAB.UI/Features/Plan.razor(3,3): error RZ9981')
        exit 1
    }
    if ([Environment]::GetEnvironmentVariable('FAKE_BUILD_ORDINARY') -eq '1' -and $count -eq 1) {
        [Console]::Error.WriteLine('src/YHAB.UI/Features/Account.razor.cs(1,1): error CS0103')
        exit 1
    }
    [Console]::Out.WriteLine('fake build output')
    exit 0
}

exit 0
'@)

    if ($IsWindows) {
        [IO.File]::WriteAllText((Join-Path $recoveryRoot 'bin/dotnet.cmd'), "@echo off`r`npwsh -NoProfile -NonInteractive -File `"%FAKE_REPO_ROOT%\bin\dotnet-fake.ps1`" %*`r`nexit /b %ERRORLEVEL%`r`n")
    }
    else {
        $shim = Join-Path $recoveryRoot 'bin/dotnet'
        [IO.File]::WriteAllText($shim, "#!/bin/sh`nexec pwsh -NoProfile -NonInteractive -File `"`$FAKE_REPO_ROOT/bin/dotnet-fake.ps1`" `"`$@`"`n")
        & chmod +x $shim
    }

    & git -C $recoveryRoot add --all
    & git -C $recoveryRoot -c user.name='Validation Fixture' -c user.email='fixture@example.invalid' commit --quiet -m Fixture

    $oldFakeRoot = [Environment]::GetEnvironmentVariable('FAKE_REPO_ROOT')
    $oldFormat = [Environment]::GetEnvironmentVariable('FAKE_FORMAT_RAZOR')
    $oldBuildRazor = [Environment]::GetEnvironmentVariable('FAKE_BUILD_RAZOR')
    $oldBuildOrdinary = [Environment]::GetEnvironmentVariable('FAKE_BUILD_ORDINARY')
    $oldPath = $env:PATH
    try {
        [Environment]::SetEnvironmentVariable('FAKE_REPO_ROOT', $recoveryRoot)
        [Environment]::SetEnvironmentVariable('FAKE_FORMAT_RAZOR', $(if ($Mode -eq 'format') { '1' } else { '0' }))
        [Environment]::SetEnvironmentVariable('FAKE_BUILD_RAZOR', $(if ($Mode -eq 'build') { '1' } else { '0' }))
        [Environment]::SetEnvironmentVariable('FAKE_BUILD_ORDINARY', $(if ($Mode -eq 'ordinary') { '1' } else { '0' }))
        $env:PATH = (Join-Path $recoveryRoot 'bin') + [IO.Path]::PathSeparator + $env:PATH
        $output = & $pwsh -NoProfile -NonInteractive -File (Join-Path $recoveryRoot 'scripts/Invoke-Validation.ps1') -Scope Fast 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        [Environment]::SetEnvironmentVariable('FAKE_REPO_ROOT', $oldFakeRoot)
        [Environment]::SetEnvironmentVariable('FAKE_FORMAT_RAZOR', $oldFormat)
        [Environment]::SetEnvironmentVariable('FAKE_BUILD_RAZOR', $oldBuildRazor)
        [Environment]::SetEnvironmentVariable('FAKE_BUILD_ORDINARY', $oldBuildOrdinary)
        $env:PATH = $oldPath
    }

    $argsText = if (Test-Path -LiteralPath (Join-Path $recoveryRoot 'dotnet-args.log')) {
        Get-Content -LiteralPath (Join-Path $recoveryRoot 'dotnet-args.log') -Raw
    } else { '' }
    return [pscustomobject] @{
        ExitCode = $exitCode
        Output = ($output | Out-String)
        Args = $argsText
    }
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
    $rootMutexName = Get-ValidationMutexName $fixtureRoot
    Assert-Result 'Checkout aliases resolve to the same mutex name' (
        $rootMutexName -eq (Get-ValidationMutexName $subdirectory))

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

    $formatFixture = Invoke-RecoveryFixture 'format'
    Assert-Result 'Formatter recovery retries once and streams retained output' (
        $formatFixture.ExitCode -eq 0 -and
        $formatFixture.Output -match 'fake formatter output' -and
        @($formatFixture.Args -split "`r?`n" | Where-Object { $_ -like 'format *' }).Count -eq 2 -and
        $formatFixture.Args -match 'clean YHAB.slnx' -and
        $formatFixture.Args -match 'restore YHAB.slnx')

    $buildFixture = Invoke-RecoveryFixture 'build'
    Assert-Result 'Build recovery retries once after widespread Razor parser failures' (
        $buildFixture.ExitCode -eq 0 -and
        @($buildFixture.Args -split "`r?`n" | Where-Object { $_ -like 'build YHAB.slnx*' }).Count -eq 2 -and
        $buildFixture.Args -match 'clean YHAB.slnx' -and
        $buildFixture.Args -match 'restore YHAB.slnx')

    $ordinaryFixture = Invoke-RecoveryFixture 'ordinary'
    Assert-Result 'Ordinary build failures do not trigger clean and restore recovery' (
        $ordinaryFixture.ExitCode -ne 0 -and
        @($ordinaryFixture.Args -split "`r?`n" | Where-Object { $_ -like 'build YHAB.slnx*' }).Count -eq 1 -and
        $ordinaryFixture.Args -notmatch 'clean YHAB.slnx' -and
        $ordinaryFixture.Args -notmatch 'restore YHAB.slnx')

    if ($BuildSmoke) {
        $repoLock = Enter-ValidationLock -RepoRoot $repoRoot -TimeoutSeconds 60
        try {
            Push-Location $repoRoot
            try {
                & dotnet build YHAB.slnx --configuration Debug --disable-build-servers
                Assert-Result 'Solution build accepts build-server isolation while holding the checkout lock' ($LASTEXITCODE -eq 0)
            }
            finally {
                Pop-Location
            }
        }
        finally {
            Exit-ValidationLock $repoLock
        }
    }

    Write-Host "Validation lock checks: $passed passed, 0 failed, 0 skipped."
}
finally {
    Remove-Module ValidationLock -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
