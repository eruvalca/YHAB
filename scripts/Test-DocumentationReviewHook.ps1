#requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot
$hookScript = Join-Path $PSScriptRoot 'Invoke-DocumentationReviewHook.ps1'
$fixtureRoot = Join-Path $repoRoot ('artifacts/documentation-hook-tests/' + [guid]::NewGuid().ToString('N') + '/workspace with spaces')
New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
$passed = 0

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

function Assert-Result([string] $Name, [bool] $Condition) {
    if (-not $Condition) {
        throw "FAIL: $Name"
    }
    $script:passed++
    Write-Host "PASS: $Name"
}

# Copilot command hooks send an event-shaped payload with no event name.
function New-CopilotPayload([string] $SessionId, [hashtable] $Fields) {
    $payload = @{ sessionId = $SessionId; timestamp = 1791957547163; cwd = $fixtureRoot }
    foreach ($key in $Fields.Keys) {
        $payload[$key] = $Fields[$key]
    }
    return $payload | ConvertTo-Json -Compress
}

function Invoke-CopilotHook([string] $PayloadJson) {
    $output = $PayloadJson | & pwsh -NoProfile -NonInteractive -File $hookScript
    if ($LASTEXITCODE -ne 0) {
        throw "Hook exited $LASTEXITCODE."
    }
    return ($output -join "`n") | ConvertFrom-Json -AsHashtable
}

function Invoke-ManifestCommandHook([string] $Command, [string] $PayloadJson, [string] $Shell) {
    $output = if ($Shell -eq 'cmd') {
        $PayloadJson | & $env:ComSpec /d /s /c $Command
    }
    else {
        $PayloadJson | & $Shell -NoProfile -NonInteractive -Command $Command
    }
    return @{ ExitCode = $LASTEXITCODE; Output = $output -join "`n" }
}

# A user prompt event for one session. Copilot has no turn id, so state is
# scoped to the session and each case below uses its own session.
function Start-Turn([string] $Session, [string] $Prompt = 'Copilot review turn') {
    return Invoke-CopilotHook (New-CopilotPayload $Session @{ prompt = $Prompt })
}

# A finishing event. Copilot supplies a transcript path rather than the final
# message, so acknowledgement cases write their own session log.
function Stop-Turn([string] $Session, [string] $TranscriptPath = '') {
    $fields = @{ stopReason = 'end_turn'; stop_hook_active = $false }
    if ($TranscriptPath) {
        $fields.transcriptPath = $TranscriptPath
    }
    return Invoke-CopilotHook (New-CopilotPayload $Session $fields)
}

function New-Transcript([string] $Name, [string] $Content) {
    $path = Join-Path $fixtureRoot $Name
    $record = @{ type = 'assistant.message'; data = @{ content = $Content } }
    [IO.File]::WriteAllText($path, ($record | ConvertTo-Json -Depth 5 -Compress) + "`n")
    return $path
}

# All mutations and commits below belong to this isolated, ignored test repo.
Invoke-FixtureGit @('init', '--quiet')
Invoke-FixtureGit @('config', 'user.name', 'Hook fixture')
Invoke-FixtureGit @('config', 'user.email', 'hook-fixture@example.invalid')
Invoke-FixtureGit @('config', 'commit.gpgsign', 'false')
Invoke-FixtureGit @('config', 'core.hooksPath', '.disabled-hooks')
Set-FixtureFile '.gitignore' "artifacts/`n"
Set-FixtureFile 'README.md' "# Fixture`n"

$result = Start-Turn 'unborn'
Assert-Result 'Prompt event injects the pre-commit review instructions' (
    $result.additionalContext -match 'Before any authorized commit' -and
    $result.additionalContext -match 'copilot-instructions\.md')
Assert-Result 'Unchanged untracked files do not trigger a finishing pass' (
    (Stop-Turn 'unborn').Count -eq 0)
Set-FixtureFile 'new file.md' "# New document`n"
Assert-Result 'New untracked file triggers a review' (
    (Stop-Turn 'unborn').decision -eq 'block')
Assert-Result 'Repeated Stop without an acknowledgement keeps requesting the review' (
    (Stop-Turn 'unborn').decision -eq 'block')

Invoke-FixtureGit @('add', '--all')
Invoke-FixtureGit @('commit', '--quiet', '-m', 'Fixture baseline')
Start-Turn 'clean' | Out-Null
Assert-Result 'Read-only turn in a clean repository stays quiet' (
    (Stop-Turn 'clean').Count -eq 0)

Set-FixtureFile 'README.md' "# Existing dirty edit`n"
Start-Turn 'dirty' | Out-Null
Assert-Result 'Pre-existing dirty work alone stays quiet' (
    (Stop-Turn 'dirty').Count -eq 0)
Set-FixtureFile 'README.md' "# Changed again in the turn`n"
$result = Stop-Turn 'dirty'
Assert-Result 'Further edits to the same dirty file trigger review' (
    $result.decision -eq 'block' -and $result.reason -match 'README.md')

Start-Turn 'staging' | Out-Null
Invoke-FixtureGit @('add', 'README.md')
Assert-Result 'Index-only change is detected' (
    (Stop-Turn 'staging').decision -eq 'block')
Start-Turn 'partial' | Out-Null
Set-FixtureFile 'README.md' "# Unstaged portion of an already-staged file`n"
Assert-Result 'Partial staging does not hide worktree edits' (
    (Stop-Turn 'partial').decision -eq 'block')

Start-Turn 'commit' | Out-Null
Invoke-FixtureGit @('add', 'README.md')
Invoke-FixtureGit @('commit', '--quiet', '-m', 'Fixture change')
Assert-Result 'A commit during the turn still triggers review' (
    (Stop-Turn 'commit').decision -eq 'block')

Start-Turn 'delete' | Out-Null
Remove-Item -LiteralPath (Join-Path $fixtureRoot 'new file.md')
Assert-Result 'Deleted files trigger review' (
    (Stop-Turn 'delete').decision -eq 'block')

Start-Turn 'guard' | Out-Null
Set-FixtureFile 'source.cs' 'class Fixture;'
Assert-Result 'stop_hook_active guard prevents continuation loops' (
    (Invoke-CopilotHook (New-CopilotPayload 'guard' @{ stopReason = 'end_turn'; stop_hook_active = $true })).Count -eq 0)
Assert-Result 'A different session cannot consume the baseline' (
    (Stop-Turn 'other-session').Count -eq 0)
Assert-Result 'A missing baseline stays quiet' (
    (Stop-Turn 'missing').Count -eq 0)

# Replay the final-response wording that caused the redundant continuation.
# Each case changes the workspace after its own baseline, so a quiet result
# proves that the acknowledgement (not an unchanged workspace) suppressed it.
$completedMessages = @(
    "Fixed the dropdown rendering and alignment issues.`n`nDocumentation reviewed and updated. The findings and test report records results and limitations."
    'Documentation review: complete. Updated the hook workflow documentation.'
    'Documentation review: complete. No documentation changes were needed.'
    'Documentation review is complete. .github/copilot-instructions.md remains accurate.'
    'Documentation review is already complete. No further edits were needed.'
    'Documentation reviewed; existing setup guidance remains accurate.'
    '**Documentation review:** complete. Updated affected guidance.'
    '- Documentation review: complete. Reviewed README.md and feature docs.'
    @'
```text
Documentation review: complete.
```

Documentation reviewed and updated.
'@
    "~~~text`nExample only.`n~~~`nDocumentation review: complete. Docs unchanged."
)
$case = 0
foreach ($message in $completedMessages) {
    $session = "completed-$case"
    Start-Turn $session | Out-Null
    Set-FixtureFile 'source.cs' "// Completed review fixture $case"
    $transcript = New-Transcript "completed-$case.jsonl" $message
    Assert-Result "Completed review $case does not request a redundant continuation" (
        (Stop-Turn $session $transcript).Count -eq 0)
    $case++
}

Assert-Result 'Repeated Stop remembers the reviewed workspace without response text' (
    (Stop-Turn $session).Count -eq 0)
Start-Turn $session | Out-Null
Assert-Result 'Duplicate prompt preserves the completed review acknowledgement' (
    (Stop-Turn $session $transcript).Count -eq 0)
Set-FixtureFile 'source.cs' '// Changed after the acknowledged review'
$finalTranscript = New-Transcript 'completed-final.jsonl' 'Made another change.'
Assert-Result 'Further workspace changes invalidate the completed review acknowledgement' (
    (Stop-Turn $session $finalTranscript).decision -eq 'block')

$incompleteMessages = @(
    ''
    'Implementation complete.'
    'Documentation review: pending.'
    'Documentation review: incomplete.'
    'Documentation review: not complete.'
    'Documentation review is not complete.'
    'Documentation review: complete except for feature docs.'
    'Documentation review: complete?'
    'I will report Documentation review: complete. after checking the docs.'
    '> Documentation reviewed and updated.'
    '    Documentation review: complete.'
    '"Documentation reviewed and updated." is an example.'
    'Documentation review: **not** complete.'
    @'
```text
Documentation review: complete.
```
Review still pending.
'@
    @'
~~~~text
~~~
Documentation review: complete.
~~~~
'@
    @'
```text
~~~
Documentation review: complete.
```
'@
)
$case = 0
foreach ($message in $incompleteMessages) {
    $session = "incomplete-$case"
    Start-Turn $session | Out-Null
    Set-FixtureFile 'source.cs' "// Missing review fixture $case"
    $transcript = New-Transcript "incomplete-$case.jsonl" $message
    Assert-Result "Missing or incomplete review $case still requests a finishing review" (
        (Stop-Turn $session $transcript).decision -eq 'block')
    $case++
}

# Only the transcript the runtime supplies can acknowledge the review; text in
# the prompt itself must not.
$promptPhraseSession = 'prompt-is-not-outcome'
Start-Turn $promptPhraseSession 'Documentation review: complete.' | Out-Null
Set-FixtureFile 'source.cs' '// Only the transcript can acknowledge the review'
Assert-Result 'A completion phrase in the prompt text cannot acknowledge review' (
    (Stop-Turn $promptPhraseSession).decision -eq 'block')

$stateText = Get-ChildItem -LiteralPath (Join-Path $fixtureRoot 'artifacts/agent-hooks/documentation') -Filter '*.json' |
    ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }
Assert-Result 'Hook state never persists the final message or its outcome text' (
    ($stateText -join "`n") -notmatch 'Documentation review|remains accurate|transcriptPath')

# The runtime replays a block reason as a new prompt; it must not reset the
# baseline, or the review would compare the workspace against itself.
$copilotSession = 'copilot-session'
Start-Turn $copilotSession 'Initial Copilot turn' | Out-Null
Set-FixtureFile 'copilot.cs' 'class CopilotFixture;'
$blockedResult = Stop-Turn $copilotSession
Assert-Result 'agentStop requests a review after an edit' ($blockedResult.decision -eq 'block')
$continuationResult = Invoke-CopilotHook (New-CopilotPayload $copilotSession @{ prompt = [string] $blockedResult.reason })
Assert-Result 'Block reason arrives as a prompt event and still injects the reminder' (
    [bool] $continuationResult.additionalContext)
Assert-Result 'Continuation prompt preserves the original baseline' (
    (Stop-Turn $copilotSession).decision -eq 'block')

$transcriptPath = New-Transcript 'copilot-transcript.jsonl' 'Fixed the rendering issue.'
$pendingSession = 'copilot-pending'
Start-Turn $pendingSession | Out-Null
Set-FixtureFile 'copilot-pending.cs' 'class CopilotPending;'
Assert-Result 'Transcript without an acknowledgement still requests review' (
    (Stop-Turn $pendingSession $transcriptPath).decision -eq 'block')

$ackSession = 'copilot-ack'
Start-Turn $ackSession | Out-Null
Set-FixtureFile 'copilot-ack.cs' 'class CopilotAcknowledged;'
$ackTranscript = New-Transcript 'copilot-ack.jsonl' 'Documentation review: complete. No documentation changes were needed.'
Assert-Result 'Transcript acknowledgement suppresses the finishing pass' (
    (Stop-Turn $ackSession $ackTranscript).Count -eq 0)

# An unknown or unrelated payload shape must stay quiet rather than request work.
Assert-Result 'An unrecognized payload shape stays quiet' (
    (Invoke-CopilotHook (@{ sessionId = 'unknown'; cwd = $fixtureRoot } | ConvertTo-Json -Compress)).Count -eq 0)

# Malformed input and a missing repository fail open at exit code 0.
$invalidJson = '{invalid' | & pwsh -NoProfile -NonInteractive -File $hookScript
Assert-Result 'Malformed input fails open at exit code zero' (
    $LASTEXITCODE -eq 0 -and ($invalidJson -join "`n").Trim() -eq '{}')
$badRepoPayload = (@{ sessionId = 'no-repo'; prompt = 'Probe'; cwd = (Join-Path $fixtureRoot 'does-not-exist') } | ConvertTo-Json -Compress)
$result = $badRepoPayload | & pwsh -NoProfile -NonInteractive -File $hookScript
Assert-Result 'Missing repository still returns the prompt reminder' (
    $LASTEXITCODE -eq 0 -and (($result -join "`n") | ConvertFrom-Json -AsHashtable).additionalContext)

$copilotManifest = Get-Content -LiteralPath (Join-Path $repoRoot '.github/hooks/documentation-review.json') -Raw | ConvertFrom-Json
$copilotPromptHandler = $copilotManifest.hooks.userPromptSubmitted[0]
$copilotStopHandler = $copilotManifest.hooks.agentStop[0]
Assert-Result 'Copilot manifest registers only the two documentation handlers' (
    $copilotManifest.version -eq 1 -and
    @($copilotManifest.hooks.PSObject.Properties).Count -eq 2 -and
    @($copilotManifest.hooks.userPromptSubmitted).Count -eq 1 -and
    @($copilotManifest.hooks.agentStop).Count -eq 1 -and
    $copilotPromptHandler.type -eq 'command' -and $copilotStopHandler.type -eq 'command' -and
    $copilotPromptHandler.powershell -match 'Invoke-DocumentationReviewHook.ps1' -and
    $copilotPromptHandler.bash -match 'Invoke-DocumentationReviewHook.ps1' -and
    $copilotStopHandler.powershell -eq $copilotPromptHandler.powershell -and
    $copilotStopHandler.bash -eq $copilotPromptHandler.bash -and
    $copilotPromptHandler.timeoutSec -eq 15)

if ($IsWindows) {
    # Copilot resolves the same script from the Git root, so its command strings
    # need path-with-spaces launcher checks through both Windows shell families.
    # A cmd-only check misses PowerShell expansion of $variables inside the
    # nested quoted command.
    $scriptDirectory = Join-Path $fixtureRoot 'scripts'
    New-Item -ItemType Directory -Path $scriptDirectory -Force | Out-Null
    Copy-Item -LiteralPath $hookScript -Destination $scriptDirectory
    Push-Location $scriptDirectory
    try {
        foreach ($shell in @('pwsh', 'powershell', 'cmd')) {
            $session = "copilot-launcher-$shell"
            $promptJson = New-CopilotPayload $session @{ prompt = 'Copilot launcher prompt' }
            $execution = Invoke-ManifestCommandHook $copilotPromptHandler.powershell $promptJson $shell
            $result = $execution.Output | ConvertFrom-Json -AsHashtable
            Assert-Result "$shell prompt launcher resolves paths with spaces and preserves stdin" (
                $execution.ExitCode -eq 0 -and $result.additionalContext)

            Set-FixtureFile "copilot-launcher-$shell.md" 'A change after the Copilot prompt hook.'
            $stopJson = New-CopilotPayload $session @{ stopReason = 'end_turn'; stop_hook_active = $false }
            $execution = Invoke-ManifestCommandHook $copilotStopHandler.powershell $stopJson $shell
            Assert-Result "$shell agentStop launcher requests a review after an edit" (
                $execution.ExitCode -eq 0 -and ($execution.Output | ConvertFrom-Json -AsHashtable).decision -eq 'block')

            $execution = Invoke-ManifestCommandHook $copilotStopHandler.powershell $stopJson $shell
            $ackTranscript = New-Transcript "copilot-launcher-$shell-ack.jsonl" 'Documentation review: complete. No documentation changes were needed.'
            Set-FixtureFile "copilot-launcher-$shell.md" 'A second change, reviewed before the final response.'
            $ackJson = New-CopilotPayload $session @{ stopReason = 'end_turn'; stop_hook_active = $false; transcriptPath = $ackTranscript }
            $execution = Invoke-ManifestCommandHook $copilotStopHandler.powershell $ackJson $shell
            Assert-Result "$shell agentStop launcher accepts an acknowledged review" (
                $execution.ExitCode -eq 0 -and ($execution.Output | ConvertFrom-Json -AsHashtable).Count -eq 0)

            $stopJson = New-CopilotPayload $session @{ stopReason = 'end_turn'; stop_hook_active = $true }
            $execution = Invoke-ManifestCommandHook $copilotStopHandler.powershell $stopJson $shell
            Assert-Result "$shell stop_hook_active guard stays quiet" (
                $execution.ExitCode -eq 0 -and ($execution.Output | ConvertFrom-Json -AsHashtable).Count -eq 0)
        }
    }
    finally {
        Pop-Location
    }
}

Assert-Result 'Hook state is ignored by Git' (
    -not ((& git -C $fixtureRoot status --porcelain --untracked-files=all) -match 'artifacts/'))
Write-Host "Documentation hook checks: $passed passed, 0 failed, 0 skipped."
Write-Host "Fixture: $fixtureRoot"
