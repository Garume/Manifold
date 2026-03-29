param(
    [string]$Solution = 'Manifold.slnx',
    [switch]$NoRestore,
    [switch]$NoBuild,
    [string]$PackageVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Get-DotNetCommand.ps1')
$dotnet = Get-DotNetCommand -RepositoryRoot $root

if (-not $NoRestore) {
    & (Join-Path $PSScriptRoot 'restore.ps1') -Solution $Solution
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

Push-Location $root
try {
    $packageOutput = Join-Path $root '.artifacts/packages'
    New-Item -ItemType Directory -Path $packageOutput -Force | Out-Null

    [xml]$solutionDocument = Get-Content (Resolve-Path $Solution)
    $packProjects =
        $solutionDocument.SelectNodes('//Project[@Path]') |
        ForEach-Object { $_.Path } |
        Where-Object { $_ -match '^(src[\\/].+\.csproj)$' } |
        ForEach-Object { (Resolve-Path (Join-Path $root $_)).Path }

    foreach ($packProject in $packProjects) {
        $arguments = @('pack', $packProject, '-c', 'Release', "-p:PackageOutputPath=$packageOutput")
        if (-not [string]::IsNullOrWhiteSpace($PackageVersion)) {
            $arguments += "-p:PackageVersion=$PackageVersion"
        }
        if ($NoRestore) {
            $arguments += '--no-restore'
        }

        if ($NoBuild) {
            $arguments += '--no-build'
        }

        & $dotnet @arguments
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
}
finally {
    Pop-Location
}
