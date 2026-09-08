@echo off
setlocal EnableExtensions
rem Bridge Steam CoP → MMX-Net (path = cartella di questo .cmd)
set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"
set "FLAG=%ROOT%\ac_steam_launch.flag"
set "ARGSFILE=%ROOT%\ac_steam_launch.args"
set "ANOMALY=%ROOT%\bin\anomalydx11avx.exe"
if not exist "%FLAG%" goto :passthrough
del /f /q "%FLAG%" >nul 2>&1
cd /d "%ROOT%"
echo 41700>"%ROOT%\steam_appid.txt"
echo 41700>"%ROOT%\bin\steam_appid.txt"
set SteamAppId=41700
set SteamGameId=41700
set SteamOverlayGameId=41700
set "ARGS=-steam -smap2048"
if exist "%ARGSFILE%" set /p ARGS=<"%ARGSFILE%"
if exist "%ANOMALY%" goto :do_launch
for %%E in ("%ROOT%\bin\anomaly*.exe") do (
  set "ANOMALY=%%~fE"
  goto :do_launch
)
echo [MMX-Net] Client non trovato in bin\
exit /b 1
:do_launch
start "MMX-Net" /D "%ROOT%" "%ANOMALY%" %ARGS%
exit /b 0
:passthrough
rem Senza flag: Call of Pripyat normale
%*
