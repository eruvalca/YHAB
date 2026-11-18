function Get-ValidationMutexName([string] $RepoRoot) {
    $fullRoot = [IO.Path]::GetFullPath($RepoRoot).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $rootBytes = [Text.Encoding]::UTF8.GetBytes($fullRoot)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $hash = [Convert]::ToHexString($sha.ComputeHash($rootBytes)).Substring(0, 32).ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
    return "YHAB-Validation-$hash"
}

function Enter-ValidationLock([string] $RepoRoot, [double] $TimeoutSeconds = 600) {
    $mutexName = Get-ValidationMutexName $RepoRoot
    $createdNew = $false
    $mutex = [Threading.Mutex]::new($false, $mutexName, [ref] $createdNew)
    if (-not $mutex.WaitOne([TimeSpan]::FromSeconds($TimeoutSeconds))) {
        $mutex.Dispose()
        throw "Another YHAB validation is still running for $RepoRoot."
    }
    return $mutex
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
        if ($line -match '(.+\.razor)\(\d+,\d+\)') {
            [void] $paths.Add($Matches[1].Trim())
        }
    }
    return $paths.Count -ge 2
}

Export-ModuleMember -Function Get-ValidationMutexName, Enter-ValidationLock, Exit-ValidationLock, Test-WidespreadRazorParserFailure
