#requires -Version 7.0
[CmdletBinding()]
param(
    [switch] $Force,
    [switch] $Uninstall
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path $PSScriptRoot

function Invoke-CheckedGit([string[]] $Arguments) {
    $output = & git -C $repoRoot @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
    return $output
}

try {
    $actualRoot = Invoke-CheckedGit @('rev-parse', '--show-toplevel')
    if ([IO.Path]::GetFullPath($actualRoot) -ne [IO.Path]::GetFullPath($repoRoot)) {
        throw 'Run the installer from a checkout containing these scripts.'
    }
    $existing = & git -C $repoRoot config --get core.hooksPath
    if ($LASTEXITCODE -notin @(0, 1)) {
        throw 'Could not read core.hooksPath.'
    }
    if ($Uninstall) {
        $local = & git -C $repoRoot config --local --get core.hooksPath
        if ($LASTEXITCODE -notin @(0, 1)) {
            throw 'Could not read local core.hooksPath.'
        }
        if ($local -eq '.githooks') {
            Invoke-CheckedGit @('config', '--local', '--unset-all', 'core.hooksPath')
            Write-Host 'Removed local core.hooksPath. Inherited Git configuration applies again.'
        }
        else {
            Write-Host 'No local YHAB hooks configuration to remove; left existing settings unchanged.'
        }
        exit 0
    }

    if ($existing -ne '.githooks' -and -not $Force) {
        if ($null -ne $existing) {
            throw "Existing core.hooksPath '$existing' is protected. Record it before using -Force."
        }
        $defaultHooks = Invoke-CheckedGit @('rev-parse', '--git-path', 'hooks')
        if (-not [IO.Path]::IsPathRooted($defaultHooks)) {
            $defaultHooks = Join-Path $repoRoot $defaultHooks
        }
        $customHooks = @(Get-ChildItem -LiteralPath $defaultHooks -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -notlike '*.sample' })
        if ($customHooks.Count -gt 0) {
            throw 'Existing default hooks are protected. Review them before using -Force; hooks are not chained.'
        }
    }

    foreach ($name in @('pre-commit', 'pre-push')) {
        $path = Join-Path $repoRoot ".githooks/$name"
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Missing hook: $path"
        }
        if (-not $IsWindows) {
            & chmod +x $path
            if ($LASTEXITCODE -ne 0) {
                throw "Could not mark $name executable."
            }
        }
    }
    if ($Force -and $null -ne $existing -and $existing -ne '.githooks') {
        Write-Host "Replacing effective core.hooksPath '$existing' with a local override; record its scope/value for restoration."
    }
    $worktreeHooks = & git -C $repoRoot config --worktree --get core.hooksPath
    if ($LASTEXITCODE -notin @(0, 1)) {
        throw 'Could not read worktree core.hooksPath.'
    }
    $worktreeConfig = & git -C $repoRoot config --get extensions.worktreeConfig
    if ($LASTEXITCODE -notin @(0, 1)) {
        throw 'Could not read extensions.worktreeConfig.'
    }
    if ($worktreeConfig -eq 'true' -and $null -ne $worktreeHooks -and $worktreeHooks -ne '.githooks') {
        throw "Worktree core.hooksPath '$worktreeHooks' overrides local configuration; resolve it explicitly first."
    }
    Invoke-CheckedGit @('config', '--local', '--replace-all', 'core.hooksPath', '.githooks')
    Write-Host 'Enabled local YHAB hooks: pre-commit Quick; pre-push Fast.'
}
catch {
    [Console]::Error.WriteLine("Hook installation failed: $($_.Exception.Message)")
    exit 1
}
