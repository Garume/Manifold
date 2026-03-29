param(
    [string]$Solution = 'Manifold.slnx',
    [switch]$NoRestore = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Get-DotNetCommand.ps1')
$dotnet = Get-DotNetCommand -RepositoryRoot $root

Push-Location $root
try {
    [xml]$solutionDocument = Get-Content (Resolve-Path $Solution)
    $buildProjects =
        $solutionDocument.SelectNodes('//Project[@Path]') |
        ForEach-Object { $_.Path } |
        Where-Object { $_ -match '^((src|samples)[\\/].+\.csproj)$' } |
        ForEach-Object { (Resolve-Path (Join-Path $root $_)).Path }

    if ($buildProjects.Count -eq 0) {
        $buildProjects = @(Resolve-Path $Solution | Select-Object -ExpandProperty Path)
    }

    foreach ($buildProject in $buildProjects) {
        $arguments = @('build', $buildProject)
        if ($NoRestore) {
            $arguments += '--no-restore'
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
