param([switch]$FrameworkDependent)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\JukkasDell\JukkasDell.csproj'
$output = Join-Path $root 'artifacts\app'
$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
& dotnet publish $project -c Release -r win-x64 --self-contained $selfContained -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Write-Host "Ready: $output\JukkasDell.exe"
