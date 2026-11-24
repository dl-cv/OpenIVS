@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "TARGET="
if /i "%~1"=="--target" (
    if "%~2"=="" exit /b 2
    set "TARGET=%~f2"
)
cd /d "%~dp0dist_cpp" || exit /b 1
set "LATEST="
for /f "delims=" %%a in ('dir /b /a-d /od dlcvpro_infer_cpp-*.whl 2^>nul') do @set "LATEST=%%a"
if not defined LATEST exit /b 1
set "RUNTIME="
for /f "tokens=2 delims=-" %%V in ("%LATEST%") do set "RUNTIME=dlcv_datamatrix_runtime-%%V-py3-none-win_amd64.whl"
if not exist "%RUNTIME%" exit /b 1
if defined TARGET goto install_target
python -m pip install --force-reinstall "%LATEST%" "%RUNTIME%"
set "RESULT=%errorlevel%"
pause
exit /b %RESULT%

:install_target
python -m pip install --no-deps --upgrade --target "%TARGET%" "%LATEST%" "%RUNTIME%"
exit /b %errorlevel%
