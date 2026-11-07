# Downloads the picture of every form listed in form-sprites.json. What is already there is skipped.
$ErrorActionPreference = 'Stop'
$appRoot = Split-Path -Parent $PSScriptRoot
$table = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'form-sprites.json') -Raw | ConvertFrom-Json
$jobs = foreach ($form in $table) {
    [pscustomobject]@{ Local = $form.local; Remote = $form.remote; Folder = 'sprites'; Sources = @('pokemon') }
    [pscustomobject]@{ Local = $form.local; Remote = $form.remote; Folder = 'sprites\shiny'; Sources = @('pokemon/shiny') }
    [pscustomobject]@{ Local = $form.local; Remote = $form.remote; Folder = 'artwork'; Sources = @('pokemon/other/official-artwork', 'pokemon/other/home') }
    [pscustomobject]@{ Local = $form.local; Remote = $form.remote; Folder = 'artwork\shiny'; Sources = @('pokemon/other/official-artwork/shiny', 'pokemon/other/home/shiny') }
}
$results = @($jobs | ForEach-Object -Parallel {
    $job = $_
    $folder = Join-Path $using:appRoot ('Assets\' + $job.Folder)
    [System.IO.Directory]::CreateDirectory($folder) | Out-Null
    $path = Join-Path $folder ($job.Local + '.png')
    if (Test-Path -LiteralPath $path) { [pscustomobject]@{ File = $job.Folder + '\' + $job.Local; Ok = $true }; return }
    $ok = $false
    foreach ($source in $job.Sources) {
        $uri = 'https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/' + $source + '/' + $job.Remote + '.png'
        for ($attempt = 0; $attempt -lt 3 -and -not $ok; $attempt++) {
            try {
                $bytes = (Invoke-WebRequest -Uri $uri -TimeoutSec 20).Content
                if ($bytes.Length -lt 100 -or $bytes[0] -ne 137 -or $bytes[1] -ne 80) { throw 'Not a PNG.' }
                [System.IO.File]::WriteAllBytes($path, [byte[]]$bytes)
                $ok = $true
            }
            catch {
                if ($_.Exception.Response.StatusCode.value__ -eq 404) { break }
                Start-Sleep -Milliseconds 400
            }
        }
        if ($ok) { break }
    }
    [pscustomobject]@{ File = $job.Folder + '\' + $job.Local; Ok = $ok }
} -ThrottleLimit 12)
$results | Where-Object { -not $_.Ok } | ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'form-sprites-missing.json') -Encoding utf8
"Downloaded: $(@($results | Where-Object Ok).Count) / $($results.Count)"
