param(
    [string]$Solution = 'Manifold.slnx',
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Get-DotNetCommand.ps1')
$dotnet = Get-DotNetCommand -RepositoryRoot $root

Push-Location $root
try {
    [xml]$solutionDocument = Get-Content (Resolve-Path $Solution)
    $testProjects =
        $solutionDocument.SelectNodes('//Project[@Path]') |
        ForEach-Object { $_.Path } |
        Where-Object { $_ -match '^(tests[\\/].+\.csproj)$' } |
        ForEach-Object { (Resolve-Path (Join-Path $root $_)).Path }

    $outputRoot = $null
    if (-not $NoBuild) {
        $outputRoot = Join-Path $root '.artifacts/test-output'
        $runDirectory = Join-Path $outputRoot ([Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
    }

    foreach ($testProject in $testProjects) {
        $arguments = @('test', '--project', $testProject)
        if ($NoBuild) {
            $arguments += '--no-build'
        }
        else {
            $projectName = [IO.Path]::GetFileNameWithoutExtension($testProject)
            $outDirectory = Join-Path $runDirectory $projectName
            New-Item -ItemType Directory -Path $outDirectory -Force | Out-Null
            $arguments += "-p:OutDir=$outDirectory\"
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
