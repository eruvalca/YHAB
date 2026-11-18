$ErrorActionPreference = 'Stop'
$python = Get-Command python -ErrorAction Stop
& $python.Source (Join-Path $PSScriptRoot 'Test-BrowserWorkflowPolicy.py')
if ($LASTEXITCODE) { exit $LASTEXITCODE }
