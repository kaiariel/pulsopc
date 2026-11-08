# Bloquea en el Explorador la extensión de sincronización de Adobe Creative Cloud (CoreSync_x64.dll):
# 3 íconos superpuestos y 1 entrada del menú contextual. No desinstala nada de Adobe.
# Usa la lista oficial de Windows "Shell Extensions\Blocked". Necesita administrador.
# Uso:      powershell -ExecutionPolicy Bypass -File bloquear-adobe-explorador.ps1
# Revertir: powershell -ExecutionPolicy Bypass -File bloquear-adobe-explorador.ps1 -Revertir

param([switch]$Revertir)

$clave = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked'
$ids = [ordered]@{
    '{AB9CF9F8-8A96-4F9D-BF21-CE85714C3A47}' = 'Adobe CoreSync - icono AccExtIco1'
    '{853B7E05-C47D-4985-909A-D0DC5C6D7303}' = 'Adobe CoreSync - icono AccExtIco2'
    '{42D38F2E-98E9-4382-B546-E24E4D6D04BB}' = 'Adobe CoreSync - icono AccExtIco3'
    '{2A118EB5-5797-4F5E-8B3D-F4ECBA3C98E4}' = 'Adobe CoreSync - menu contextual'
}

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { Write-Host 'Hace falta abrirlo como administrador.' -ForegroundColor Red; pause; exit 1 }

if (-not (Test-Path $clave)) { New-Item -Path $clave -Force | Out-Null }

foreach ($id in $ids.Keys) {
    if ($Revertir) {
        Remove-ItemProperty -Path $clave -Name $id -ErrorAction SilentlyContinue
        Write-Host "Desbloqueado: $($ids[$id])"
    } else {
        New-ItemProperty -Path $clave -Name $id -Value $ids[$id] -PropertyType String -Force | Out-Null
        Write-Host "Bloqueado:    $($ids[$id])"
    }
}

$log = Join-Path $env:LOCALAPPDATA 'PulsoPC\registro.txt'
$accion = if ($Revertir) { 'Desbloqueada' } else { 'Bloqueada' }
Add-Content -Path $log -Value ("{0:yyyy-MM-dd HH:mm:ss}  {1} la extension de Adobe CoreSync en el Explorador ({2} CLSID en Shell Extensions\Blocked)" -f (Get-Date), $accion, $ids.Count) -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Reiniciando el Explorador para que tome el cambio (la barra de tareas desaparece unos segundos)...'
Stop-Process -Name explorer -Force
Start-Sleep -Seconds 3
if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe }
Write-Host 'Listo.' -ForegroundColor Green
pause
