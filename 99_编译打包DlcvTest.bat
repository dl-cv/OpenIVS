@echo off
chcp 936 >nul
setlocal EnableExtensions
cd /d "%~dp0"

set "ROOT=%~dp0"
set "PROJ=%ROOT%DlcvTest\DlcvTest.csproj"
set "BUILD_PY=%ROOT%.cursor\skills\vs-build\scripts\build.py"
set "OUT_DIR=%ROOT%DlcvTest\bin\x64\Release"
set "ZIP_PATH=%ROOT%DlcvTest.zip"
set "SEVEN_Z=C:\Program Files\7-Zip\7z.exe"

echo ========================================
echo  编译打包 DlcvTest (Release x64)
echo ========================================
echo.

if not exist "%PROJ%" (
    echo [错误] 未找到项目文件: %PROJ%
    goto :fail
)

if not exist "%BUILD_PY%" (
    echo [错误] 未找到编译脚本: %BUILD_PY%
    goto :fail
)

if not exist "%SEVEN_Z%" (
    echo [错误] 未找到 7-Zip: %SEVEN_Z%
    goto :fail
)

echo [1/2] Release x64 编译 DlcvTest ...
python "%BUILD_PY%" "%PROJ%" --configuration Release --platform x64 --target Build --verbosity minimal
if errorlevel 1 (
    echo [错误] 编译失败
    goto :fail
)

if not exist "%OUT_DIR%\DlcvTest.exe" (
    echo [错误] 未找到编译产物: %OUT_DIR%\DlcvTest.exe
    goto :fail
)

echo.
echo [2/2] 7z 打包到仓库根目录 DlcvTest.zip ...
if exist "%ZIP_PATH%" del /f /q "%ZIP_PATH%"
"%SEVEN_Z%" a -tzip -mx=9 "%ZIP_PATH%" "%OUT_DIR%\*" -x!*.pdb -x!*.xml
if errorlevel 1 (
    echo [错误] 打包失败
    goto :fail
)

echo.
echo ========================================
echo  完成
echo  产物目录: %OUT_DIR%
echo  压缩包:   %ZIP_PATH%
echo ========================================
echo.
pause
endlocal
exit /b 0

:fail
echo.
echo 失败，已中止。
pause
endlocal
exit /b 1
