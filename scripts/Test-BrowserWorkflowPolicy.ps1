$ErrorActionPreference = 'Stop'
$python = Get-Command python -ErrorAction Stop
& $python.Source (Join-Path $PSScriptRoot 'Test-BrowserWorkflowPolicy.py')
# Throw so a multi-command CI step stops here; a later script may reset $LASTEXITCODE.
if ($LASTEXITCODE) { throw "Browser workflow policy validation failed with exit code $LASTEXITCODE." }
