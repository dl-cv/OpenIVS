@echo off
setlocal
pushd "%~dp0" || exit /b 1
python -m build_script.build_package
set "BUILD_EXIT_CODE=%ERRORLEVEL%"
popd
pause
exit /b %BUILD_EXIT_CODE%
