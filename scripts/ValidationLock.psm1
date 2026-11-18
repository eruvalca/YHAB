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

function Get-ValidationMutexName([string] $RepoRoot) {
    $root = Get-CanonicalRepoRoot $RepoRoot
    $bytes = [Text.Encoding]::UTF8.GetBytes($root)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $hex = -join ($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString('x2') })
    }
    finally {
        $sha.Dispose()
    }
    return "YHAB-Validation-$($hex.Substring(0,32))"
}

function Enter-ValidationLock([string] $RepoRoot, [double] $TimeoutSeconds = 60) {
    $mutexName = Get-ValidationMutexName $RepoRoot
    $createdNew = $false
    $mutex = [Threading.Mutex]::new($false, $mutexName, [ref] $createdNew)
    try {
        try {
            $acquired = $mutex.WaitOne([TimeSpan]::FromSeconds([Math]::Max(0.1, $TimeoutSeconds)))
        }
        catch [Threading.AbandonedMutexException] {
            $acquired = $true
        }
        catch {
            $mutex.Dispose()
            throw
        }
        if (-not $acquired) {
            $mutex.Dispose()
            throw "Another YHAB validation is still running for $RepoRoot."
        }
        return $mutex
    }
    catch {
        $mutex.Dispose()
        throw
    }
}

function Exit-ValidationLock([Threading.Mutex] $Mutex) {
    try {
        $Mutex.ReleaseMutex()
    }
    finally {
        $Mutex.Dispose()
    }
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

Export-ModuleMember -Function Get-CanonicalRepoRoot, Get-ValidationMutexName, Enter-ValidationLock, Exit-ValidationLock, Test-WidespreadRazorParserFailure
