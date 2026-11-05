$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$projectPath = Join-Path $root 'Test\SentinelManager\SentinelManager.csproj'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$installation = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
if ($LASTEXITCODE -ne 0 -or -not $installation) { throw '未找到 Visual Studio MSBuild' }
$bin = Join-Path $installation 'MSBuild\Current\Bin'
$env:MSBUILD_EXE_PATH = Join-Path $bin 'MSBuild.exe'
# PowerShell 不使用 MSBuild.exe.config，需要从安装目录解析程序集依赖。
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
    Write-Output ('可枚举平台：' + ($platforms -join ', '))
    Write-Output ('可枚举配置：' + ($configurations -join ', '))
    if ('x64' -notin $platforms) { throw 'IDE 工程配置缺少 x64 平台声明' }
    $guid = $project.GetPropertyValue('ProjectGuid')
    $collection.UnloadAllProjects()
    $solution = [IO.File]::ReadAllText((Join-Path $root 'OpenIVS.sln'))
    $targets = @()
    foreach ($configuration in @('Debug', 'Release')) {
        if ($configuration -notin $configurations) { throw "缺少配置：$configuration" }
        foreach ($suffix in @('ActiveCfg', 'Build.0')) {
            if (-not $solution.Contains("$guid.$configuration|x64.$suffix = $configuration|x64")) {
                throw "解决方案缺少配置映射：$configuration|x64.$suffix"
            }
        }
        $properties = New-Object 'System.Collections.Generic.Dictionary[string,string]'
        $properties.Add('Configuration', $configuration)
        $properties.Add('Platform', 'x64')
        $properties.Add('BuildingInsideVisualStudio', 'true')
        $project = $collection.LoadProject($projectPath, $properties, $collection.DefaultToolsVersion)
        if ($project.GetPropertyValue('OutputType') -ne 'WinExe' -or
            $project.GetPropertyValue('PlatformTarget') -ne 'x64') { throw '工程必须生成 x64 Windows 应用程序' }
        $target = Join-Path (Split-Path $projectPath -Parent) "bin\$configuration\SentinelManager.exe"
        if ($project.GetPropertyValue('TargetPath') -ne $target) { throw "$configuration 的启动产物路径不正确" }
        $targets += @{ configuration = $configuration; platform = 'x64'; target = $target }
        $collection.UnloadAllProjects()
    }
    @{ status = 'passed'; targets = $targets; ide_debugger_tested = $false } | ConvertTo-Json -Depth 4
}
finally {
    if ($null -ne $collection) { $collection.UnloadAllProjects(); $collection.Dispose() }
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
