[CmdletBinding()]
param(
    [string]$LogPath,
    [string]$SentinelDllPath,
    [string]$VirboxDllPath
)

$ErrorActionPreference = "Stop"
$testExePath = Join-Path $PSScriptRoot "bin\x64\Release\DlcvCSharpTest.exe"
if (-not (Test-Path -LiteralPath $testExePath -PathType Leaf)) {
    Write-Error "测试程序不存在，请先运行编号构建脚本生成 Release|x64：$testExePath"
    exit 2
}

$testRunId = [Guid]::NewGuid().ToString("N")
$testTempDirectory = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
if ([string]::IsNullOrWhiteSpace($LogPath)) {
    $LogPath = Join-Path $testTempDirectory ("OpenIVS-DlcvCSharpTest-" + $testRunId + "-all-tests.log")
}

$finalLogPath = [IO.Path]::GetFullPath($LogPath)
$tempPrefix = $testTempDirectory.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $finalLogPath.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    Write-Output "日志路径必须位于系统临时目录：$testTempDirectory"
    exit 2
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($finalLogPath)) | Out-Null

$testTempPrefix = Join-Path $testTempDirectory ("OpenIVS-DlcvCSharpTest-" + $testRunId)
$managedLogPath = $testTempPrefix + "-managed.log"
$stdoutLogPath = $testTempPrefix + "-stdout.log"
$stderrLogPath = $testTempPrefix + "-stderr.log"
$testProcess = $null

$hasSentinelDll = -not [string]::IsNullOrWhiteSpace($SentinelDllPath)
$hasVirboxDll = -not [string]::IsNullOrWhiteSpace($VirboxDllPath)
if ($hasSentinelDll -ne $hasVirboxDll) {
    Write-Output "指定推理 DLL 时必须同时提供 SentinelDllPath 和 VirboxDllPath。"
    exit 2
}

$allTestsArguments = @("all-tests", ('"' + $managedLogPath + '"'))
if ($hasSentinelDll) {
    $allTestsArguments += ('"' + [IO.Path]::GetFullPath($SentinelDllPath) + '"')
    $allTestsArguments += ('"' + [IO.Path]::GetFullPath($VirboxDllPath) + '"')
}

$strictUtf8 = New-Object Text.UTF8Encoding($false, $true)

function Read-Utf8Text {
    param([string]$Path)
    if (-not [IO.File]::Exists($Path)) {
        throw "结果文件不存在：$Path"
    }
    return $strictUtf8.GetString([IO.File]::ReadAllBytes($Path))
}

# 原生标准流包含 UTF-8 和 GB18030；逐行严格解码，托管详细日志保持严格 UTF-8。
function Read-NativeText {
    param([string]$Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    $nativeEncoding = [Text.Encoding]::GetEncoding(54936, [Text.EncoderFallback]::ExceptionFallback, [Text.DecoderFallback]::ExceptionFallback)
    $text = New-Object Text.StringBuilder
    $start = 0
    for ($index = 0; $index -le $bytes.Length; $index++) {
        if ($index -lt $bytes.Length -and $bytes[$index] -ne 10) { continue }
        $length = $index - $start
        try { $line = $strictUtf8.GetString($bytes, $start, $length) }
        catch { $line = $nativeEncoding.GetString($bytes, $start, $length) }
        $null = $text.Append($line)
        if ($index -lt $bytes.Length) { $null = $text.Append("`n") }
        $start = $index + 1
    }
    return $text.ToString()
}

function Add-Utf8Text {
    param(
        [IO.Stream]$Destination,
        [string]$Text
    )
    $bytes = $strictUtf8.GetBytes($Text)
    $Destination.Write($bytes, 0, $bytes.Length)
}

function Add-FileContent {
    param(
        [IO.Stream]$Destination,
        [string]$SourcePath
    )
    $source = [IO.File]::OpenRead($SourcePath)
    try {
        $source.CopyTo($Destination)
    }
    finally {
        $source.Dispose()
    }
}

try {
    $testProcess = Start-Process `
        -FilePath $testExePath `
        -ArgumentList $allTestsArguments `
        -WindowStyle Hidden `
        -RedirectStandardOutput $stdoutLogPath `
        -RedirectStandardError $stderrLogPath `
        -Wait `
        -PassThru

    $finalLog = [IO.File]::Open($finalLogPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try {
        Add-Utf8Text $finalLog "==== 程序标准输出 ====`r`n"
        Add-FileContent $finalLog $stdoutLogPath
        Add-Utf8Text $finalLog "`r`n==== 程序标准错误 ====`r`n"
        Add-FileContent $finalLog $stderrLogPath
        Add-Utf8Text $finalLog "`r`n==== 各项测试详细输出 ====`r`n"
        Add-FileContent $finalLog $managedLogPath
    }
    finally {
        $finalLog.Dispose()
    }

    $stdoutText = Read-NativeText $stdoutLogPath
    $null = Read-NativeText $stderrLogPath
    $null = Read-Utf8Text $managedLogPath

    $stdoutLines = $stdoutText -split "`r?`n"
    $summaryStart = -1
    for ($index = $stdoutLines.Length - 1; $index -ge 0; $index--) {
        if ($stdoutLines[$index] -eq "==== C# 统一测试汇总 ====") {
            $summaryStart = $index
            break
        }
    }

    if ($summaryStart -ge 0) {
        for ($index = $summaryStart; $index -lt $stdoutLines.Length; $index++) {
            $line = $stdoutLines[$index]
            if ($line.StartsWith("详细日志:")) {
                Write-Output ("详细日志: " + $finalLogPath)
                break
            }
            Write-Output $line
        }
    }
    else {
        Write-Output "统一测试未生成汇总信息"
        Write-Output ("详细日志: " + $finalLogPath)
    }

    if ($testProcess.ExitCode -eq 0 -and $summaryStart -lt 0) {
        Write-Output "测试程序未提供完整汇总，不能标为通过。"
        exit 1
    }

    exit $testProcess.ExitCode
}
catch {
    Write-Output ("测试结果读取失败：" + $_.Exception.Message)
    if ($testProcess -ne $null -and $testProcess.ExitCode -ne 0) {
        exit $testProcess.ExitCode
    }
    exit 1
}
finally {
    foreach ($temporaryPath in @($managedLogPath, $stdoutLogPath, $stderrLogPath)) {
        if ([IO.File]::Exists($temporaryPath)) {
            [IO.File]::Delete($temporaryPath)
        }
    }
}
