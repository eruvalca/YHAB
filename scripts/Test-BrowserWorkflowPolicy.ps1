$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$deploy = Get-Content -Raw -Path "$root/.github/workflows/deploy.yml"
$request = Get-Content -Raw -Path "$root/.github/workflows/browser-request.yml"
$nightly = Get-Content -Raw -Path "$root/.github/workflows/nightly-browser.yml"

function Assert-True([bool] $Condition, [string] $Message) {
    if (!$Condition) { throw $Message }
}

Assert-True ($deploy -notmatch '(?m)^[ \t]{2,}browser-(smoke|full|requested|gate):') 'The main workflow must not define browser jobs.'
Assert-True ($deploy -notmatch 'Browser validation gate|Browser smoke|Category=BrowserSmoke|run-full-browser|run-full-browser/action') 'The main workflow must not select or require browser validation.'
Assert-True ($deploy -match '(?m)^[ \t]{4,}needs:\s*\[\s*fast\s*,\s*tests\s*,\s*publish\s*\]\s*$') 'Main deployment must not depend on a browser job.'

Assert-True (
    $request -match 'types:\s*\[[^\]]*\blabeled\b[^\]]*\]' -and
    $request -match 'types:\s*\[[^\]]*\bsynchronize\b[^\]]*\]' -and
    $request -match 'types:\s*\[[^\]]*\breopened\b[^\]]*\]' -and
    $request -match 'types:\s*\[[^\]]*\bready_for_review\b[^\]]*\]'
) 'Opt-in browser runs must support labels and subsequent commits.'
Assert-True ($request -match "contains\s*\(\s*github\.event\.pull_request\.labels\.\*\.name\s*,\s*['`"]run-full-browser['`"]\s*\)") 'The PR browser job must require the explicit opt-in label.'
Assert-True ($request -match "github\.event\.action\s*!=\s*['`"]labeled['`"]\s*\|\|\s*github\.event\.label\.name\s*==\s*['`"]run-full-browser['`"]") 'Unrelated label events must not start or replace a requested browser run.'
Assert-True ($request -notmatch '(?m)^concurrency\s*:' -and $request -match '(?m)^[ \t]{2,}concurrency\s*:') 'Only a job that passes the opt-in guard may acquire the browser concurrency group.'
Assert-True ($request -match '\buses:\s*\.\/\.github\/actions\/run-full-browser\b') 'The request workflow must reuse the full-browser action.'
Assert-True ($request -notmatch '(?m)^[ \t]{2}(push|schedule)\s*:') 'The PR request workflow must not run on pushes or a schedule.'

Assert-True ($nightly -match '(?m)^[ \t]{4,}-\s*cron:\s*[''"]?\d+\s+\d+\s+\*\s+\*\s+\*[''"]?\s*$') 'Nightly validation must have a daily schedule.'
Assert-True ($nightly -match '(?m)^[ \t]{2,}browser-full\s*:' -and $nightly -match '(?m)^[ \t]{2,}performance\s*:') 'Nightly validation must include both suites.'
Assert-True ($nightly -match '--filter-not-trait Category=Performance' -or $nightly -match 'run-full-browser') 'Nightly browser validation must cover the non-performance suite.'
Assert-True ($nightly -match '--filter-trait Category=Performance') 'Nightly performance validation must select performance cases.'
Assert-True ($nightly -match 'issues:\s*write') 'Nightly reporting must be able to create issues.'
Assert-True ($nightly -match 'Publish-NightlyValidationIssue\.ps1\s+-Suite\s+[''"]?Browser\b') 'Browser failures must be reported.'
Assert-True ($nightly -match 'Publish-NightlyValidationIssue\.ps1\s+-Suite\s+[''"]?Performance\b') 'Performance failures and regressions must be reported.'

Write-Host 'PASS: Browser workflow policy checks.'
