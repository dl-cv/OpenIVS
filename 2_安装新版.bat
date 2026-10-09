@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "TARGET="
if /i "%~1"=="--target" (
    if "%~2"=="" exit /b 2
    set "TARGET=%~f2"
)
cd /d "%~dp0dist" || exit /b 1
set "LATEST="
for /f "delims=" %%a in ('dir /b /a-d /od *.whl 2^>nul') do @set "LATEST=%%a"
if not defined LATEST exit /b 1
if defined TARGET goto install_target
python -m pip uninstall -y dlcvpro_infer_csharp
if errorlevel 1 exit /b %errorlevel%
python -m pip install -U "%LATEST%"
set "RESULT=%errorlevel%"
pause
exit /b %RESULT%

:install_target
python -m pip install --no-deps --upgrade --target "%TARGET%" "%LATEST%"
exit /b %errorlevel%
