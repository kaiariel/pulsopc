# Compacta el disco virtual de Docker (docker_data.vhdx) para que devuelva el espacio libre a Windows.
# No borra nada de Docker: imágenes, contenedores y volúmenes quedan igual.
# Requisitos: Docker Desktop cerrado (también desde el ícono junto al reloj) y ejecutar como administrador.
# Uso: powershell -ExecutionPolicy Bypass -File compactar-docker.ps1

$vhdx = Join-Path $env:LOCALAPPDATA 'Docker\wsl\disk\docker_data.vhdx'
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { Write-Host 'Hace falta abrirlo como administrador.' -ForegroundColor Red; pause; exit 1 }
if (-not (Test-Path $vhdx)) {
    # Al ejecutar como administrador, LOCALAPPDATA puede ser el de otra cuenta: se busca en el perfil del usuario dueño
    $vhdx = Get-ChildItem 'C:\Users\*\AppData\Local\Docker\wsl\disk\docker_data.vhdx' -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $vhdx) { Write-Host 'No se encontró docker_data.vhdx' -ForegroundColor Red; pause; exit 1 }
if (Get-Process 'Docker Desktop', 'com.docker.backend' -ErrorAction SilentlyContinue) {
    Write-Host 'Docker Desktop sigue abierto. Cerralo (clic derecho en la ballena junto al reloj > Quit Docker Desktop) y volvé a ejecutar.' -ForegroundColor Yellow
    pause; exit 1
}

$antes = (Get-Item $vhdx).Length
Write-Host ("Disco de Docker: {0:N1} GB. Apagando WSL..." -f ($antes / 1GB))
wsl --shutdown
Start-Sleep -Seconds 5

$script = Join-Path $env:TEMP 'compactar-docker.txt'
@"
select vdisk file="$vhdx"
attach vdisk readonly
compact vdisk
detach vdisk
"@ | Set-Content -Path $script -Encoding ASCII
Write-Host 'Compactando (puede tardar varios minutos)...'
diskpart /s $script
Remove-Item $script -ErrorAction SilentlyContinue

$despues = (Get-Item $vhdx).Length
$msg = "Disco de Docker compactado: {0:N1} GB -> {1:N1} GB (se liberaron {2:N1} GB)" -f ($antes / 1GB), ($despues / 1GB), (($antes - $despues) / 1GB)
Write-Host ''; Write-Host $msg -ForegroundColor Green
try { Add-Content -Path (Join-Path ([IO.Path]::GetDirectoryName($vhdx) -replace '\\Docker\\wsl\\disk$', '\PulsoPC') 'registro.txt') -Value ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + '  ' + $msg) -Encoding UTF8 } catch { }
Write-Host 'Ya podés volver a abrir Docker Desktop.'
pause
