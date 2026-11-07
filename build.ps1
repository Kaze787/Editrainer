$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\Editrainer.csproj'
$outputDir = Join-Path $PSScriptRoot 'build-tools\release'
dotnet publish $project -c Release --self-contained false -o $outputDir --nologo -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'No se pudo compilar la app.' }
Get-ChildItem -LiteralPath $outputDir -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $PSScriptRoot -Force }
Write-Output ('App lista: ' + (Join-Path $PSScriptRoot 'Editrainer.exe'))
