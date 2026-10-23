[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('none', 'smoke', 'full')]
    [string] $Scope,
    [string] $SmokeResult,
    [string] $FullResult,
    [string] $RunUrl
)

$ErrorActionPreference = 'Stop'
if ($Scope -eq 'none') {
    Write-Host 'No browser validation is required for these changed paths.'
    exit 0
}
$result = if ($Scope -eq 'full') { $FullResult } else { $SmokeResult }
if ($result -eq 'success') {
    Write-Host "The selected $Scope browser validation passed."
    exit 0
}
throw "The selected $Scope browser validation concluded '$result'. See $RunUrl"
