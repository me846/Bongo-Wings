@echo off
setlocal
cd /d "%~dp0"

set "CONFIG_EXE=KeyConfigApp\bin\WhiteBackground\ArcadeLeverKeyConfig.exe"
if exist "%CONFIG_EXE%" (
  start "" "%CONFIG_EXE%"
  exit /b 0
)

dotnet run --project KeyConfigApp\KeyConfigApp.csproj

if not %errorlevel%==0 (
  echo.
  echo Failed to start the WPF key config application.
  pause
)
