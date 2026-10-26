$ErrorActionPreference = 'Stop'
$publisher = "$PSScriptRoot/Publish-NightlyValidationIssue.ps1"
$commit = '0123456789abcdef0123456789abcdef01234567'
$runUrl = 'https://github.com/eruvalca/YHAB/actions/runs/12345'
$global:calls = @()
$global:listResult = '[]'
$global:failList = $false

function global:gh {
    $global:calls += , [object[]] $args
    if ($args[0] -eq 'issue' -and $args[1] -eq 'list') {
        if ($global:failList) {
            $global:LASTEXITCODE = 1
            return
        }
        $global:LASTEXITCODE = 0
        $global:listResult
        return
    }
    $global:LASTEXITCODE = 0
    'https://github.com/eruvalca/YHAB/issues/42'
}

function Reset-GhMock {
    $global:calls = @()
    $global:listResult = '[]'
    $global:failList = $false
    $global:LASTEXITCODE = 0
}

function Assert-True([bool] $Condition, [string] $Message) {
    if (!$Condition) { throw $Message }
}

try {
    Reset-GhMock
    & $publisher -Suite Browser -RunUrl $runUrl -Commit $commit
    $create = @($global:calls | Where-Object { $_[1] -eq 'create' })
    Assert-True ($create.Count -eq 1) 'A new browser failure must create one issue.'
    Assert-True ($create[0][3] -eq '[Nightly] Full browser validation failed') 'The browser issue title must be stable.'
    Assert-True ($create[0][5].Contains($runUrl) -and $create[0][5].Contains($commit)) 'The issue must link the failed run and commit.'
    Assert-True ($create[0][5].Contains('yhab-nightly:browser')) 'The issue must contain its stable marker.'

    Reset-GhMock
    $global:listResult = '[{"number":41,"title":"[Nightly] Performance validation failed or regressed (old)"},{"number":42,"title":"[Nightly] Performance validation failed or regressed"}]'
    & $publisher -Suite Performance -RunUrl $runUrl -Commit $commit
    $comment = @($global:calls | Where-Object { $_[1] -eq 'comment' })
    Assert-True ($comment.Count -eq 1 -and $comment[0][2] -eq 42) 'A repeated performance failure must comment on the exact-title issue.'
    Assert-True (@($global:calls | Where-Object { $_[1] -eq 'create' }).Count -eq 0) 'A repeated failure must not create a duplicate issue.'
    Assert-True ($comment[0][4].Contains($runUrl)) 'The recurrence comment must link the new run.'

    Reset-GhMock
    $global:listResult = '[{"number":42,"title":"[Nightly] Full browser validation failed"}]'
    & $publisher -Suite Browser -RunUrl $runUrl -Commit $commit
    Assert-True (@($global:calls | Where-Object { $_[1] -eq 'comment' -and $_[2] -eq 42 }).Count -eq 1) 'A repeated browser failure must reuse its open issue.'

    Reset-GhMock
    $global:failList = $true
    $failed = $false
    try {
        & $publisher -Suite Browser -RunUrl $runUrl -Commit $commit
    }
    catch {
        $failed = $true
    }
    Assert-True $failed 'A failed issue search must fail the reporting step.'
    Assert-True (@($global:calls | Where-Object { $_[1] -in @('create', 'comment') }).Count -eq 0) 'A failed search must not write an issue.'

    Write-Host 'PASS: 4 nightly issue-reporting cases.'
}
finally {
    Remove-Item -Path Function:\gh -ErrorAction SilentlyContinue
}
