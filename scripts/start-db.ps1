param(
    [int]$Port = 5433,
    [string]$DataDir = (Join-Path $PSScriptRoot '..\App_Data\pgdata')
)

$ErrorActionPreference = 'Stop'

$initdb = Get-Command initdb -ErrorAction SilentlyContinue
$pgCtl = Get-Command pg_ctl -ErrorAction SilentlyContinue
$psql = Get-Command psql -ErrorAction SilentlyContinue

if (-not $initdb -or -not $pgCtl -or -not $psql) {
    throw 'PostgreSQL tools не найдены в PATH. Установите PostgreSQL или используйте docker compose up -d.'
}

$dataDir = [System.IO.Path]::GetFullPath($DataDir)
$logFile = Join-Path $dataDir 'postgres.log'

if (-not (Test-Path (Join-Path $dataDir 'PG_VERSION'))) {
    New-Item -ItemType Directory -Path $dataDir -Force | Out-Null
    Write-Host "Инициализация кластера в $dataDir..."
    & $initdb.Source -D $dataDir -U flowdesk --auth=trust --encoding=UTF8 --locale=C | Out-Null
}

$isRunning = & $pgCtl.Source -D $dataDir status 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host 'PostgreSQL уже запущен.'
} else {
    Write-Host "Запуск PostgreSQL на порту $Port..."
    $arguments = "-D `"$dataDir`" -o `"-p $Port`" -l `"$logFile`" start"
    Start-Process -FilePath $pgCtl.Source -ArgumentList $arguments -WindowStyle Hidden | Out-Null
    Start-Sleep -Seconds 3
}

$dbExists = & $psql.Source -h 127.0.0.1 -p $Port -U flowdesk -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = 'flowdesk'"
if ($dbExists.Trim() -ne '1') {
    Write-Host 'Создание базы flowdesk...'
    & $psql.Source -h 127.0.0.1 -p $Port -U flowdesk -d postgres -c 'CREATE DATABASE flowdesk' | Out-Null
}

Write-Host "Готово. Connection string: Host=127.0.0.1;Port=$Port;Database=flowdesk;Username=flowdesk;Password=flowdesk"
