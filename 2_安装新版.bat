cd dist
for /f "delims=" %%a in ('dir /b /od *.whl') do @set "LATEST=%%a"
pip uninstall -y dlcvpro_infer_csharp
pip install -U %LATEST%
pause