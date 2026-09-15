@echo off
setlocal
set "BUILD_SCRIPT=%~dp0..\..\.cursor\skills\vs-build\scripts\build.py"

python "%BUILD_SCRIPT%" "%~dp0..\..\dlcv_infer_c_qt_demo\dlcv_infer_c_qt_demo.vcxproj" --configuration Release --platform x64 --target Build --verbosity minimal
if errorlevel 1 exit /b %errorlevel%

python "%BUILD_SCRIPT%" "%~dp0..\..\dlcv_infer_cpp_qt_demo\dlcv_infer_cpp_qt_demo.vcxproj" --configuration Release --platform x64 --target Build --verbosity minimal
if errorlevel 1 exit /b %errorlevel%

python "%BUILD_SCRIPT%" "%~dp0dlcv_infer_c_qt_mask_test.vcxproj" --configuration Release --platform x64 --target Build --verbosity minimal
if errorlevel 1 exit /b %errorlevel%

python "%BUILD_SCRIPT%" "%~dp0dlcv_infer_cpp_qt_mask_test.vcxproj" --configuration Release --platform x64 --target Build --verbosity minimal
if errorlevel 1 exit /b %errorlevel%

exit /b 0
