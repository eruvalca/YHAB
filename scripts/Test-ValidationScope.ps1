$ErrorActionPreference = 'Stop'

$cases = @(
    @{ Paths = @('README.md'); Application = $false; Deploy = $false },
    @{ Paths = @('tests/README.md', '.github/instructions/ui.instructions.md'); Application = $false; Deploy = $false },
    @{ Paths = @('.github/hooks/documentation-review.json', 'scripts/Test-DocumentationReviewHook.ps1'); Application = $false; Deploy = $false },
    @{ Paths = @('scripts/Test-AgentValidationHook.ps1'); Application = $false; Deploy = $false },
    @{ Paths = @("README.md`n"); Application = $true; Deploy = $true },
    @{ Paths = @('src/YHAB.UI/Page.razor'); Application = $true; Deploy = $true },
    @{ Paths = @('tests/YHAB.UnitTests/Example.cs'); Application = $true; Deploy = $false },
    @{ Paths = @('README.md', 'Directory.Packages.props'); Application = $true; Deploy = $true },
    @{ Paths = @('.github/workflows/deploy.yml'); Application = $true; Deploy = $true },
    @{ Paths = @('docs/possible-test-input.md'); Application = $true; Deploy = $true },
    @{ Paths = @('new-build-input'); Application = $true; Deploy = $true },
    @{ Paths = @('README.md', 'deleted.cs'); Application = $true; Deploy = $true },
    @{ Paths = @(); Application = $true; Deploy = $true }
)
foreach ($case in $cases) {
    $scope = & "$PSScriptRoot/Get-ValidationScope.ps1" -Paths $case.Paths
    if ($scope.application -ne $case.Application -or $scope.deploy -ne $case.Deploy) {
        throw "Wrong validation scope for $($case.Paths -join ', ')"
    }
}
$scope = & "$PSScriptRoot/Get-ValidationScope.ps1" -Paths README.md -Full
if (!$scope.application -or !$scope.deploy) { throw 'Full validation must override path selection.' }
Write-Host "PASS: $($cases.Count + 1) validation-scope cases."
