@echo off
setlocal
python -B "%~dp0build_package.py" build
exit /b %ERRORLEVEL%
