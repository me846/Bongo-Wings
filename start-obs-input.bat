@echo off
setlocal
cd /d "%~dp0"

where py >nul 2>nul
if %errorlevel%==0 (
  py -3 obs_input_server.py
) else (
  python obs_input_server.py
)

if not %errorlevel%==0 (
  echo.
  echo Failed to start the OBS input relay.
  pause
)
