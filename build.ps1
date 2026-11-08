# Compila PulsoPC.exe (un solo archivo, sin dependencias).
#   .\build.ps1            -> release (pide administrador al abrir) en esta carpeta
#   .\build.ps1 -Test      -> version de prueba (sin UAC) en -Out
param([switch]$Test, [string]$Out)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = @(
  "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe",
  "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe",
  "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\Roslyn\csc.exe",
  "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\Roslyn\csc.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { throw "No se encontro el compilador Roslyn (Visual Studio Build Tools 2022)." }
if (-not (Test-Path "$root\assets\pulso.ico")) { & "$root\make-icon.ps1" | Out-Null }
$level = if ($Test) { 'asInvoker' } else { 'requireAdministrator' }
$tmp = Join-Path ([IO.Path]::GetTempPath()) 'pulsopc-build'
New-Item -ItemType Directory -Force $tmp | Out-Null
$man = Join-Path $tmp 'app.manifest'
(Get-Content "$root\app.manifest" -Raw).Replace('@@LEVEL@@',$level) | Set-Content $man -Encoding UTF8
if (-not $Out) { $Out = Join-Path $root 'PulsoPC.exe' }
$fx = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$refs = 'System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Management.dll','System.ServiceProcess.dll','Microsoft.VisualBasic.dll','Microsoft.CSharp.dll'
$a = @('-nologo','-target:winexe','-platform:anycpu','-optimize+','-codepage:65001','-langversion:latest',"-out:$Out","-win32manifest:$man","-win32icon:$root\assets\pulso.ico")
$a += $refs | ForEach-Object { "-r:$fx\$_" }
$a += (Get-ChildItem "$root\src" -Recurse -Filter *.cs | ForEach-Object { $_.FullName })
& $csc @a
if ($LASTEXITCODE -ne 0) { throw "Fallo la compilacion ($LASTEXITCODE)" }
"OK -> $Out ($([math]::Round((Get-Item $Out).Length/1KB)) KB, nivel: $level)"
