$ErrorActionPreference = "Stop"
$root = "C:\Users\sueli\Documents\desenvolvedor\pceb-site"
$fonts = Join-Path $root "assets\fonts"
$ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36"
$url = "https://fonts.googleapis.com/css2?family=Archivo:wght@400..700&family=Inter:wght@400..600&family=IBM+Plex+Mono:wght@400;500&display=swap"
$css = (Invoke-WebRequest -Uri $url -UserAgent $ua -UseBasicParsing -TimeoutSec 30).Content
Set-Content -Path (Join-Path $fonts "_source.css") -Value $css -Encoding UTF8

$pattern = "(?s)/\* ([^*]+) \*/\s*@font-face\s*\{([^}]+)\}"
$matches = [regex]::Matches($css, $pattern)
foreach ($m in $matches) {
    $subset = $m.Groups[1].Value.Trim()
    if ($subset -ne "latin") { continue }
    $body = $m.Groups[2].Value
    $fam = [regex]::Match($body, "font-family: '([^']+)'").Groups[1].Value
    $wghtMatch = [regex]::Match($body, "font-weight: ([\d ]+);")
    if ($wghtMatch.Success) { $wght = $wghtMatch.Groups[1].Value.Trim().Replace(" ", "-") } else { $wght = "400" }
    $style = [regex]::Match($body, "font-style: (\w+);").Groups[1].Value
    $src = [regex]::Match($body, "url\((https://[^)]+)\)").Groups[1].Value
    $fname = ($fam.ToLower().Replace(" ", "-")) + "-" + $wght + "-" + $style + ".woff2"
    $dest = Join-Path $fonts $fname
    Invoke-WebRequest -Uri $src -OutFile $dest -UserAgent $ua -UseBasicParsing -TimeoutSec 60
    Write-Output "$fam | $wght | $style | $fname"
}
