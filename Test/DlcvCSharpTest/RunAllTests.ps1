[CmdletBinding()]
param(
    [string]$LogPath
)

$ErrorActionPreference = "Stop"
$testExePath = Join-Path $PSScriptRoot "bin\x64\Release\DlcvCSharpTest.exe"
if (-not (Test-Path -LiteralPath $testExePath -PathType Leaf)) {
    Write-Error "测试程序不存在，请先构建 Release|x64：$testExePath"
    exit 2
}

if ([string]::IsNullOrWhiteSpace($LogPath)) {
    $LogPath = Join-Path $PSScriptRoot "bin\x64\Release\DlcvCSharpTest-all-tests.log"
}

$finalLogPath = [IO.Path]::GetFullPath($LogPath)
$finalLogDirectory = [IO.Path]::GetDirectoryName($finalLogPath)
if (-not [string]::IsNullOrEmpty($finalLogDirectory)) {
    [IO.Directory]::CreateDirectory($finalLogDirectory) | Out-Null
}

$testRunId = [Guid]::NewGuid().ToString("N")
$testTempDirectory = [IO.Path]::GetTempPath()
$testTempPrefix = Join-Path $testTempDirectory ("OpenIVS-DlcvCSharpTest-" + $testRunId)
$managedLogPath = $testTempPrefix + "-managed.log"
$stdoutLogPath = $testTempPrefix + "-stdout.log"
$stderrLogPath = $testTempPrefix + "-stderr.log"
$testProcess = $null

function Add-Utf8Text {
    param(
        [IO.Stream]$Destination,
        [string]$Text
    )

    $utf8 = New-Object Text.UTF8Encoding($false)
    $bytes = $utf8.GetBytes($Text)
    $Destination.Write($bytes, 0, $bytes.Length)
}

function Add-FileContent {
    param(
        [IO.Stream]$Destination,
        [string]$SourcePath
    )

    if (-not [IO.File]::Exists($SourcePath)) {
        return
    }
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
        -ArgumentList @("all-tests", ('"' + $managedLogPath + '"')) `
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

    $stdoutText = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($stdoutLogPath))
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

    exit $testProcess.ExitCode
}
finally {
    foreach ($temporaryPath in @($managedLogPath, $stdoutLogPath, $stderrLogPath)) {
        if ([IO.File]::Exists($temporaryPath)) {
            [IO.File]::Delete($temporaryPath)
        }
    }
}
