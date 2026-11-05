@echo off
setlocal
set "PYTHONUTF8=1"
set "PYTHONIOENCODING=utf-8"
python "%~dp0build_cpp_package.py"
set "BUILD_EXIT_CODE=%ERRORLEVEL%"
pause
exit /b %BUILD_EXIT_CODE%
