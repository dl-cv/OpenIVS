@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "PYTHONUTF8=1"
set "PYTHONIOENCODING=utf-8"
cd /d "%~dp0dist_cpp" || exit /b 1
set "WHEEL="
for /f "delims=" %%F in ('dir /b /a-d /o-d dlcvpro_infer_cpp-*.whl 2^>nul') do if not defined WHEEL set "WHEEL=%%F"
if not defined WHEEL exit /b 1
python -m twine upload -r dlcvpro "%WHEEL%"
set "UPLOAD_ERR=%ERRORLEVEL%"
pause
exit /b %UPLOAD_ERR%
