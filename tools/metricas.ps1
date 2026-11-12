# Métricas do PCEB: downloads dos instaladores (Releases do GitHub) e visitas ao repositório.
# Uso: powershell -NoProfile -File tools\metricas.ps1
# Precisa do GitHub CLI (gh) logado na conta dona do repositório (as visitas ao repositório só aparecem para quem tem acesso de escrita).
# O histórico diário dos downloads fica na branch "metricas" (arquivo downloads.csv), gravado pelo workflow .github/workflows/metricas.yml:
# o GitHub só informa o total acumulado, então é esse registro diário que permite ver quantos downloads houve em cada período.
$ErrorActionPreference = 'Stop'
$repo = 'Josileissu/PCEB'
$gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
if (-not $gh) { $gh = Join-Path $env:LOCALAPPDATA 'Programs\gh\bin\gh.exe' }
if (-not (Test-Path $gh)) { throw 'GitHub CLI (gh) não encontrado.' }

$rels = & $gh api "repos/$repo/releases?per_page=100" | ConvertFrom-Json
$linhas = foreach ($r in $rels) {
    foreach ($a in $r.assets) {
        [pscustomobject]@{
            Programa = if ($a.name -like 'DEB_Revit_Setup_*') { 'Revit' } else { 'CAD' }
            Versao = $r.tag_name
            Publicada = ([datetime]$r.published_at).ToString('dd/MM/yyyy')
            Downloads = $a.download_count
        }
    }
}
Write-Host "`nDOWNLOADS DOS INSTALADORES" -ForegroundColor Cyan
$linhas | Format-Table -AutoSize | Out-String | Write-Host
foreach ($g in $linhas | Group-Object Programa) { Write-Host ("Total {0}: {1}" -f $g.Name, ($g.Group | Measure-Object Downloads -Sum).Sum) }
Write-Host ("Total geral: {0}" -f ($linhas | Measure-Object Downloads -Sum).Sum)

Write-Host "`nREPOSITÓRIO NO GITHUB (últimos 14 dias)" -ForegroundColor Cyan
try {
    $v = & $gh api "repos/$repo/traffic/views" | ConvertFrom-Json
    $c = & $gh api "repos/$repo/traffic/clones" | ConvertFrom-Json
    Write-Host ("Visualizações: {0} ({1} visitantes únicos)" -f $v.count, $v.uniques)
    Write-Host ("Clones: {0} ({1} únicos)" -f $c.count, $c.uniques)
    $ref = & $gh api "repos/$repo/traffic/popular/referrers" | ConvertFrom-Json
    if ($ref) { Write-Host 'De onde vieram:'; $ref | Select-Object referrer, count, uniques | Format-Table -AutoSize | Out-String | Write-Host }
} catch { Write-Host 'Sem acesso às visitas do repositório (precisa de acesso de escrita).' }

Write-Host "Visitas ao site (www.pceb.com.br): no painel do contador de visitas (ver docs/notas/metricas.md)." -ForegroundColor DarkGray
