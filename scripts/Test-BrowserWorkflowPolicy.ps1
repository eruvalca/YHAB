$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$deploy = Get-Content -Raw -Path "$root/.github/workflows/deploy.yml"
$request = Get-Content -Raw -Path "$root/.github/workflows/browser-request.yml"
$nightly = Get-Content -Raw -Path "$root/.github/workflows/nightly-browser.yml"

function Assert-True([bool] $Condition, [string] $Message) {
    if (!$Condition) { throw $Message }
}

# Returns the indented block under the first block-style key, so checks cannot match unrelated keys.
function Get-YamlBlock([string] $Text, [string] $Key) {
    $match = [regex]::Match($Text, "(?m)^([ \t]*)(['`"]?)$([regex]::Escape($Key))\2[ \t]*:[ \t]*(#[^\r\n]*)?(\r?\n|\z)")
    if (!$match.Success) { return '' }
    $lines = foreach ($line in ($Text.Substring($match.Index + $match.Length) -split '\r?\n')) {
        if ($line -match '^[ \t]*(#|$)') { $line; continue }
        if (($line.Length - $line.TrimStart().Length) -le $match.Groups[1].Length) { break }
        $line
    }
    $lines -join "`n"
}

Assert-True ($deploy -notmatch '(?m)^[ \t]{2,}browser-(smoke|full|requested|gate):') 'The main workflow must not define browser jobs.'
Assert-True ($deploy -notmatch 'Browser validation gate|Browser smoke|Category=BrowserSmoke|run-full-browser|run-full-browser/action') 'The main workflow must not select or require browser validation.'
Assert-True ($deploy -match '(?m)^[ \t]{4,}needs:\s*\[\s*fast\s*,\s*tests\s*,\s*publish\s*\]\s*$') 'Main deployment must not depend on a browser job.'

$triggers = Get-YamlBlock $request 'on'
$types = [regex]::Matches((Get-YamlBlock $triggers 'pull_request'), '(?m)^[ \t]*types[ \t]*:[ \t]*\[([^\]#]*)\][ \t]*(#[^\r\n]*)?\r?$')
$events = if ($types.Count -eq 1) {
    @($types[0].Groups[1].Value -split ',' | ForEach-Object { $_.Trim().Trim('''', '"') } | Where-Object { $_ } | Sort-Object)
}
Assert-True ($types.Count -eq 1 -and ($events -join ',') -ceq 'labeled,ready_for_review,reopened,synchronize') 'Opt-in browser runs must support labels and subsequent commits, and only those events.'
Assert-True ($request -match "contains\s*\(\s*github\.event\.pull_request\.labels\.\*\.name\s*,\s*['`"]run-full-browser['`"]\s*\)") 'The PR browser job must require the explicit opt-in label.'
Assert-True ($request -match "github\.event\.action\s*!=\s*['`"]labeled['`"]\s*\|\|\s*github\.event\.label\.name\s*==\s*['`"]run-full-browser['`"]") 'Unrelated label events must not start or replace a requested browser run.'
$job = [regex]::Match($request, '(?m)^([ \t]*)browser-full[ \t]*:')
$concurrency = @([regex]::Matches($request, '(?m)^([ \t]*)concurrency[ \t]*:'))
Assert-True (
    $job.Success -and $concurrency.Count -gt 0 -and
    @($concurrency | Where-Object { $_.Index -lt $job.Index -or $_.Groups[1].Length -le $job.Groups[1].Length }).Count -eq 0
) 'Only a job that passes the opt-in guard may acquire the browser concurrency group.'
Assert-True ($request -match '(?m)^[ \t]*(-[ \t]*)?uses[ \t]*:[ \t]*([''"]?)\./\.github/actions/run-full-browser\2[ \t]*(#[^\r\n]*)?\r?$') 'The request workflow must reuse the full-browser action.'
Assert-True ($triggers -cnotmatch '(?m)^[ \t]+(push|schedule)[ \t]*:') 'The PR request workflow must not run on pushes or a schedule.'

Assert-True ($nightly -match '(?m)^[ \t]*-[ \t]*cron[ \t]*:[ \t]*([''"]?)17[ \t]+6[ \t]+\*[ \t]+\*[ \t]+\*\1[ \t]*(#[^\r\n]*)?\r?$') 'Both suites must share the documented daily schedule.'
Assert-True ($nightly -match '(?m)^[ \t]{2,}browser-full\s*:' -and $nightly -match '(?m)^[ \t]{2,}performance\s*:') 'Nightly validation must include both suites.'
Assert-True ($nightly -match '--filter-not-trait Category=Performance' -or $nightly -match 'run-full-browser') 'Nightly browser validation must cover the non-performance suite.'
Assert-True ($nightly -match '--filter-trait Category=Performance') 'Nightly performance validation must select performance cases.'
Assert-True ($nightly -match 'issues:\s*write') 'Nightly reporting must be able to create issues.'
Assert-True ($nightly -match 'Publish-NightlyValidationIssue\.ps1\s+-Suite\s+[''"]?Browser\b') 'Browser failures must be reported.'
Assert-True ($nightly -match 'Publish-NightlyValidationIssue\.ps1\s+-Suite\s+[''"]?Performance\b') 'Performance failures and regressions must be reported.'

Write-Host 'PASS: Browser workflow policy checks.'
