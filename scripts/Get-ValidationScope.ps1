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
foreach ($path in $Paths) {
    if ($path -cmatch '^(README\.md|build/(README|agent-hooks)\.md|tests/README\.md|\.github/copilot-instructions\.md|\.github/instructions/[^/]+\.instructions\.md)$') {
        continue
    }
    if ($path -cmatch '^(\.github/hooks/[^/]+\.json|\.githooks/(pre-commit|pre-push)|\.vscode/tasks\.json|scripts/(Invoke-DocumentationReviewHook|Test-DocumentationReviewHook|Invoke-Validation|Install-GitHooks|Invoke-AgentValidationHook|Test-LocalValidation)\.ps1)$') {
        continue
    }
    $application = $true
    if ($path -cnotmatch '^tests/') {
        $deploy = $true
    }
}
if ($Paths.Count -eq 0) {
    $application = $true
    $deploy = $true
}
[pscustomobject] @{
    application = $application
    deploy = $deploy
}
