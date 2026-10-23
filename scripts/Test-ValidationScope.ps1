$ErrorActionPreference = 'Stop'

$cases = @(
    @{ Paths = @('README.md'); Application = $false; Deploy = $false; Browser = 'none' },
    @{ Paths = @('tests/README.md', '.github/instructions/ui.instructions.md'); Application = $false; Deploy = $false; Browser = 'none' },
    @{ Paths = @('.github/hooks/documentation-review.json', 'scripts/Test-DocumentationReviewHook.ps1'); Application = $false; Deploy = $false; Browser = 'none' },
    @{ Paths = @('scripts/Test-AgentValidationHook.ps1'); Application = $false; Deploy = $false; Browser = 'none' },
    @{ Paths = @("README.md`n"); Application = $true; Deploy = $true; Browser = 'full' },
    @{ Paths = @('src/YHAB.UI/Page.razor'); Application = $true; Deploy = $true; Browser = 'full' },
    @{ Paths = @('src/YHAB.Client/Program.cs'); Application = $true; Deploy = $true; Browser = 'full' },
    @{ Paths = @('src/YHAB/Features/Budgets/Index.razor.cs'); Application = $true; Deploy = $true; Browser = 'full' },
    @{ Paths = @('src/YHAB/wwwroot/app.css'); Application = $true; Deploy = $true; Browser = 'full' },
    @{ Paths = @('tests/YHAB.PlaywrightTests/BudgetTests.cs'); Application = $true; Deploy = $false; Browser = 'full' },
    @{ Paths = @('src/YHAB/Features/Budgets/BudgetService.cs'); Application = $true; Deploy = $true; Browser = 'smoke' },
    @{ Paths = @('src/YHAB.SharedKernel/Money.cs'); Application = $true; Deploy = $true; Browser = 'smoke' },
    @{ Paths = @('tests/YHAB.UnitTests/Example.cs'); Application = $true; Deploy = $false; Browser = 'smoke' },
    @{ Paths = @('README.md', 'Directory.Packages.props'); Application = $true; Deploy = $true; Browser = 'full' },
    @{ Paths = @('.github/workflows/deploy.yml'); Application = $true; Deploy = $true; Browser = 'full' },
    @{ Paths = @('docs/possible-test-input.md'); Application = $true; Deploy = $true; Browser = 'full' },
    @{ Paths = @('new-build-input'); Application = $true; Deploy = $true; Browser = 'full' },
    @{ Paths = @('README.md', 'deleted.cs'); Application = $true; Deploy = $true; Browser = 'full' },
    @{ Paths = @(); Application = $true; Deploy = $true; Browser = 'full' }
)
foreach ($case in $cases) {
    $scope = & "$PSScriptRoot/Get-ValidationScope.ps1" -Paths $case.Paths
    if ($scope.application -ne $case.Application -or $scope.deploy -ne $case.Deploy -or $scope.browser -ne $case.Browser) {
        throw "Wrong validation scope for $($case.Paths -join ', ')"
    }
}
$scope = & "$PSScriptRoot/Get-ValidationScope.ps1" -Paths README.md -Full
if (!$scope.application -or !$scope.deploy -or $scope.browser -ne 'full') { throw 'Full validation must override path selection.' }
Write-Host "PASS: $($cases.Count + 1) validation-scope cases."
