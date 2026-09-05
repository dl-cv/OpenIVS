@echo off
python "%~dp0build_package.py"
set "BUILD_EXIT_CODE=%ERRORLEVEL%"
pause
exit /b %BUILD_EXIT_CODE%
