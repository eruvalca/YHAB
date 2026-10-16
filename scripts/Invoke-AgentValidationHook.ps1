#requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

if ($env:YHAB_AGENT_VALIDATE -ne '1') {
    '{}'
    exit 0
}

function Get-TextHash([string] $Text) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes($Text)))
}

function Get-GitText([string[]] $Arguments) {
    $output = & git -C $repoRoot @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw 'Git could not inspect the validation workspace.'
    }
    return $output -join "`n"
}

function Get-WorkspaceFingerprint {
    $head = & git -C $repoRoot rev-parse --verify HEAD 2>$null
    if ($LASTEXITCODE -ne 0) {
        $head = ''
    }
    $pathspec = @('--', '.', ':(exclude).github/agents/**')
    $paths = @(
        (Get-GitText (@('diff', '--name-only', '--no-renames', '-z') + $pathspec)).Split([char]0)
        (Get-GitText (@('diff', '--cached', '--name-only', '--no-renames', '-z') + $pathspec)).Split([char]0)
        (Get-GitText (@('ls-files', '--others', '--exclude-standard', '-z') + $pathspec)).Split([char]0)
    ) | Where-Object { $_ } | Sort-Object -Unique -CaseSensitive

    $parts = [Collections.Generic.List[string]]::new()
    $parts.Add([string] $head)
    $parts.Add((Get-GitText (@('diff', '--raw', '--no-abbrev', '--no-renames', '-z') + $pathspec)))
    $parts.Add((Get-GitText (@('diff', '--cached', '--raw', '--no-abbrev', '--no-renames', '-z') + $pathspec)))
    foreach ($path in $paths) {
        $fullPath = Join-Path $repoRoot $path
        $item = Get-Item -LiteralPath $fullPath -Force -ErrorAction SilentlyContinue
        $hash = if ($null -eq $item) {
            '<absent>'
        }
        elseif ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            Get-TextHash ([string] $item.LinkTarget)
        }
        elseif ($item -is [IO.FileInfo]) {
            (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash
        }
        else {
            '<directory>'
        }
        $parts.Add("$path`0$hash")
    }
    return Get-TextHash ($parts -join "`0")
}

function Save-State {
    $state | ConvertTo-Json -Compress | Set-Content -LiteralPath $statePath -Encoding utf8
}

$lock = $null
$state = @{}
$statePath = $null
$event = $null
try {
    $event = [Console]::In.ReadToEnd() | ConvertFrom-Json -AsHashtable
    if ($event -isnot [hashtable]) {
        throw 'Expected a JSON event object.'
    }
    if ($event.stop_hook_active) {
        '{}'
        exit 0
    }
    $isPrompt = $event.ContainsKey('prompt')
    $isStop = $event.ContainsKey('stopReason') -or $event.ContainsKey('transcriptPath')
    if (-not ($isPrompt -or $isStop)) {
        '{}'
        exit 0
    }
    if (-not $event.sessionId) {
        throw 'No sessionId: restart the agent or run Invoke-Validation.ps1 manually.'
    }

    $repoRoot = Split-Path $PSScriptRoot
    $stateDirectory = Join-Path $repoRoot 'artifacts/agent-hooks/validation'
    # Refuse to create state in a checkout that would accidentally track it.
    & git -C $repoRoot check-ignore --quiet -- 'artifacts/agent-hooks/validation/state.json'
    if ($LASTEXITCODE -ne 0) {
        throw 'artifacts/agent-hooks/validation must be ignored by Git.'
    }
    New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
    $key = Get-TextHash ([string] $event.sessionId)
    $statePath = Join-Path $stateDirectory "$key.json"
    try {
        $lock = [IO.File]::Open((Join-Path $stateDirectory "$key.lock"),
            [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    }
    catch [IO.IOException] {
        [Console]::Error.WriteLine('YHAB validation is already handling this session; no duplicate run started.')
        '{}'
        exit 0
    }
    if (Test-Path -LiteralPath $statePath) {
        $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json -AsHashtable
    }
    if ($state -isnot [hashtable]) {
        throw 'Invalid validation state. Remove this session state and restart the agent.'
    }
    $fingerprint = Get-WorkspaceFingerprint
    if ($isPrompt) {
        if ($state.continuationPromptHash -ne (Get-TextHash ([string] $event.prompt))) {
            $state.baselineFingerprint = $fingerprint
            Save-State
        }
        '{}'
        exit 0
    }
    if (-not $state.baselineFingerprint) {
        [Console]::Error.WriteLine('YHAB validation has no prompt baseline; restart with YHAB_AGENT_VALIDATE=1 or run Fast manually.')
        '{}'
        exit 0
    }
    if ($fingerprint -eq $state.attemptedFingerprint) {
        if ($state.result -ne 'passed') {
            [Console]::Error.WriteLine("YHAB validation previously $($state.result); unchanged workspace was not rerun. Run Fast manually. Log: $($state.logPath)")
        }
        '{}'
        exit 0
    }
    if ($fingerprint -eq $state.baselineFingerprint) {
        '{}'
        exit 0
    }

    # Record before launching: interruption or an outer hook timeout must not
    # cause the next Stop event to repeat the same expensive run.
    $state.attemptedFingerprint = $fingerprint
    $state.result = 'interrupted'
    $state.logPath = "artifacts/agent-hooks/validation/$key.log"
    Save-State

    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = (Get-Command pwsh -CommandType Application | Select-Object -First 1).Source
    $start.WorkingDirectory = $repoRoot
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @('-NoProfile', '-NonInteractive', '-File',
            (Join-Path $PSScriptRoot 'Invoke-Validation.ps1'), '-Scope', 'Fast')) {
        $start.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        if (-not $process.Start()) {
            throw 'Could not launch Fast validation.'
        }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $timedOut = -not $process.WaitForExit(600000)
        if ($timedOut) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        $exitCode = $process.ExitCode
        [IO.File]::WriteAllText((Join-Path $repoRoot $state.logPath),
            $stdout.GetAwaiter().GetResult() + "`n[stderr]`n" + $stderr.GetAwaiter().GetResult())
    }
    finally {
        $process.Dispose()
    }

    $state.result = 'failed'
    $failure = if ($timedOut) {
        'Fast validation timed out after 600 seconds.'
    }
    elseif ($exitCode -ne 0) {
        "Fast validation failed with exit code $exitCode."
    }
    elseif ((Get-WorkspaceFingerprint) -ne $fingerprint) {
        'The workspace changed during Fast validation; its result does not validate the latest edits.'
    }
    else {
        $state.result = 'passed'
        ''
    }
    if ($failure) {
        $reason = "$failure Inspect $($state.logPath), fix task-related issues if authorized, and run pwsh ./scripts/Invoke-Validation.ps1 -Scope Fast manually after edits. Report any blocker truthfully; do not claim validation passed. This hook blocks once per attempted workspace and does not authorize commits or unrelated changes."
        $state.continuationPromptHash = Get-TextHash $reason
        Save-State
        [Console]::Error.WriteLine("YHAB validation failed. $failure Log: $($state.logPath)")
        @{ decision = 'block'; reason = $reason } | ConvertTo-Json -Compress
    }
    else {
        Save-State
        [Console]::Error.WriteLine("YHAB Fast validation passed. Log: $($state.logPath)")
        '{}'
    }
}
catch {
    $message = "YHAB opt-in validation hook failed: $($_.Exception.Message) Run pwsh ./scripts/Invoke-Validation.ps1 -Scope Fast manually and report the result."
    [Console]::Error.WriteLine($message)
    if ($statePath -and $lock -and -not $state.hookFailureReported -and -not $isPrompt) {
        $state.hookFailureReported = $true
        try { Save-State } catch { [Console]::Error.WriteLine('Could not persist validation failure state.') }
        @{ decision = 'block'; reason = $message } | ConvertTo-Json -Compress
    }
    else {
        '{}'
    }
}
finally {
    if ($null -ne $lock) {
        $lock.Dispose()
    }
}
