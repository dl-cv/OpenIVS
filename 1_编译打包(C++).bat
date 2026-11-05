@echo off
setlocal
set "PYTHONUTF8=1"
set "PYTHONIOENCODING=utf-8"
pushd "%~dp0" || exit /b 1
python -m build_script.build_cpp_package
set "BUILD_EXIT_CODE=%ERRORLEVEL%"
popd
pause
exit /b %BUILD_EXIT_CODE%
