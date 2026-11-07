$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\Editrainer.csproj'
$outputDir = Join-Path $PSScriptRoot 'build-tools\release'
dotnet publish $project -c Release --self-contained false -o $outputDir --nologo -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'No se pudo compilar la app.' }
Get-ChildItem -LiteralPath $outputDir -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $PSScriptRoot -Force }
Write-Output ('App lista: ' + (Join-Path $PSScriptRoot 'Editrainer.exe'))

# The ZIP people download: the app and what fetches the images, which are not distributed.
$package = Join-Path $PSScriptRoot 'build-tools\package'
if (Test-Path -LiteralPath $package) { Remove-Item -LiteralPath $package -Recurse -Force }
$packedAssets = New-Item -ItemType Directory -Path (Join-Path $package 'Assets')
$packedTools = New-Item -ItemType Directory -Path (Join-Path $package 'build-tools')
Get-ChildItem -LiteralPath $outputDir -File | Where-Object Extension -ne '.pdb' | Copy-Item -Destination $package
'README.md', 'LICENSE', 'CREDITOS.md', 'Download-Images.bat' | ForEach-Object { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $_) -Destination $package }
'app.ico', 'app.png', 'SPRITES-CREDITS.md' | ForEach-Object { Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Assets\$_") -Destination $packedAssets }
'Download-Images.ps1', 'species.json', 'form-sprites.json' | ForEach-Object { Copy-Item -LiteralPath (Join-Path $PSScriptRoot "build-tools\$_") -Destination $packedTools }
$zip = Join-Path $PSScriptRoot 'Editrainer-windows.zip'
Compress-Archive -Path (Join-Path $package '*') -DestinationPath $zip -Force
Write-Output ('Paquete listo: ' + $zip)
