@echo off
setlocal
set "BUILD_SCRIPT=%~dp0..\.cursor\skills\vs-build\scripts\build.py"

python "%BUILD_SCRIPT%" "%~dp0dlcv_infer_cpp_test\dlcv_infer_cpp_test.vcxproj" --configuration Release --platform x64 --target Build --verbosity minimal
if errorlevel 1 exit /b %errorlevel%

python "%BUILD_SCRIPT%" "%~dp0dlcv_infer_c_test\dlcv_infer_c_test.vcxproj" --configuration Release --platform x64 --target Build --verbosity minimal
if errorlevel 1 exit /b %errorlevel%

python "%BUILD_SCRIPT%" "%~dp0DlcvCSharpTest\DlcvCSharpTest.csproj" --configuration Release --platform x64 --target Build --verbosity minimal
if errorlevel 1 exit /b %errorlevel%

python "%BUILD_SCRIPT%" "%~dp0DlcvCSharpCppTest\DlcvCSharpCppTest.csproj" --configuration Release --platform x64 --target Build --verbosity minimal
if errorlevel 1 exit /b %errorlevel%

exit /b 0
