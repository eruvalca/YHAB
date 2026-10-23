$ErrorActionPreference = 'Stop'
$gate = "$PSScriptRoot/Confirm-BrowserValidation.ps1"

& $gate -Scope none -SmokeResult skipped -FullResult skipped
& $gate -Scope smoke -SmokeResult success -FullResult skipped
& $gate -Scope full -SmokeResult skipped -FullResult success

$failures = @(
    @{ Scope = 'smoke'; Smoke = 'failure'; Full = 'success' },
    @{ Scope = 'full'; Smoke = 'success'; Full = 'skipped' },
    @{ Scope = 'full'; Smoke = 'skipped'; Full = 'cancelled' }
)
foreach ($failure in $failures) {
    $failed = $false
    try {
        & $gate -Scope $failure.Scope -SmokeResult $failure.Smoke -FullResult $failure.Full -RunUrl 'https://example.invalid/run'
    }
    catch {
        $failed = $true
    }
    if (!$failed) { throw "The $($failure.Scope) gate accepted an unsuccessful selected run." }
}
Write-Host 'PASS: 6 browser-gate cases.'
