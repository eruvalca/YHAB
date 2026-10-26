[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Browser', 'Performance')]
    [string] $Suite,
    [Parameter(Mandatory)]
    [uri] $RunUrl,
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string] $Commit
)

$ErrorActionPreference = 'Stop'
$title = if ($Suite -eq 'Browser') {
    '[Nightly] Full browser validation failed'
}
else {
    '[Nightly] Performance validation failed or regressed'
}
$suiteDescription = if ($Suite -eq 'Browser') {
    'The scheduled full non-performance browser suite failed.'
}
else {
    'The scheduled performance suite failed or detected a regression.'
}
$body = @"
$suiteDescription

Commit: $Commit
Run: $RunUrl

Inspect the run and its retained artifacts. Keep traces, screenshots, and diagnostic payloads out of this issue.
<!-- yhab-nightly:$($Suite.ToLowerInvariant()) -->
"@
$search = "`"$title`" in:title"
$issuesJson = gh issue list --state open --search $search --limit 100 --json number,title
if ($LASTEXITCODE) {
    throw "Unable to search for an existing $Suite nightly issue."
}
$issues = @($issuesJson | ConvertFrom-Json)
$existing = $issues | Where-Object { $_.title -eq $title } | Select-Object -First 1
if ($null -ne $existing) {
    $comment = @"
$suiteDescription

Commit: $Commit
Run: $RunUrl
"@
    gh issue comment $existing.number --body $comment
    if ($LASTEXITCODE) {
        throw "Unable to update $Suite nightly issue #$($existing.number)."
    }
    Write-Host "Updated existing $Suite nightly issue #$($existing.number)."
    exit 0
}

gh issue create --title $title --body $body
if ($LASTEXITCODE) {
    throw "Unable to create the $Suite nightly issue."
}
Write-Host "Created a new $Suite nightly issue."
