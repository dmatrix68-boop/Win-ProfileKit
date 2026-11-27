@echo off
rem Win-ProfileKit (framework-abhaengig): prueft die .NET-10-Runtime, installiert sie bei Bedarf
rem und startet dann die Oberflaeche. / Checks the .NET 10 runtime, installs it if needed, starts the GUI.
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-DotNetRuntime.ps1"
if errorlevel 1 (
  pause
  exit /b 1
)
start "" "%~dp0profilekit-gui.exe" %*
