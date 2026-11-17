@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-server-build.ps1" %*
set "SCRIPT_EXIT=%ERRORLEVEL%"
echo.
pause
exit /b %SCRIPT_EXIT%
