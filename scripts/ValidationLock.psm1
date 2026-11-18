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
    return Join-Path $root 'artifacts/validation.lock'
}

function Enter-ValidationLock([string] $RepoRoot, [double] $TimeoutSeconds = 60) {
    $lockPath = Get-ValidationLockPath $RepoRoot
    $lockDirectory = Split-Path $lockPath
    New-Item -ItemType Directory -Path $lockDirectory -Force | Out-Null
    $deadline = [DateTime]::UtcNow.AddSeconds([Math]::Max(0.1, $TimeoutSeconds))
    while ($true) {
        try {
            return [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        }
        catch [IO.IOException] {
            if ([DateTime]::UtcNow -ge $deadline) {
                throw "Another YHAB validation is still running for $RepoRoot."
            }
            Start-Sleep -Milliseconds 50
        }
    }
}

function Exit-ValidationLock([IO.FileStream] $Lock) {
    $Lock.Dispose()
}

function Test-WidespreadRazorParserFailure([string] $Output) {
    if ($Output -notmatch 'RZ1021|RZ1009|RZ9981|RZ1034|RZ1026') {
        return $false
    }
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($line in ($Output -split "`r?`n")) {
        if ($line -match 'RZ1021|RZ1009|RZ9981|RZ1034|RZ1026') {
            $match = [regex]::Match($line, '(.+\.razor)\(\d+,\d+\)')
            if ($match.Success) {
                [void] $paths.Add($match.Groups[1].Value.Trim())
            }
        }
    }
    return $paths.Count -ge 2
}

Export-ModuleMember -Function Get-CanonicalRepoRoot, Get-ValidationLockPath, Enter-ValidationLock, Exit-ValidationLock, Test-WidespreadRazorParserFailure
