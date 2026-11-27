#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ReferenceDirectory
)

$ErrorActionPreference = 'Stop'
$referenceFolder = Get-Item -LiteralPath $ReferenceDirectory
if (!$referenceFolder.PSIsContainer) { throw 'ReferenceDirectory 必须是现有的 DlcvDemo 构建输出目录。' }

$sdkCandidates = @(dotnet --list-sdks | ForEach-Object {
    if ($_ -match '^(\d+\.\d+\.\d+) \[(.+)\]$') {
        $version = [version]$Matches[1]
        $directory = Join-Path $Matches[2] "$version/Roslyn/bincore"
        if ($version.Major -le [Environment]::Version.Major -and
            (Test-Path -LiteralPath (Join-Path $directory 'Microsoft.CodeAnalysis.CSharp.dll') -PathType Leaf)) {
            [pscustomobject]@{ Version = $version; Directory = $directory }
        }
    }
})
if ($LASTEXITCODE -ne 0) { throw '读取已安装的 .NET SDK 失败。' }
$roslyn = $sdkCandidates | Sort-Object Version -Descending | Select-Object -First 1
if (!$roslyn) { throw '没有找到与当前 PowerShell 运行环境兼容的 .NET SDK Roslyn。' }
[Reflection.Assembly]::LoadFrom((Join-Path $roslyn.Directory 'Microsoft.CodeAnalysis.dll')) | Out-Null
[Reflection.Assembly]::LoadFrom((Join-Path $roslyn.Directory 'Microsoft.CodeAnalysis.CSharp.dll')) | Out-Null

$net = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies/Microsoft/Framework/.NETFramework/v4.7.2'
if (!(Test-Path -LiteralPath $net -PathType Container)) { throw '未找到 .NET Framework 4.7.2 参考程序集。' }
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$project = Join-Path $root 'DlcvDemo/DlcvDemo.csproj'
[xml]$xml = Get-Content -LiteralPath $project -Raw
$sources = @($xml.DocumentElement.ItemGroup.Compile | ForEach-Object {
    if ($_.Include) {
        [IO.Path]::GetFullPath([IO.Path]::Combine([IO.Path]::GetDirectoryName($project), $_.Include))
    }
})
$sources += Join-Path $root 'PressureTestRunner/PressureTestRunner.cs'

$names = 'mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Drawing.dll',
         'System.Windows.Forms.dll', 'System.Management.dll', 'System.Configuration.dll',
         'System.Net.Http.dll', 'System.Xml.dll', 'System.Xml.Linq.dll', 'Microsoft.CSharp.dll',
         'Facades/netstandard.dll'
$refs = @($names | ForEach-Object { Join-Path $net $_ })
$refs += @(Get-ChildItem -LiteralPath $referenceFolder.FullName -File -Filter '*.dll' |
    Where-Object Name -ne 'PressureTestRunner.dll' | ForEach-Object {
        try {
            [Reflection.AssemblyName]::GetAssemblyName($_.FullName) | Out-Null
            $_.FullName
        }
        catch [BadImageFormatException] {}
    })

$trees = [Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
$metadata = [Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
$parse = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::new(
    [Microsoft.CodeAnalysis.CSharp.LanguageVersion]::CSharp7_3)
$none = [Threading.CancellationToken]::None
foreach ($source in $sources) {
    $trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText(
        [IO.File]::ReadAllText($source), $parse, $source, [Text.Encoding]::UTF8, $none))
}
foreach ($reference in $refs) {
    $metadata.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($reference))
}
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new(
    [Microsoft.CodeAnalysis.OutputKind]::WindowsApplication)
$check = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create(
    'DemoSourceCheck', $trees, $metadata, $options)
$errors = @($check.GetDiagnostics($none) | Where-Object Severity -eq Error)
if ($errors.Count) {
    $errors | ForEach-Object ToString
    exit 1
}
Write-Output "C# 7.3 源码语义检查通过：Roslyn SDK $($roslyn.Version)，$($sources.Count) 个源文件，0 个错误。"
