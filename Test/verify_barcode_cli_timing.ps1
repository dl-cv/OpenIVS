param([Parameter(Mandatory=$true)][string]$DemoPath)
$ErrorActionPreference = 'Stop'
$demo = (Resolve-Path -LiteralPath $DemoPath).Path
$folder = Split-Path $demo
[void][Reflection.Assembly]::LoadFrom((Join-Path $folder 'Newtonsoft.Json.dll'))
$assembly = [Reflection.Assembly]::LoadFrom($demo)
$runner = $assembly.GetType('DlcvDemo.CliRunner', $true)
$flags = [Reflection.BindingFlags]'NonPublic,Static'
$compare = $runner.GetMethod('AreConsistent', $flags)
$summaryType = $runner.GetNestedType('PathSummary', [Reflection.BindingFlags]::NonPublic)
$add = $summaryType.GetMethod('Add')
function New-Summary([string]$json) {
    $summary = [Activator]::CreateInstance($summaryType, $true)
    $extra = [Newtonsoft.Json.Linq.JObject]::Parse($json)
    [void]$add.Invoke($summary, [object[]]@(1.0, 'TEST', $false, $extra, 0.5))
    return $summary
}
$baseline = New-Summary '{"barcode_format":"Code 128","barcode_enhance_mode":"central-p35","barcode_enhance_ms":1.2,"polyline":[[0,0],[10,0]]}'
$differentTime = New-Summary '{"barcode_format":"Code 128","barcode_enhance_mode":"central-p35","barcode_enhance_ms":8.9,"polyline":[[0,0],[10,0]]}'
if (-not $compare.Invoke($null, [object[]]@($baseline, $differentTime))) { throw 'Only elapsed time differs, but CLI rejects the result' }
foreach ($json in @(
    '{"barcode_format":"Code 39","barcode_enhance_mode":"central-p35","barcode_enhance_ms":1.2,"polyline":[[0,0],[10,0]]}',
    '{"barcode_format":"Code 128","barcode_enhance_mode":"upper-p35","barcode_enhance_ms":1.2,"polyline":[[0,0],[10,0]]}',
    '{"barcode_format":"Code 128","barcode_enhance_mode":"central-p35","barcode_enhance_ms":1.2,"polyline":[[0,0],[11,0]]}',
    '{"barcode_format":"Code 128","barcode_enhance_mode":"central-p35","barcode_enhance_ms":1.2,"polyline":[[0,0],[10,0]],"other_ms":9}'
)) {
    if ($compare.Invoke($null, [object[]]@($baseline, (New-Summary $json)))) { throw 'Non-timing difference was ignored' }
}
if (-not ($baseline.ExtraInfos[0].ToString().Contains('1.2'))) { throw 'Source timing changed' }
Write-Output 'PASS: timing ignored only for known enhancement fields; metadata and source retained'
