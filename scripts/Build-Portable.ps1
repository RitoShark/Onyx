param(
    [switch]$NoRestore,
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$restoreArguments = @()
if ($NoRestore) { $restoreArguments += '--no-restore' }
if ($Version) { $restoreArguments += "-p:Version=$Version" }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repository 'Onyx/Onyx.csproj'
$output = Join-Path $repository 'artifacts/portable'

dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $output @restoreArguments
if ($LASTEXITCODE -ne 0) { throw 'Portable build failed.' }

$executable = Join-Path $output 'Onyx.exe'
if (!(Test-Path -LiteralPath $executable)) { throw 'Onyx.exe was not produced.' }
Get-Item -LiteralPath $executable | Select-Object FullName, Length
