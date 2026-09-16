# 只读取工程配置，不编译、不启动 Visual Studio，也不修改个人设置。
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$projectPath = Join-Path $PSScriptRoot 'DlcvCSharpCppTest.csproj'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$installation = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
if ($LASTEXITCODE -ne 0 -or -not $installation) { throw '未找到 Visual Studio MSBuild' }
$bin = Join-Path $installation 'MSBuild\Current\Bin'
$env:MSBUILD_EXE_PATH = Join-Path $bin 'MSBuild.exe'
# PowerShell 不使用 MSBuild.exe.config，需要从安装目录解析其程序集依赖。
$resolver = [ResolveEventHandler] {
    param($sender, $eventArgs)
    $path = Join-Path $bin (($eventArgs.Name.Split(',')[0]) + '.dll')
    if (Test-Path -LiteralPath $path) { return [Reflection.Assembly]::LoadFrom($path) }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
$collection = $null
try {
    foreach ($name in @('Microsoft.Build.Framework.dll', 'Microsoft.Build.dll')) {
        [void][Reflection.Assembly]::LoadFrom((Join-Path $bin $name))
    }
    $collection = New-Object Microsoft.Build.Evaluation.ProjectCollection
    $project = $collection.LoadProject($projectPath)
    $platforms = @($project.ConditionedProperties['Platform'])
    $configurations = @($project.ConditionedProperties['Configuration'])
    if ('x64' -notin $platforms) {
        throw '工程可枚举平台缺少 x64；只有 Platform 默认值不足以声明 IDE 项目配置'
    }
    foreach ($configuration in @('Debug', 'Release')) {
        if ($configuration -notin $configurations) { throw "工程缺少 $configuration 配置" }
    }
    $guid = $project.GetPropertyValue('ProjectGuid')
    $collection.UnloadAllProjects()
    $solution = [IO.File]::ReadAllText((Join-Path $root 'OpenIVS.sln'))
    $relative = 'Test\DlcvCSharpCppTest\DlcvCSharpCppTest.csproj'
    $declaration = 'Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "DlcvCSharpCppTest", "' + $relative + '", "' + $guid + '"'
    if ([regex]::Matches($solution, [regex]::Escape($declaration)).Count -ne 1) {
        throw '原解决方案必须登记且仅登记一次 C# 测试项目'
    }
    $profiles = Get-Content (Join-Path $root 'OpenIVS.slnLaunch') -Raw -Encoding UTF8 | ConvertFrom-Json
    $profile = @($profiles | Where-Object { $_.Name -eq 'C# 与 C++ 混编模型测试' })
    if ($profile.Count -ne 1 -or @($profile[0].Projects).Count -ne 1 -or
        $profile[0].Projects[0].Path -ne $relative -or $profile[0].Projects[0].Action -ne 'Start') {
        throw '共享启动配置必须只启动 C# 应用'
    }
    $results = @()
    foreach ($configuration in @('Debug', 'Release')) {
        foreach ($suffix in @('ActiveCfg', 'Build.0')) {
            $mapping = "$guid.$configuration|x64.$suffix = $configuration|x64"
            if (-not $solution.Contains($mapping)) { throw "解决方案缺少配置映射：$mapping" }
        }
        $properties = New-Object 'System.Collections.Generic.Dictionary[string,string]'
        $properties.Add('Configuration', $configuration)
        $properties.Add('Platform', 'x64')
        $properties.Add('BuildingInsideVisualStudio', 'true')
        $project = $collection.LoadProject($projectPath, $properties, $collection.DefaultToolsVersion)
        $expected = @{
            OutputType = 'WinExe'; PlatformTarget = 'x64'; Prefer32Bit = 'false'
            StartAction = 'Project'; StartupObject = 'DlcvCSharpCppTest.Program'
            EnableUnmanagedDebugging = 'true'; TargetFrameworkVersion = 'v4.7.2'
        }
        foreach ($name in $expected.Keys) {
            if ($project.GetPropertyValue($name) -ne $expected[$name]) { throw "$configuration 的 $name 不正确" }
        }
        $target = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "bin\x64\$configuration\DlcvCSharpCppTest.exe"))
        if ($project.GetPropertyValue('TargetPath') -ne $target) { throw "$configuration 启动产物路径不正确" }
        $results += @{ configuration = $configuration; platform = 'x64'; target = $target }
        $collection.UnloadAllProjects()
    }
    @{ status = 'passed'; configurations = $configurations; platforms = $platforms
       targets = $results; scope = 'project-configuration-only'; ide_debugger_tested = $false } | ConvertTo-Json -Depth 5
}
finally {
    if ($null -ne $collection) { $collection.UnloadAllProjects(); $collection.Dispose() }
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
