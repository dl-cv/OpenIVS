@echo off
setlocal
set "PYTHONUTF8=1"
set "PYTHONIOENCODING=utf-8"
python "%~dp0build_package.py" --package cpp
set "BUILD_EXIT_CODE=%ERRORLEVEL%"
pause
exit /b %BUILD_EXIT_CODE%
