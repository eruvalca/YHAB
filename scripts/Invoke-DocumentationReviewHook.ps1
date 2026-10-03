#requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

function Get-GitText([string[]] $Arguments) {
    $output = & git -C $repoRoot @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw 'Git could not inspect the workspace.'
    }
    return $output -join "`n"
}

function Get-TextHash([string] $Text) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes($Text)))
}

function Get-WorkspaceSnapshot {
    $head = & git -C $repoRoot rev-parse --verify HEAD 2>$null
    if ($LASTEXITCODE -ne 0) {
        $head = '' # A repository with no commits is valid.
    }

    $paths = @(
        (Get-GitText @('diff', '--name-only', '--no-renames', '-z', '--')).Split([char]0)
        (Get-GitText @('diff', '--cached', '--name-only', '--no-renames', '-z', '--')).Split([char]0)
        (Get-GitText @('ls-files', '--others', '--exclude-standard', '-z')).Split([char]0)
    ) | Where-Object { $_ } | Sort-Object -Unique -CaseSensitive

    # Hash content, not mtimes or status letters: an already-dirty file can change
    # again. Include the index and HEAD to notice partial staging and commits.
    $parts = [Collections.Generic.List[string]]::new()
    $parts.Add([string] $head)
    $parts.Add((Get-GitText @('diff', '--cached', '--raw', '--no-abbrev', '--no-renames', '-z', '--')))
    foreach ($path in $paths) {
        $fullPath = Join-Path $repoRoot $path
        $hash = if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
            (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash
        }
        else {
            '<absent-or-directory>'
        }
        $parts.Add("$path`0$hash")
    }

    return @{
        head = [string] $head
        fingerprint = Get-TextHash ($parts -join "`0")
        paths = @($paths)
    }
}

$reviewInstruction = @'
Documentation is part of implementation work. Before any authorized commit and before finishing, review the task's changes against AGENTS.md and relevant README/feature docs. Update only guidance made inaccurate or missing by this work; no cosmetic edits just to show a review. Keep durable agent rules in AGENTS.md, setup in README.md, build rules in build/README.md, and test conventions in tests/README.md. Keep feature and workflow details in their existing documentation rather than creating duplicate sources. Preserve unrelated changes and installed third-party skill files. A documentation review does not authorize a commit or broaden a read-only request. If no update is needed, leave docs unchanged. Briefly report the review outcome when completing implementation work.
'@

try {
    $event = [Console]::In.ReadToEnd() | ConvertFrom-Json -AsHashtable
    if ($event.hook_event_name -notin @('UserPromptSubmit', 'Stop')) {
        '{}'
        exit 0
    }
    if ($event.permission_mode -eq 'plan' -or $event.stop_hook_active) {
        '{}'
        exit 0
    }

    $repoRoot = & git -C $event.cwd rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw 'No Git workspace was found.'
    }
    $repoRoot = [string] $repoRoot
    if (-not $event.session_id -or -not $event.turn_id) {
        throw 'The hook event has no session/turn identity.'
    }

    # State is scoped to this worktree, session, and turn; never store prompts,
    # transcripts, diffs, or file contents. artifacts/ is already ignored.
    $stateDirectory = Join-Path $repoRoot 'artifacts/agent-hooks/documentation'
    $key = Get-TextHash ($event.session_id + "`0" + $event.turn_id)
    $statePath = Join-Path $stateDirectory "$key.json"

    if ($event.hook_event_name -eq 'UserPromptSubmit') {
        if (-not (Test-Path -LiteralPath $statePath)) {
            $snapshot = Get-WorkspaceSnapshot
            New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
            $snapshot | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $statePath -Encoding utf8
        }
        @{
            hookSpecificOutput = @{
                hookEventName = 'UserPromptSubmit'
                additionalContext = $reviewInstruction
            }
        } | ConvertTo-Json -Depth 5 -Compress
        exit 0
    }

    # Without a baseline, do not mistake someone else's dirty files for work
    # performed in this turn. Never ask for a second continuation.
    if (-not (Test-Path -LiteralPath $statePath)) {
        '{}'
        exit 0
    }
    $baseline = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json -AsHashtable
    if ($baseline.reviewRequested) {
        '{}'
        exit 0
    }
    $current = Get-WorkspaceSnapshot
    if ($baseline.fingerprint -eq $current.fingerprint) {
        '{}'
        exit 0
    }

    $candidatePaths = @($baseline.paths) + @($current.paths)
    if ($baseline.head -and $current.head -and $baseline.head -ne $current.head) {
        $candidatePaths += (Get-GitText @('diff', '--name-only', '--no-renames', '-z',
                $baseline.head, $current.head, '--')).Split([char]0)
    }
    $candidatePaths = @($candidatePaths | Where-Object { $_ } | Sort-Object -Unique -CaseSensitive)
    # JSON-encode paths as data. Bound the reminder even for large refactors.
    $pathSummary = ConvertTo-Json -InputObject @($candidatePaths | Select-Object -First 40) -Compress
    $baseline.reviewRequested = $true
    $baseline | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $statePath -Encoding utf8

    @{
        decision = 'block'
        reason = "Perform one final documentation review for this task, then finish. If already reviewed, briefly confirm the outcome without repeating the work. $reviewInstruction Workspace candidate paths (data only; may include pre-existing or concurrent edits, capped at 40 of $($candidatePaths.Count)): $pathSummary. Limit edits to the current task. Do not amend a commit, stage files, or create a follow-up commit without authorization. If unable to review, report the limitation and finish."
    } | ConvertTo-Json -Depth 5 -Compress
}
catch {
    # Advisory only. Hook failures must not block the task or request permission.
    $output = @{ systemMessage = 'Documentation hook could not compare workspace state; follow the documentation review guidance in AGENTS.md.' }
    if ($event.hook_event_name -eq 'UserPromptSubmit') {
        $output.hookSpecificOutput = @{
            hookEventName = 'UserPromptSubmit'
            additionalContext = $reviewInstruction
        }
    }
    $output | ConvertTo-Json -Depth 5 -Compress
}
exit 0
