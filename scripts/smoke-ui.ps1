param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [int]$RenderWidth=1360,
    [int]$RenderHeight=960
)
$ErrorActionPreference='Stop'
$smokeRoot=Join-Path ([System.IO.Path]::GetTempPath()) ('OWExtractor-ui-'+[guid]::NewGuid().ToString('N'))
$exportRoot=Join-Path $smokeRoot 'exports'
$skinRoot=Join-Path $exportRoot 'Heroes/Test Hero/Skin/Test Season/Test Skin'
New-Item -ItemType Directory -Path $skinRoot,$OutputDirectory -Force | Out-Null
[System.IO.File]::WriteAllBytes((Join-Path $skinRoot 'synthetic.owmdl'),[byte[]](1,2,3))
$heroes=Join-Path $smokeRoot 'heroes.json'
$localized=Join-Path $smokeRoot 'heroes-zh.json'
'{"test-guid":{"GUID":"test-guid","Name":"Test Hero","Class":"Support","IsHero":true}}' | Set-Content -LiteralPath $heroes -Encoding utf8
'{"test-guid":{"GUID":"test-guid","Name":"测试英雄","Class":"Support","IsHero":true}}' | Set-Content -LiteralPath $localized -Encoding utf8
$profile=Join-Path $smokeRoot 'settings.local.json'
$image=Join-Path ([System.IO.Path]::GetFullPath($OutputDirectory)) 'ui-smoke.png'
$process=Start-Process -FilePath ([System.IO.Path]::GetFullPath($Executable)) -ArgumentList @('--profile',('"'+$profile+'"'),'--import-heroes',('"'+$heroes+'"'),'--localized-heroes',('"'+$localized+'"'),'--import-export',('"'+$exportRoot+'"'),'--verify-ui','--render-width',$RenderWidth,'--render-height',$RenderHeight,'--render',('"'+$image+'"')) -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit(60000)){ $process.Kill(); throw 'UI smoke timed out' }
if($process.ExitCode -ne 0){throw ('UI smoke failed: '+(Get-Content -LiteralPath ($image+'.error.txt') -Raw -ErrorAction SilentlyContinue))}
$report=Get-Content -LiteralPath ($image+'.json') -Raw | ConvertFrom-Json
if(-not $report.offline -or $report.engineStarted -or -not $report.verifiedWorkflow -or $report.heroes -ne 1 -or $report.skins -ne 1){throw 'Unexpected UI smoke result'}
$report | ConvertTo-Json
