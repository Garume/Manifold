param(
    [string]$Solution = 'Manifold.slnx',
    [switch]$Fix,
    [switch]$NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Get-DotNetCommand.ps1')
$dotnet = Get-DotNetCommand -RepositoryRoot $root

Push-Location $root
try {
    $arguments = @('format', $Solution)
    if (-not $Fix) {
        $arguments += '--verify-no-changes'
    }

    if ($NoRestore) {
        $arguments += '--no-restore'
    }

    & $dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}
