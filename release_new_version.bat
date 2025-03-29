"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" DlcvDemo\DlcvDemo.csproj /p:Configuration=Release /p:Platform=x64

xcopy DlcvDemo\bin\*.exe dlcvpro_infer_csharp\ /Y
xcopy DlcvDemo\bin\*.config dlcvpro_infer_csharp\ /Y
xcopy DlcvDemo\bin\*.dll dlcvpro_infer_csharp\ /Y
xcopy DlcvDemo\bin\dll\x64\OpenCvSharpExtern.dll dlcvpro_infer_csharp\ /Y

python -m build --wheel --outdir ./dist

twine upload -r dlcvpro dist/*

pause
