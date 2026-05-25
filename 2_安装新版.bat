cd dist
for /f "delims=" %%a in ('dir /b /od *.whl') do @set "LATEST=%%a"
pip uninstall -y dlcvpro_infer_csharp
pip install -U %LATEST%

echo 正在执行 NGEN 预编译（消除首次推理 JIT 开销）...
python "%~dp0ngen_installed_dlls.py"

pause