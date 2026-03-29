Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$errors = [System.Collections.Generic.List[string]]::new()

$requiredFiles = @(
    'Manifold.slnx',
    'README.md',
    'LICENSE',
    'build/pack.ps1',
    '.github/workflows/ci.yml'
)

foreach ($requiredFile in $requiredFiles) {
    $requiredPath = Join-Path $root $requiredFile
    if (-not (Test-Path $requiredPath)) {
        $errors.Add("Required repository file is missing: $requiredFile")
    }
}

$forbiddenReferences = @(
    'DalamudMCP.Plugin',
    'DalamudMCP.Protocol',
    'DalamudMCP.Cli',
    'DalamudMCP.Framework'
)

$sourceFiles = Get-ChildItem (Join-Path $root 'src'), (Join-Path $root 'tests'), (Join-Path $root 'samples') -Recurse -File |
    Where-Object { $_.Extension -in '.cs', '.csproj', '.props', '.targets' }

foreach ($sourceFile in $sourceFiles) {
    $content = Get-Content $sourceFile.FullName -Raw
    foreach ($forbiddenReference in $forbiddenReferences) {
        if ($content -match [regex]::Escape($forbiddenReference)) {
            $relativePath = $sourceFile.FullName.Substring($root.Length + 1)
            $errors.Add("$relativePath still references $forbiddenReference.")
        }
    }
}

if ($errors.Count -gt 0) {
    foreach ($error in $errors) {
        Write-Error $error
    }

    exit 1
}

Write-Host 'Repository architecture checks passed.'
