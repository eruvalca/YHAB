#requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path $PSScriptRoot
$fixtureParent = Join-Path $repoRoot ('artifacts/validation-hook-tests/' + [guid]::NewGuid().ToString('N'))
$fixtureRoot = Join-Path $fixtureParent 'workspace with spaces'
$hookScript = Join-Path $fixtureRoot 'scripts/Invoke-AgentValidationHook.ps1'
$stateDirectory = Join-Path $fixtureRoot 'artifacts/agent-hooks/validation'
$counterPath = Join-Path $fixtureRoot 'artifacts/calls.txt'
$pwsh = (Get-Command pwsh -CommandType Application | Select-Object -First 1).Source
$originalOptIn = $env:YHAB_AGENT_VALIDATE
$passed = 0

function Assert-Result([string] $Name, [bool] $Condition) {
    if (-not $Condition) {
        throw "FAIL: $Name"
    }
    $script:passed++
    Write-Host "PASS: $Name"
}

function Invoke-FixtureGit([string[]] $Arguments) {
    $output = & git -C $fixtureRoot @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Fixture Git command failed: $($output -join "`n")"
    }
}

function Set-FixtureFile([string] $Path, [string] $Content) {
    $fullPath = Join-Path $fixtureRoot $Path
    New-Item -ItemType Directory -Path (Split-Path $fullPath) -Force | Out-Null
    [IO.File]::WriteAllText($fullPath, $Content)
}

function Get-CallCount {
    if (Test-Path -LiteralPath $counterPath) {
        return @(Get-Content -LiteralPath $counterPath).Count
    }
    return 0
}

function New-Payload([string] $Session, [hashtable] $Fields) {
    $payload = @{ sessionId = $Session; cwd = (Join-Path $fixtureRoot 'subdirectory') }
    foreach ($key in $Fields.Keys) {
        $payload[$key] = $Fields[$key]
    }
    return $payload | ConvertTo-Json -Compress
}

function Invoke-Hook([string] $Payload) {
    $errorPath = Join-Path $fixtureParent 'stderr.txt'
    $output = $Payload | & $pwsh -NoProfile -NonInteractive -File $hookScript 2>$errorPath
    Assert-Result 'Hook process exits zero and stdout is one JSON object' ($LASTEXITCODE -eq 0 -and @($output).Count -eq 1)
    return @{
        Response = ($output -join "`n") | ConvertFrom-Json -AsHashtable
        Error = Get-Content -LiteralPath $errorPath -Raw
    }
}

function Start-Turn([string] $Session, [string] $Prompt = 'Fixture user prompt') {
    return Invoke-Hook (New-Payload $Session @{ prompt = $Prompt })
}

function Stop-Turn([string] $Session, [bool] $Active = $false) {
    return Invoke-Hook (New-Payload $Session @{ stopReason = 'end_turn'; stop_hook_active = $Active })
}

try {
    New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'scripts'),
        (Join-Path $fixtureRoot 'subdirectory') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Invoke-AgentValidationHook.ps1') -Destination $hookScript
    Set-FixtureFile '.gitignore' "artifacts/`n"
    Set-FixtureFile 'source.txt' "Initial`n"
    Set-FixtureFile 'scripts/Invoke-Validation.ps1' @'
param([ValidateSet('Fast')][string] $Scope)
$root = Split-Path $PSScriptRoot
Add-Content -LiteralPath (Join-Path $root 'artifacts/calls.txt') -Value $Scope
Write-Output 'Fixture output must not corrupt hook JSON.'
[Console]::Error.WriteLine('Fixture stderr must go to the ignored log.')
if (Test-Path -LiteralPath (Join-Path $root 'artifacts/fail')) { exit 23 }
if (Test-Path -LiteralPath (Join-Path $root 'artifacts/mutate')) {
    Add-Content -LiteralPath (Join-Path $root 'source.txt') -Value 'Concurrent edit'
}
exit 0
'@
    Invoke-FixtureGit @('init', '--quiet')
    Invoke-FixtureGit @('config', 'core.hooksPath', '.disabled-hooks')
    Invoke-FixtureGit @('add', '--all')

    $env:YHAB_AGENT_VALIDATE = $null
    $result = Invoke-Hook 'not JSON'
    Assert-Result 'Disabled hook does not parse payload, create state, or validate' (
        $result.Response.Count -eq 0 -and -not (Test-Path $stateDirectory) -and (Get-CallCount) -eq 0)
    $env:YHAB_AGENT_VALIDATE = 'true'
    Start-Turn 'off' | Out-Null
    Assert-Result 'Only literal 1 opts in' (-not (Test-Path $stateDirectory))

    $env:YHAB_AGENT_VALIDATE = '1'
    $result = Invoke-Hook 'not JSON'
    Assert-Result 'Enabled malformed payload reports error without launching validation' (
        $result.Response.Count -eq 0 -and $result.Error -match 'hook failed' -and (Get-CallCount) -eq 0)
    $result = Stop-Turn 'missing'
    Assert-Result 'Missing prompt baseline warns without validating' (
        $result.Response.Count -eq 0 -and $result.Error -match 'no prompt baseline' -and (Get-CallCount) -eq 0)
    $result = Start-Turn 'main'
    Assert-Result 'Prompt records a baseline without injecting instructions' (
        $result.Response.Count -eq 0 -and (Get-CallCount) -eq 0)
    Stop-Turn 'main' | Out-Null
    Assert-Result 'Unchanged work in unborn repo does not validate' ((Get-CallCount) -eq 0)
    Set-FixtureFile 'source.txt' "Pre-existing dirty file`n"
    Start-Turn 'dirty' | Out-Null
    Stop-Turn 'dirty' | Out-Null
    Assert-Result 'Pre-existing dirty files alone do not validate' ((Get-CallCount) -eq 0)

    Stop-Turn 'main' $true | Out-Null
    Assert-Result 'stop_hook_active prevents expensive validation and a block' ((Get-CallCount) -eq 0)
    $result = Stop-Turn 'main'
    Assert-Result 'Changed work calls shared Fast and reports success only on stderr' (
        (Get-CallCount) -eq 1 -and $result.Response.Count -eq 0 -and $result.Error -match 'Fast validation passed')
    Stop-Turn 'main' | Out-Null
    Assert-Result 'Duplicate Stop for successful fingerprint does not rerun' ((Get-CallCount) -eq 1)
    Set-FixtureFile 'artifacts/ignored.txt' 'Ignored output'
    Stop-Turn 'main' | Out-Null
    Assert-Result 'Ignored outputs do not invalidate fingerprint' ((Get-CallCount) -eq 1)
    Invoke-FixtureGit @('add', 'source.txt')
    Stop-Turn 'main' | Out-Null
    Assert-Result 'Index-only staging change invalidates fingerprint' ((Get-CallCount) -eq 2)

    Set-FixtureFile 'source.txt' "Further edits`n"
    Set-FixtureFile 'artifacts/fail' ''
    $result = Stop-Turn 'main'
    $reason = $result.Response.reason
    Assert-Result 'Validation failure propagates as one block with original exit code and log path' (
        (Get-CallCount) -eq 3 -and $result.Response.decision -eq 'block' -and
        $reason -match 'exit code 23' -and $reason -match 'artifacts/agent-hooks/validation/' -and
        $result.Error -match 'validation failed')
    $result = Stop-Turn 'main'
    Assert-Result 'Unchanged failed fingerprint warns but neither reruns nor blocks again' (
        (Get-CallCount) -eq 3 -and $result.Response.Count -eq 0 -and $result.Error -match 'previously failed')
    Start-Turn 'main' $reason | Out-Null
    Stop-Turn 'main' | Out-Null
    Assert-Result 'Failure continuation prompt preserves one-pass behavior' ((Get-CallCount) -eq 3)
    Set-FixtureFile 'source.txt' "Fixed source`n"
    Remove-Item -LiteralPath (Join-Path $fixtureRoot 'artifacts/fail')
    Stop-Turn 'main' $true | Out-Null
    Assert-Result 'Continuation re-entrancy skips even changed work' ((Get-CallCount) -eq 3)
    Stop-Turn 'main' | Out-Null
    Assert-Result 'A later ordinary Stop can validate a changed fingerprint' ((Get-CallCount) -eq 4)
    Start-Turn 'main' | Out-Null
    Stop-Turn 'main' | Out-Null
    Assert-Result 'Next user turn with no changes does not rerun' ((Get-CallCount) -eq 4)
    Set-FixtureFile 'new file.txt' "New file`n"
    Stop-Turn 'main' | Out-Null
    Assert-Result 'Nonignored untracked file invalidates fingerprint' ((Get-CallCount) -eq 5)
    Remove-Item -LiteralPath (Join-Path $fixtureRoot 'new file.txt')
    Set-FixtureFile 'source.txt' "Deleted untracked and edited source`n"
    Set-FixtureFile 'artifacts/mutate' ''
    $result = Stop-Turn 'main'
    Assert-Result 'Concurrent source changes are not reported as a validated workspace' (
        (Get-CallCount) -eq 6 -and $result.Response.decision -eq 'block' -and
        $result.Response.reason -match 'workspace changed during')
    Remove-Item -LiteralPath (Join-Path $fixtureRoot 'artifacts/mutate')

    $states = @(Get-ChildItem -LiteralPath $stateDirectory -Filter '*.json')
    $stateText = ($states | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"
    Assert-Result 'Persistent state contains hashes/results, not prompts or command output' (
        $stateText -notmatch 'Fixture user prompt|Fixture output|Further edits|Fixed source')
    $logText = (Get-ChildItem -LiteralPath $stateDirectory -Filter '*.log' |
        ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"
    Assert-Result 'Child stdout and stderr are captured in ignored logs' (
        $logText -match 'Fixture output' -and $logText -match 'Fixture stderr')
    Invoke-FixtureGit @('check-ignore', '--quiet', 'artifacts/agent-hooks/validation/state.json')
    Assert-Result 'State is ignored by Git' ($LASTEXITCODE -eq 0)

    $mainKey = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes('main')))
    $lock = [IO.File]::Open((Join-Path $stateDirectory "$mainKey.lock"),
        [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try { $result = Stop-Turn 'main' } finally { $lock.Dispose() }
    Assert-Result 'Concurrent handler is not allowed to launch duplicate Fast' (
        (Get-CallCount) -eq 6 -and $result.Response.Count -eq 0 -and $result.Error -match 'already handling')

    $manifest = Get-Content -LiteralPath (Join-Path $repoRoot '.github/hooks/validation.json') -Raw | ConvertFrom-Json -AsHashtable
    Assert-Result 'Separate manifest registers baseline and Stop with bounded deadlines' (
        $manifest.hooks.Count -eq 2 -and $manifest.hooks.userPromptSubmitted[0].timeoutSec -eq 15 -and
        $manifest.hooks.agentStop[0].timeoutSec -eq 660)
    Push-Location (Join-Path $fixtureRoot 'subdirectory')
    try {
        $payload = New-Payload 'manifest' @{ prompt = 'Manifest baseline' }
        $output = $payload | & $pwsh -NoProfile -NonInteractive -Command $manifest.hooks.userPromptSubmitted[0].powershell
        Assert-Result 'PowerShell manifest resolves root from subdirectory with spaces' (
            $LASTEXITCODE -eq 0 -and ($output | ConvertFrom-Json -AsHashtable).Count -eq 0)
        if (-not $IsWindows) {
            $output = $payload | & sh -c $manifest.hooks.userPromptSubmitted[0].bash
            Assert-Result 'Shell manifest resolves root from subdirectory with spaces' (
                $LASTEXITCODE -eq 0 -and ($output | ConvertFrom-Json -AsHashtable).Count -eq 0)
        }
        else {
            $output = $payload | & $env:ComSpec /d /s /c $manifest.hooks.userPromptSubmitted[0].powershell
            Assert-Result 'Windows command prompt manifest resolves root with spaces' (
                $LASTEXITCODE -eq 0 -and ($output | ConvertFrom-Json -AsHashtable).Count -eq 0)
        }
        Set-FixtureFile 'source.txt' "Manifest finishing change`n"
        $payload = New-Payload 'manifest' @{ stopReason = 'end_turn'; stop_hook_active = $false }
        $output = $payload | & $pwsh -NoProfile -NonInteractive -Command $manifest.hooks.agentStop[0].powershell 2>(Join-Path $fixtureParent 'manifest-stderr.txt')
        Assert-Result 'Stop manifest runs shared Fast once with clean JSON output' (
            $LASTEXITCODE -eq 0 -and ($output | ConvertFrom-Json -AsHashtable).Count -eq 0 -and (Get-CallCount) -eq 7)
    }
    finally { Pop-Location }
    Write-Host "Validation hook checks: $passed passed, 0 failed, 0 skipped."
}
finally {
    $env:YHAB_AGENT_VALIDATE = $originalOptIn
    Remove-Item -LiteralPath $fixtureParent -Recurse -Force -ErrorAction SilentlyContinue
}
