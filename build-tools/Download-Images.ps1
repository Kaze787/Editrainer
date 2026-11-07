# Downloads the sprites and artwork the app shows, into Assets. Runs on the PowerShell every Windows includes.
# What is already there is skipped, so running it again finishes an interrupted download.
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.Net.Http

$assets = Join-Path (Split-Path -Parent $PSScriptRoot) 'Assets'
$source = 'https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites'
$sprite = 'pokemon'
$shinySprite = 'pokemon/shiny'
$artwork = 'pokemon/other/official-artwork'
$shinyArtwork = 'pokemon/other/official-artwork/shiny'
$fallback = 'pokemon/other/home'
$shinyFallback = 'pokemon/other/home/shiny'
$simultaneous = 16
$rounds = 3

function New-Image($folder, $name, $origins) {
    [pscustomobject]@{ Path = Join-Path (Join-Path $assets $folder) "$name.png"; Origins = $origins }
}

$images = New-Object System.Collections.Generic.List[object]
foreach ($id in (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'species.json') -Raw | ConvertFrom-Json)) {
    $images.Add((New-Image 'sprites' $id @("$sprite/$id")))
    $images.Add((New-Image 'sprites\shiny' $id @("$shinySprite/$id")))
    $images.Add((New-Image 'artwork' $id @("$artwork/$id")))
    $images.Add((New-Image 'artwork\shiny' $id @("$shinyArtwork/$id")))
}
foreach ($form in (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'form-sprites.json') -Raw | ConvertFrom-Json)) {
    $images.Add((New-Image 'sprites' $form.local @("$sprite/$($form.remote)")))
    $images.Add((New-Image 'sprites\shiny' $form.local @("$shinySprite/$($form.remote)")))
    $images.Add((New-Image 'artwork' $form.local @("$artwork/$($form.remote)", "$fallback/$($form.remote)")))
    $images.Add((New-Image 'artwork\shiny' $form.local @("$shinyArtwork/$($form.remote)", "$shinyFallback/$($form.remote)")))
}

function Get-Missing { $images | Where-Object { -not (Test-Path -LiteralPath $_.Path) } }

$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(60)

# Each round tries an image's next origin, so a form without official artwork falls back to its other picture.
for ($round = 0; $round -lt $rounds; $round++) {
    $missing = @(Get-Missing)
    if ($missing.Count -eq 0) { break }
    for ($first = 0; $first -lt $missing.Count; $first += $simultaneous) {
        $batch = @($missing[$first..([Math]::Min($first + $simultaneous, $missing.Count) - 1)])
        $downloads = @($batch | ForEach-Object {
            $client.GetByteArrayAsync("$source/$($_.Origins[$round % $_.Origins.Count]).png")
        })
        try { [void][System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]$downloads) } catch { }
        for ($i = 0; $i -lt $batch.Count; $i++) {
            if ($downloads[$i].Status -ne 'RanToCompletion') { continue }
            $bytes = $downloads[$i].Result
            if ($bytes.Length -lt 100 -or $bytes[0] -ne 137 -or $bytes[1] -ne 80) { continue }
            [void][System.IO.Directory]::CreateDirectory((Split-Path -Parent $batch[$i].Path))
            [System.IO.File]::WriteAllBytes($batch[$i].Path, $bytes)
        }
        Write-Host -NoNewline ("`r  {0} of {1} images ready   " -f ($images.Count - $missing.Count + [Math]::Min($first + $simultaneous, $missing.Count)), $images.Count)
    }
}
$client.Dispose()
Write-Host ''
Write-Host ''

$failed = @(Get-Missing).Count
if ($failed -eq 0) {
    Write-Host "Done. All $($images.Count) images are ready. You can open Editrainer.exe now."
    exit 0
}
Write-Host "$failed of $($images.Count) images could not be downloaded."
Write-Host 'Check your internet connection and run this again: it continues where it stopped.'
exit 1
