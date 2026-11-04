param(
    [string]$DataDir = (Join-Path $PSScriptRoot '..\App_Data\pgdata')
)

$ErrorActionPreference = 'Stop'

$pgCtl = Get-Command pg_ctl -ErrorAction SilentlyContinue
if (-not $pgCtl) {
    throw 'pg_ctl не найден в PATH.'
}

$dataDir = [System.IO.Path]::GetFullPath($DataDir)
if (-not (Test-Path (Join-Path $dataDir 'PG_VERSION'))) {
    Write-Host 'Кластер не найден, останавливать нечего.'
    exit 0
}

& $pgCtl.Source -D $dataDir -m fast stop
Write-Host 'PostgreSQL остановлен.'
