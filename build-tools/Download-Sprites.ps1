param([switch]$IncludeShiny)
$ErrorActionPreference = 'Stop'
$appRoot = Split-Path -Parent $PSScriptRoot
$speciesIds = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'species.json') -Raw | ConvertFrom-Json)
$jobs = foreach ($id in $speciesIds) {
    [pscustomobject]@{ Id = $id; Kind = 'sprites'; Remote = 'pokemon'; Shiny = $false }
    [pscustomobject]@{ Id = $id; Kind = 'artwork'; Remote = 'pokemon/other/official-artwork'; Shiny = $false }
    if ($IncludeShiny) {
        [pscustomobject]@{ Id = $id; Kind = 'sprites'; Remote = 'pokemon/shiny'; Shiny = $true }
        [pscustomobject]@{ Id = $id; Kind = 'artwork'; Remote = 'pokemon/other/official-artwork/shiny'; Shiny = $true }
    }
}
$results = @($jobs | ForEach-Object -Parallel {
    $job = $_
    $folder = Join-Path $using:appRoot ('Assets\' + $job.Kind + $(if ($job.Shiny) { '\shiny' } else { '' }))
    [System.IO.Directory]::CreateDirectory($folder) | Out-Null
    $path = Join-Path $folder ($job.Id.ToString() + '.png')
    if (Test-Path -LiteralPath $path) { [pscustomobject]@{ Id = $job.Id; Kind = $job.Kind; Shiny = $job.Shiny; Ok = $true }; return }
    $uri = 'https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/' + $job.Remote + '/' + $job.Id + '.png'
    $ok = $false
    for ($attempt = 0; $attempt -lt 3; $attempt++) {
        try {
            $download = Invoke-WebRequest -Uri $uri -TimeoutSec 20
            $bytes = $download.Content
            if ($bytes.Length -lt 100 -or $bytes[0] -ne 137 -or $bytes[1] -ne 80) { throw 'Not a PNG.' }
            [System.IO.File]::WriteAllBytes($path, [byte[]]$bytes)
            $ok = $true; break
        } catch { if ($attempt -lt 2) { Start-Sleep -Milliseconds 400 } }
    }
    [pscustomobject]@{ Id = $job.Id; Kind = $job.Kind; Shiny = $job.Shiny; Ok = $ok }
} -ThrottleLimit 12)
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'sprite-downloads.json') -Encoding utf8
"Downloaded: $(@($results | Where-Object Ok).Count) / $($results.Count)"
$results | Where-Object { -not $_.Ok } | ConvertTo-Json -Compress
