$ErrorActionPreference = 'Stop'

$cases = @(
    @{ Paths = @('README.md'); Application = $false; Deploy = $false },
    @{
        Paths = @('tests/README.md', '.github/instructions/ui.instructions.md', 'build/agent-instructions.md', 'build/agent-hooks.md')
        Application = $false
        Deploy = $false
    },
    @{ Paths = @('.vscode/tasks.json', 'scripts/Invoke-Validation.ps1'); Application = $false; Deploy = $false },
    @{ Paths = @("README.md`n"); Application = $true; Deploy = $true },
    @{ Paths = @('src/YHAB.UI/Page.razor'); Application = $true; Deploy = $true },
    @{ Paths = @('src/YHAB.Client/Program.cs'); Application = $true; Deploy = $true },
    @{ Paths = @('src/YHAB/Features/Budgets/Index.razor.cs'); Application = $true; Deploy = $true },
    @{ Paths = @('src/YHAB/wwwroot/app.css'); Application = $true; Deploy = $true },
    @{ Paths = @('tests/YHAB.PlaywrightTests/BudgetTests.cs'); Application = $true; Deploy = $false },
    @{ Paths = @('src/YHAB/Features/Budgets/BudgetService.cs'); Application = $true; Deploy = $true },
    @{ Paths = @('src/YHAB/Program.cs'); Application = $true; Deploy = $true },
    @{ Paths = @('src/YHAB/YHAB.csproj'); Application = $true; Deploy = $true },
    @{ Paths = @('src/YHAB.SharedKernel/Money.cs'); Application = $true; Deploy = $true },
    @{ Paths = @('tests/YHAB.UnitTests/Example.cs'); Application = $true; Deploy = $false },
    @{ Paths = @('README.md', 'Directory.Packages.props'); Application = $true; Deploy = $true },
    @{ Paths = @('.github/workflows/deploy.yml'); Application = $true; Deploy = $true },
    @{ Paths = @('docs/possible-test-input.md'); Application = $true; Deploy = $true },
    @{ Paths = @('new-build-input'); Application = $true; Deploy = $true },
    @{ Paths = @('README.md', 'deleted.cs'); Application = $true; Deploy = $true },
    @{ Paths = @(); Application = $true; Deploy = $true }
)
# Removed hook paths are no longer tooling-only; each must select full validation on its own.
$cases += @(
    '.githooks/pre-commit', '.githooks/pre-push', '.github/hooks/documentation-review.json', '.github/hooks/validation.json',
    'scripts/Install-GitHooks.ps1', 'scripts/Invoke-DocumentationReviewHook.ps1', 'scripts/Test-DocumentationReviewHook.ps1',
    'scripts/Invoke-AgentValidationHook.ps1', 'scripts/Test-AgentValidationHook.ps1'
) | ForEach-Object { @{ Paths = @($_); Application = $true; Deploy = $true } }
foreach ($case in $cases) {
    $scope = & "$PSScriptRoot/Get-ValidationScope.ps1" -Paths $case.Paths
    if ($scope.application -ne $case.Application -or $scope.deploy -ne $case.Deploy) {
        throw "Wrong validation scope for $($case.Paths -join ', ')"
    }
}
$scope = & "$PSScriptRoot/Get-ValidationScope.ps1" -Paths README.md -Full
if (!$scope.application -or !$scope.deploy) { throw 'Full validation must override path selection.' }
Write-Host "PASS: $($cases.Count + 1) validation-scope cases."
