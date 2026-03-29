param(
    [ValidateSet('all', 'cli', 'mcp')]
    [string]$Target = 'all',
    [switch]$NoRestore,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$BenchmarkArguments = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Get-DotNetCommand.ps1')
$dotnet = Get-DotNetCommand -RepositoryRoot $root

Push-Location $root
try {
    $projects = switch ($Target) {
        'cli' { @('benchmarks/Manifold.Benchmarks/Manifold.Benchmarks.csproj') }
        'mcp' { @('benchmarks/Manifold.Mcp.Benchmarks/Manifold.Mcp.Benchmarks.csproj') }
        default {
            @(
                'benchmarks/Manifold.Benchmarks/Manifold.Benchmarks.csproj',
                'benchmarks/Manifold.Mcp.Benchmarks/Manifold.Mcp.Benchmarks.csproj'
            )
        }
    }

    foreach ($project in $projects) {
        $projectName = [IO.Path]::GetFileNameWithoutExtension($project)
        $outputDirectory = Join-Path $root (Join-Path '.artifacts/benchmark-output' ([Guid]::NewGuid().ToString('N')))
        $projectOutputDirectory = Join-Path $outputDirectory $projectName
        New-Item -ItemType Directory -Path $projectOutputDirectory -Force | Out-Null

        $arguments = @('run', '-c', 'Release', '--project', $project)
        if ($NoRestore) {
            $arguments += '--no-restore'
        }
        $arguments += "-p:OutDir=$projectOutputDirectory\"

        $effectiveBenchmarkArguments = if ($BenchmarkArguments.Count -gt 0) {
            $BenchmarkArguments
        }
        else {
            @('--filter', '*')
        }

        $arguments += '--'
        $arguments += $effectiveBenchmarkArguments

        & $dotnet @arguments
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
}
finally {
    Pop-Location
}
