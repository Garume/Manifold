param(
    [string]$Solution = 'Manifold.slnx',
    [switch]$SkipRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $SkipRestore) {
    & (Join-Path $PSScriptRoot 'restore.ps1') -Solution $Solution
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

& (Join-Path $PSScriptRoot 'build.ps1') -Solution $Solution -NoRestore
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& (Join-Path $PSScriptRoot 'format.ps1') -Solution $Solution -NoRestore
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& (Join-Path $PSScriptRoot 'test.ps1') -Solution $Solution
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& (Join-Path $PSScriptRoot 'architecture.ps1')
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
