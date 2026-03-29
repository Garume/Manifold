Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-DotNetCommand {
    param(
        [string]$RepositoryRoot
    )

    $localDotNet = Join-Path $RepositoryRoot '.dotnet\dotnet.exe'
    if (Test-Path $localDotNet) {
        return $localDotNet
    }

    $command = Get-Command dotnet -ErrorAction Stop
    return $command.Source
}
