"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" DlcvDemo\DlcvDemo.csproj /p:Configuration=Release /p:Platform=x64

rm -rf dlcvpro_infer_csharp
mkdir dlcvpro_infer_csharp

xcopy DlcvDemo\bin\*.exe dlcvpro_infer_csharp\ /Y
xcopy DlcvDemo\bin\*.config dlcvpro_infer_csharp\ /Y
xcopy DlcvDemo\bin\*.dll dlcvpro_infer_csharp\ /Y

C:\sign-tool\signtool.exe sign /n ����������Ӿ��Ƽ����޹�˾ /t http://time.certum.pl /fd sha256 /v  "dlcvpro_infer_csharp\C# ���Գ���.exe"

python -m build --wheel --outdir ./dist

pause

