#Requires -RunAsAdministrator

param(
    [int]    $Port          = 5063,
    [bool]   $SeedSampleData = $true
)

$ServiceName = "LocalAuthService"
$DisplayName = "LocalAuthService - OAuth2 Auth Server"
$BinaryPath  = Join-Path $PSScriptRoot "LocalAuthService.exe"

if (-not (Test-Path $BinaryPath)) {
    Write-Error "Executable not found: $BinaryPath"
    exit 1
}

# Aggiorna appsettings.json con i parametri scelti
$settingsPath = Join-Path $PSScriptRoot "appsettings.json"
if (Test-Path $settingsPath) {
    $settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
    $settings.SeedSampleData = $SeedSampleData
    $settings | ConvertTo-Json -Depth 10 | Set-Content $settingsPath
    Write-Host "appsettings.json aggiornato: SeedSampleData=$SeedSampleData"
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Stopping and removing existing service..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

New-Service `
    -Name $ServiceName `
    -DisplayName $DisplayName `
    -BinaryPathName "`"$BinaryPath`" --urls `"http://localhost:$Port`" --contentRoot `"$PSScriptRoot`"" `
    -StartupType Automatic `
    -Description "Offline-first OAuth2 / OpenID Connect server for industrial machines"

Start-Service -Name $ServiceName
Write-Host "Service '$ServiceName' installed and started."
Write-Host "Listening on: http://localhost:$Port"
Write-Host "SeedSampleData: $SeedSampleData"
