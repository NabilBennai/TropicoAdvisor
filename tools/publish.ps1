<#
.SYNOPSIS
    Publishes TropicoAdvisor as one self-contained Windows executable (no .NET install needed).
.EXAMPLE
    ./tools/publish.ps1
    ./tools/publish.ps1 -Output D:\apps\TropicoAdvisor
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\publish'),
    [string]$Runtime = 'win-x64'
)

$project = Join-Path $PSScriptRoot '..\src\Tropico.Desktop'

dotnet publish $project -c Release -r $Runtime --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None `
    -o $Output
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Published to $((Resolve-Path $Output).Path)\Tropico.Desktop.exe"
