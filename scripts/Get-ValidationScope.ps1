[CmdletBinding()]
param(
    [string[]] $Paths = @(),
    [switch] $Full
)

$ErrorActionPreference = 'Stop'
# Only explicit, non-executable documentation and known agent tooling are cheap.
# Unknown paths (including future build inputs) deliberately select full validation.
$application = [bool] $Full
$deploy = [bool] $Full
$browser = if ($Full) { 'full' } else { 'none' }
foreach ($path in $Paths) {
    if ($path -cmatch '^(README\.md|build/(README|agent-hooks)\.md|tests/README\.md|\.github/copilot-instructions\.md|\.github/instructions/[^/]+\.instructions\.md)\z') {
        continue
    }
    if ($path -cmatch '^(\.github/hooks/[^/]+\.json|\.githooks/(pre-commit|pre-push)|\.vscode/tasks\.json|scripts/(Invoke-DocumentationReviewHook|Test-DocumentationReviewHook|Invoke-Validation|Install-GitHooks|Invoke-AgentValidationHook|Test-AgentValidationHook)\.ps1)\z') {
        continue
    }
    $application = $true
    if ($path -cnotmatch '^tests/') {
        $deploy = $true
    }
    if ($browser -eq 'full') {
        continue
    }
    if ($path -cmatch '^(src/YHAB\.(UI|Client)/|tests/YHAB\.PlaywrightTests/)' -or
        $path -cmatch '\.(razor|razor\.cs|css|scss|js|mjs|ts|html)\z') {
        $browser = 'full'
        continue
    }
    if ($path -cmatch '^(src/YHAB/.+\.cs\z|src/YHAB\.(AppHost|ServiceDefaults|SharedKernel)/|tests/)') {
        $browser = 'smoke'
        continue
    }
    $browser = 'full'
}
if ($Paths.Count -eq 0) {
    $application = $true
    $deploy = $true
    $browser = 'full'
}
[pscustomobject] @{
    application = $application
    deploy = $deploy
    browser = $browser
}
