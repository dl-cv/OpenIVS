@echo off
chcp 936 >nul
setlocal EnableExtensions
cd /d "%~dp0"

set "ROOT=%~dp0"
set "PROJ=%ROOT%DlcvTest\DlcvTest.csproj"
set "BUILD_PY=%ROOT%.cursor\skills\vs-build\scripts\build.py"
set "OUT_DIR=%ROOT%DlcvTest\bin\x64\Release"
set "SEVEN_Z=C:\Program Files\7-Zip\7z.exe"

for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd"') do set "DATE_TAG=%%i"
if not defined DATE_TAG (
    echo [错误] 无法获取日期
    goto :fail
)
set "ZIP_PATH=%ROOT%DlcvTest_%DATE_TAG%.zip"

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
echo [2/2] 7z 打包到仓库根目录 DlcvTest_%DATE_TAG%.zip ...
echo      排除: dll\ 目录、*.pdb、OpenCvSharpExtern.dll、opencv_videoio_ffmpeg*.dll
if exist "%ZIP_PATH%" del /f /q "%ZIP_PATH%"

pushd "%OUT_DIR%"
if errorlevel 1 (
    echo [错误] 无法进入输出目录: %OUT_DIR%
    goto :fail
)

"%SEVEN_Z%" a -tzip -mx=9 "%ZIP_PATH%" * ^
  -x!*.pdb ^
  -x!*.xml ^
  -xr!dll ^
  -x!OpenCvSharpExtern.dll ^
  -x!opencv_videoio_ffmpeg4100_64.dll ^
  -x!opencv_videoio_ffmpeg*.dll
set "PACK_ERR=%ERRORLEVEL%"
popd
if not "%PACK_ERR%"=="0" (
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
