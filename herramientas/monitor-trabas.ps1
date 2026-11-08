# Monitor de trabas: cada 3 segundos anota cómo está el equipo en un CSV.
# Cuando la PC se trabe, anotá la hora y se cruza con el archivo.
# Uso: powershell -NoProfile -ExecutionPolicy Bypass -File monitor-trabas.ps1 [-Segundos 3]
# Salida: %LOCALAPPDATA%\PulsoPC\monitor-AAAA-MM-DD-v2.csv (uno por día), con los dos programas que más leyeron del disco y más CPU usaron en cada muestra. Se corta con Ctrl+C o cerrando la ventana.

param([int]$Segundos = 3)

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class Ventana {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool IsHungAppWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr res);
    public static int Pid(IntPtr h) { uint p; GetWindowThreadProcessId(h, out p); return (int)p; }
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int l, t, r, b; }
    [StructLayout(LayoutKind.Sequential)] public struct GUITHREADINFO {
        public int cbSize; public int flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public RECT rcCaret;
    }
    [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO info);
    // Teclas que Windows cree apretadas (una tecla "trabada" bloquea clics y atajos).
    public static string Apretadas() {
        var n = new System.Collections.Generic.List<string>();
        int[] vk = { 0x11, 0x12, 0x10, 0x5B, 0x5C, 0x01, 0x02 };
        string[] nom = { "Ctrl", "Alt", "Shift", "Win", "Win", "ClicIzq", "ClicDer" };
        for (int i = 0; i < vk.Length; i++) if ((GetAsyncKeyState(vk[i]) & 0x8000) != 0 && !n.Contains(nom[i])) n.Add(nom[i]);
        return string.Join("+", n);
    }
    // Quién tiene el mouse "capturado" y si hay un menú o arrastre abierto.
    public static string Captura() {
        var g = new GUITHREADINFO(); g.cbSize = Marshal.SizeOf(g);
        if (!GetGUIThreadInfo(0, ref g)) return "";
        var s = "";
        if (g.hwndCapture != IntPtr.Zero) s += "captura:" + Pid(g.hwndCapture);
        if ((g.flags & 0x4) != 0) s += " menu";
        if ((g.flags & 0x2) != 0) s += " arrastre";
        return s.Trim();
    }
    // Milisegundos que tarda la ventana en responder (tope 5000).
    public static int Responde(IntPtr h) {
        IntPtr r; var t = Environment.TickCount;
        var ok = SendMessageTimeout(h, 0, IntPtr.Zero, IntPtr.Zero, 2, 5000, out r);
        return ok == IntPtr.Zero ? 5000 : Environment.TickCount - t;
    }
}
"@

$dir = Join-Path $env:LOCALAPPDATA 'PulsoPC'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$ultimoDrive = Get-Date
$previo = @{}
$tPrevio = Get-Date

# Qué programa leyó más del disco y cuál usó más CPU desde la muestra anterior.
function Culpables($seg) {
    $actual = @{}
    $lect = @{}; $cpu = @{}
    foreach ($p in Get-CimInstance Win32_Process -Property ProcessId, Name, ReadTransferCount, KernelModeTime, UserModeTime) {
        $actual[$p.ProcessId] = $p
        $a = $script:previo[$p.ProcessId]
        if ($a -and $a.Name -eq $p.Name -and $p.ProcessId -ne 0) {
            $n = $p.Name -replace '\.exe$',''
            $lect[$n] += [double]($p.ReadTransferCount - $a.ReadTransferCount)
            $cpu[$n]  += [double](($p.KernelModeTime + $p.UserModeTime) - ($a.KernelModeTime + $a.UserModeTime))
        }
    }
    $script:previo = $actual
    $topL = $lect.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 2
    $topC = $cpu.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 2
    $nucleos = [Environment]::ProcessorCount
    [pscustomobject]@{
        Lectura = ($topL | ForEach-Object { '{0} {1:N0}MB/s' -f $_.Key, ($_.Value / 1MB / $seg) }) -join ' | '
        CPU     = ($topC | ForEach-Object { '{0} {1:N0}%' -f $_.Key, ($_.Value / 1e7 / $seg / $nucleos * 100) }) -join ' | '
    }
}
[void](Culpables 1)

Write-Host "Monitor de trabas activo cada $Segundos s. Archivo en $dir"

while ($true) {
    $ahora = Get-Date
    $csv = Join-Path $dir ("monitor-{0:yyyy-MM-dd}-v4.csv" -f $ahora)
    try {
        $mem  = Get-CimInstance Win32_PerfFormattedData_PerfOS_Memory
        $cpu  = Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor -Filter "Name='_Total'"
        $disk = Get-CimInstance Win32_PerfFormattedData_PerfDisk_PhysicalDisk -Filter "Name='_Total'"

        $fg = [Ventana]::GetForegroundWindow()
        $fgProc = ''; $fgColgada = $false; $fgMs = 0
        if ($fg -ne [IntPtr]::Zero) {
            $p = Get-Process -Id ([Ventana]::Pid($fg)) -ErrorAction SilentlyContinue
            $fgProc = if ($p) { $p.ProcessName } else { '?' }
            $fgColgada = [Ventana]::IsHungAppWindow($fg)
            $fgMs = [Ventana]::Responde($fg)
        }

        # Barra de tareas (ventana Shell_TrayWnd del Explorador): cuánto tarda en responder.
        $barra = [Ventana]::FindWindow('Shell_TrayWnd', $null)
        $barraMs = if ($barra -ne [IntPtr]::Zero) { [Ventana]::Responde($barra) } else { -1 }
        $barraColgada = ($barra -ne [IntPtr]::Zero) -and [Ventana]::IsHungAppWindow($barra)

        # Pedidos a G: que Google Drive no contestó a tiempo desde la muestra anterior.
        $drive = @(Get-WinEvent -FilterHashtable @{LogName='System'; ProviderName='googledrivefs31931'; StartTime=$ultimoDrive} -ErrorAction SilentlyContinue).Count
        $ultimoDrive = $ahora

        $seg = [math]::Max(1, ($ahora - $tPrevio).TotalSeconds); $tPrevio = $ahora
        $top = Culpables $seg

        $fila = [pscustomobject]@{
            Hora             = $ahora.ToString('HH:mm:ss')
            RAM_libre_MB     = $mem.AvailableMBytes
            Commit_pct       = $mem.PercentCommittedBytesInUse
            Lecturas_pagina  = $mem.PagesInputPersec
            CPU_pct          = $cpu.PercentProcessorTime
            Disco_ocupado_pct= 100 - $disk.PercentIdleTime
            Disco_cola       = $disk.CurrentDiskQueueLength
            Ventana_app      = $fgProc
            Ventana_colgada  = $fgColgada
            Ventana_ms       = $fgMs
            Barra_ms         = $barraMs
            Barra_colgada    = $barraColgada
            Drive_avisos     = $drive
            Teclas_apretadas = [Ventana]::Apretadas()
            Captura_mouse    = $(
                $c = [Ventana]::Captura()
                if ($c -match 'captura:(\d+)') { $n = (Get-Process -Id $Matches[1] -ErrorAction SilentlyContinue).ProcessName; $c -replace 'captura:\d+', "captura:$n" } else { $c }
            )
            Mas_lectura      = $top.Lectura
            Mas_CPU          = $top.CPU
        }
        $fila | Export-Csv -Path $csv -Append -NoTypeInformation -Encoding UTF8
    } catch {
        Add-Content -Path (Join-Path $dir 'monitor-errores.txt') -Value ("{0:HH:mm:ss} {1}" -f $ahora, $_.Exception.Message)
    }
    Start-Sleep -Seconds $Segundos
}
