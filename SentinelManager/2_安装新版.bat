@echo off
setlocal
python -B "%~dp0build_package.py" install
exit /b %ERRORLEVEL%
