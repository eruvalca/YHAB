function Get-CanonicalRepoRoot([string] $RepoRoot) {
    $resolved = (Resolve-Path -LiteralPath $RepoRoot -ErrorAction Stop).ProviderPath
    try {
        $gitRoot = (& git -C $resolved rev-parse --show-toplevel 2>$null | Select-Object -First 1)
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($gitRoot)) {
            $resolved = (Resolve-Path -LiteralPath $gitRoot.Trim() -ErrorAction Stop).ProviderPath
        }
    }
    catch {
        # A test fixture or caller without Git still uses the resolved filesystem path.
    }
    return [IO.Path]::GetFullPath($resolved).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
}

function Get-ValidationLockPath([string] $RepoRoot) {
    $root = Get-CanonicalRepoRoot $RepoRoot
    return Join-Path (Join-Path $root 'artifacts') '.validation.lock'
}

function Enter-ValidationLock([string] $RepoRoot, [double] $TimeoutSeconds = 60) {
    $lockPath = Get-ValidationLockPath $RepoRoot
    $lockDirectory = Split-Path -Parent $lockPath
    New-Item -ItemType Directory -Path $lockDirectory -Force | Out-Null
    $timer = [Diagnostics.Stopwatch]::StartNew()
    try {
        while ($true) {
            try {
                return [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            }
            catch [IO.IOException] {
                if ($timer.Elapsed.TotalSeconds -ge [Math]::Max(0.1, $TimeoutSeconds)) {
                    throw "Could not acquire the YHAB validation lock at '$lockPath' within $TimeoutSeconds seconds."
                }
                Start-Sleep -Milliseconds 250
            }
        }
    }
    finally {
        $timer.Stop()
    }
}

function Exit-ValidationLock([IO.FileStream] $FileLock) {
    $FileLock.Dispose()
}

Export-ModuleMember -Function Get-CanonicalRepoRoot, Get-ValidationLockPath, Enter-ValidationLock, Exit-ValidationLock
