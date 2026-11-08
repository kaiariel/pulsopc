# Prueba del Mapa del disco con lectura de la MFT (solo lee, no cambia nada).
# Necesita administrador. Deja los resultados en la carpeta pruebas, junto a este script (que tiene que estar al lado de PulsoPC.exe).
$exe = Join-Path $PSScriptRoot 'PulsoPC.exe'
$out = Join-Path $PSScriptRoot 'pruebas'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$log = Join-Path $out 'tiempos.txt'
"Prueba de la MFT - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Set-Content $log -Encoding UTF8

function Medir($nombre, $args2) {
    Write-Host "Corriendo: $nombre ..."
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $exe -ArgumentList $args2 -Wait -PassThru
    "{0}: {1:N1} s (salida {2})" -f $nombre, $sw.Elapsed.TotalSeconds, $p.ExitCode | Add-Content $log -Encoding UTF8
}

Medir 'C: con MFT' @('--mapa', 'C:\', "`"$out\mft-c.txt`"")
Medir 'E: con MFT' @('--mapa', 'E:\', "`"$out\mft-e.txt`"")
Medir 'C: recorrido de carpetas (para comparar)' @('--mapa-lento', 'C:\', "`"$out\lento-c.txt`"")

Write-Host ''
Get-Content $log
Write-Host ''
Write-Host 'Listo. Podés cerrar esta ventana.' -ForegroundColor Green
pause
