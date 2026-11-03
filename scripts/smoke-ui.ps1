param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [int]$RenderWidth=1360,
    [int]$RenderHeight=960,
    [int]$RenderDpi=96,
    [switch]$ExportOptions
)
$ErrorActionPreference='Stop'
$smokeRoot=Join-Path ([System.IO.Path]::GetTempPath()) ('OWExtractor-ui-'+[guid]::NewGuid().ToString('N'))
$exportRoot=Join-Path $smokeRoot 'exports'
$skinRoot=Join-Path $exportRoot 'Heroes/Test Hero/Skin/Test Season/Test Skin'
New-Item -ItemType Directory -Path $skinRoot,$OutputDirectory -Force | Out-Null
# A synthetic box in OWMDL 2.0: no game data or external tools.
$model=Join-Path $skinRoot 'synthetic.owmdl'
$writer=[System.IO.BinaryWriter]::new([System.IO.File]::Create($model))
try {
    $writer.Write([uint16]2); $writer.Write([uint16]0); $writer.Write(''); $writer.Write('synthetic-box')
    $writer.Write([uint32]0); $writer.Write([uint16]0); $writer.Write([uint32]1); $writer.Write([uint32]0)
    $writer.Write('box'); $writer.Write([uint64]1); $writer.Write([byte]1); $writer.Write([uint32]8); $writer.Write([uint32]12); $writer.Write([sbyte]0)
    foreach($v in @(-0.5,-0.3,0, 0.5,-0.3,0, 0.5,0.3,0, -0.5,0.3,0, -0.5,-0.3,1, 0.5,-0.3,1, 0.5,0.3,1, -0.5,0.3,1)){ $writer.Write([single]$v) }
    foreach($i in 0..7){ $writer.Write([single]0); $writer.Write([single]-1); $writer.Write([single]0) }
    foreach($i in 1..32){ $writer.Write([single]0) }
    foreach($i in 1..16){ $writer.Write([single]0) }
    foreach($i in 1..64){ $writer.Write([single]1) }
    foreach($i in @(0,1,5, 0,5,4, 1,2,6, 1,6,5, 2,3,7, 2,7,6, 3,0,4, 3,4,7, 4,5,6, 4,6,7, 3,2,1, 3,1,0)){ $writer.Write([uint32]$i) }
} finally { $writer.Dispose() }
$heroes=Join-Path $smokeRoot 'heroes.json'
$localized=Join-Path $smokeRoot 'heroes-zh.json'
'{"test-guid":{"GUID":"test-guid","Name":"Test Hero","Class":"Support","IsHero":true}}' | Set-Content -LiteralPath $heroes -Encoding utf8
'{"test-guid":{"GUID":"test-guid","Name":"测试英雄","Class":"Support","IsHero":true}}' | Set-Content -LiteralPath $localized -Encoding utf8
$profile=Join-Path $smokeRoot 'settings.local.json'
$preview=Join-Path $smokeRoot 'preview.png'
[System.IO.File]::WriteAllBytes($preview,[Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a9XcAAAAASUVORK5CYII='))
$image=Join-Path ($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)) 'ui-smoke.png'
$arguments=@('--offline','--profile',('"'+$profile+'"'),'--import-heroes',('"'+$heroes+'"'),'--localized-heroes',('"'+$localized+'"'),'--import-export',('"'+$exportRoot+'"'),'--preview-front',('"'+$preview+'"'),'--preview-back',('"'+$preview+'"'),'--preview-model',('"'+$model+'"'),'--verify-ui','--render-width',$RenderWidth,'--render-height',$RenderHeight,'--render-dpi',$RenderDpi,'--render',('"'+$image+'"'))
if($ExportOptions){$arguments+='--render-export-options'}
$process=Start-Process -FilePath ($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Executable)) -ArgumentList $arguments -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit(60000)){ $process.Kill(); throw 'UI smoke timed out' }
if($process.ExitCode -ne 0){throw ('UI smoke failed: '+(Get-Content -LiteralPath ($image+'.error.txt') -Raw -ErrorAction SilentlyContinue))}
$report=Get-Content -LiteralPath ($image+'.json') -Raw | ConvertFrom-Json
if(-not $report.offline -or -not $report.offlineLocked -or $report.engineStarted -or -not $report.verifiedWorkflow -or -not $report.has3D -or -not $report.hasPreview -or -not $report.hasBackPreview -or $report.heroes -ne 1 -or $report.skins -ne 1){throw 'Unexpected UI smoke result'}
if($report.nativeWindowCreated -or -not $report.softwareRendering -or $report.bindingErrors.Count -ne 0 -or $report.renderDpi -ne $RenderDpi){throw 'Off-screen/software/DPI invariant failed'}
if($ExportOptions -and $report.inspector -ne 'export'){throw 'Export options were not rendered'}
$report | ConvertTo-Json
