python "%~dp0sync_assembly_version.py"

"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" DlcvDemo\DlcvDemo.csproj /p:Configuration=Release /p:Platform=x64

rm -rf dlcvpro_infer_csharp
mkdir dlcvpro_infer_csharp

xcopy DlcvDemo\bin\*.exe dlcvpro_infer_csharp\ /Y
xcopy DlcvDemo\bin\*.config dlcvpro_infer_csharp\ /Y
xcopy DlcvDemo\bin\*.dll dlcvpro_infer_csharp\ /Y

echo 正在执行本地 NGEN 预编译（消除开发测试首次推理 JIT 开销）...
for %%f in (DlcvCsharpApi.dll OpenCvSharp.dll Newtonsoft.Json.dll) do (
    if exist "%~dp0dlcvpro_infer_csharp\%%f" (
        C:\Windows\Microsoft.NET\Framework64\v4.0.30319\ngen.exe install "%~dp0dlcvpro_infer_csharp\%%f"
    )
)

C:\sign-tool\signtool.exe sign /n 深度视觉（广东）人工智能研究有限公司 /t http://time.certum.pl /fd sha256 /v  "dlcvpro_infer_csharp\C# 测试程序.exe"

python -m build --wheel --outdir ./dist

pause

