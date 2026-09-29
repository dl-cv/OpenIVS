set "WHEEL="
for /f "delims=" %%F in ('dir /b /a-d /o-d "dist\*.whl" 2^>nul') do if not defined WHEEL set "WHEEL=%%F"
if not defined WHEEL (
    echo [错误] dist 目录中未找到 wheel 文件，无法上传
    pause
    exit /b 1
)
echo 将上传: dist\%WHEEL%
twine upload -r dlcvpro "dist\%WHEEL%"
set "UPLOAD_ERR=%ERRORLEVEL%"
pause
exit /b %UPLOAD_ERR%