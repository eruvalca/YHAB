$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$deploy = Get-Content -Raw -Path "$root/.github/workflows/deploy.yml"
$request = Get-Content -Raw -Path "$root/.github/workflows/browser-request.yml"
$nightly = Get-Content -Raw -Path "$root/.github/workflows/nightly-browser.yml"

function Assert-True([bool] $Condition, [string] $Message) {
    if (!$Condition) { throw $Message }
}

Assert-True ($deploy -notmatch '(?m)^  browser-(smoke|full|requested|gate):') 'The main workflow must not define browser jobs.'
Assert-True ($deploy -notmatch 'Browser validation gate|Browser smoke|Category=BrowserSmoke|run-full-browser|run-full-browser/action') 'The main workflow must not select or require browser validation.'
Assert-True ($deploy -match '(?m)^    needs: \[fast, tests, publish\]$') 'Main deployment must not depend on a browser job.'

Assert-True ($request -match 'types: \[labeled, synchronize, reopened, ready_for_review\]') 'Opt-in browser runs must support labels and subsequent commits.'
Assert-True ($request -match "contains\(github\.event\.pull_request\.labels\.\*\.name, 'run-full-browser'\)") 'The PR browser job must require the explicit opt-in label.'
Assert-True ($request -match '(?m)^      - uses: \./\.github/actions/run-full-browser$') 'The request workflow must reuse the full-browser action.'
Assert-True ($request -notmatch '(?m)^  (push|schedule):') 'The PR request workflow must not run on pushes or a schedule.'

Assert-True ($nightly -match "- cron: '17 6 \* \* \*'") 'Both suites must share the documented daily schedule.'
Assert-True ($nightly -match '(?m)^  browser-full:$' -and $nightly -match '(?m)^  performance:$') 'Nightly validation must include both suites.'
Assert-True ($nightly -match '--filter-not-trait Category=Performance' -or $nightly -match 'run-full-browser') 'Nightly browser validation must cover the non-performance suite.'
Assert-True ($nightly -match '--filter-trait Category=Performance') 'Nightly performance validation must select performance cases.'
Assert-True ($nightly -match 'issues: write') 'Nightly reporting must be able to create issues.'
Assert-True ($nightly -match 'Publish-NightlyValidationIssue\.ps1 -Suite Browser') 'Browser failures must be reported.'
Assert-True ($nightly -match 'Publish-NightlyValidationIssue\.ps1 -Suite Performance') 'Performance failures and regressions must be reported.'

Write-Host 'PASS: PR browser runs are opt-in and both nightly suites report failures.'
