@echo off
REM Agent Runner Windows Service Installer
REM Run as Administrator

set SERVICE_NAME=AgentRunner
set DISPLAY_NAME="Crypton Agent Runner"
set DESCRIPTION="Agent Runner - Crypton Learning Loop Orchestration Service"
set EXE_PATH=%~dp0AgentRunner.exe

echo Installing %SERVICE_NAME%...
sc create %SERVICE_NAME% binPath= "%EXE_PATH%" DisplayName= %DISPLAY_NAME%
sc description %SERVICE_NAME% "%DESCRIPTION%"
sc config %SERVICE_NAME% start= auto

echo.
echo Service installed. To start:
echo   sc start %SERVICE_NAME%
echo.
echo To uninstall:
echo   sc stop %SERVICE_NAME%
echo   sc delete %SERVICE_NAME%
