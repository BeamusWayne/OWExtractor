#requires -Version 7.0
param(
    [Parameter(Mandatory=$true)][string]$PublishDirectory,
    [Parameter(Mandatory=$true)][string]$ZipPath
)
$ErrorActionPreference='Stop'
$repoRoot=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$publishRoot=(Resolve-Path -LiteralPath $PublishDirectory).Path.TrimEnd('\','/')
$zipFull=$ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ZipPath)
if($zipFull.StartsWith($publishRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'ZIP must be outside the publish directory.'}
if(Test-Path -LiteralPath $zipFull){throw 'ZIP already exists; choose a new output path.'}
if(-not (Test-Path -LiteralPath (Join-Path $publishRoot 'OWExtractor.exe'))){throw 'Published executable is missing.'}
$runtime=Get-Content -LiteralPath (Join-Path $publishRoot 'OWExtractor.runtimeconfig.json') -Raw | ConvertFrom-Json
$assets=Get-Content -LiteralPath (Join-Path $repoRoot 'src/OWExtractor.App/obj/project.assets.json') -Raw | ConvertFrom-Json
$packageRoots=@($assets.packageFolders.PSObject.Properties.Name)
$licenses=Join-Path $publishRoot 'licenses'
[IO.Directory]::CreateDirectory($licenses) | Out-Null
$records=@()
foreach($framework in $runtime.runtimeOptions.includedFrameworks){
    $version=[string]$framework.version
    if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'Only stable runtime versions are supported by this release script.'}
    switch($framework.name){
        'Microsoft.NETCore.App' { $package='microsoft.netcore.app.runtime.win-x64'; $licenseName='LICENSE.TXT'; $upstream='runtime' }
        'Microsoft.WindowsDesktop.App' { $package='microsoft.windowsdesktop.app.runtime.win-x64'; $licenseName='LICENSE'; $upstream='wpf' }
        default {throw ('Unhandled runtime framework: '+$framework.name)}
    }
    $packageDir=$null
    foreach($root in $packageRoots){$candidate=Join-Path $root ($package+'/'+$version);if(Test-Path -LiteralPath (Join-Path $candidate $licenseName)){$packageDir=$candidate;break}}
    if(-not $packageDir){throw ('Matching runtime package license not found: '+$package+'/'+$version)}
    $targetDir=Join-Path $licenses ($framework.name+'/'+$version)
    [IO.Directory]::CreateDirectory($targetDir) | Out-Null
    $licenseTarget=Join-Path $targetDir 'LICENSE.txt'
    Copy-Item -LiteralPath (Join-Path $packageDir $licenseName) -Destination $licenseTarget
    $noticeTarget=Join-Path $targetDir 'THIRD-PARTY-NOTICES.txt'
    $packageNotice=Join-Path $packageDir 'THIRD-PARTY-NOTICES.TXT'
    $packageUrl='https://www.nuget.org/packages/'+$package+'/'+$version
    $noticeSource=$packageUrl
    if(Test-Path -LiteralPath $packageNotice){Copy-Item -LiteralPath $packageNotice -Destination $noticeTarget}
    else {
        # Desktop runtime packages supply LICENSE but omit WPF's separate notices.
        # Fetch only the matching upstream release tag; never substitute main/latest.
        $noticeSource='https://raw.githubusercontent.com/dotnet/'+$upstream+'/v'+$version+'/THIRD-PARTY-NOTICES.TXT'
        Invoke-WebRequest -Uri $noticeSource -OutFile $noticeTarget -TimeoutSec 60
    }
    foreach($file in @($licenseTarget,$noticeTarget)){if((Get-Item -LiteralPath $file).Length -lt 100){throw 'Runtime notice is missing or unexpectedly empty.'}}
    $additionalNotices=@()
    if($framework.name -eq 'Microsoft.WindowsDesktop.App'){
        # The desktop runtime also ships Windows Forms, with separate notices.
        $formsSource='https://raw.githubusercontent.com/dotnet/winforms/v'+$version+'/THIRD-PARTY-NOTICES.TXT'
        $formsTarget=Join-Path $targetDir 'WINDOWS-FORMS-THIRD-PARTY-NOTICES.txt'
        Invoke-WebRequest -Uri $formsSource -OutFile $formsTarget -TimeoutSec 60
        if((Get-Item -LiteralPath $formsTarget).Length -lt 100){throw 'Windows Forms notices are missing or unexpectedly empty.'}
        $additionalNotices+=@{component='Windows Forms';file=[IO.Path]::GetFileName($formsTarget);source=$formsSource;sha256=(Get-FileHash -LiteralPath $formsTarget -Algorithm SHA256).Hash.ToLowerInvariant()}
    }
    $records+=@{framework=$framework.name;version=$version;runtimeIdentifier='win-x64';licenseSource=$packageUrl;noticeSource=$noticeSource;licenseSha256=(Get-FileHash -LiteralPath $licenseTarget -Algorithm SHA256).Hash.ToLowerInvariant();noticeSha256=(Get-FileHash -LiteralPath $noticeTarget -Algorithm SHA256).Hash.ToLowerInvariant();additionalNotices=$additionalNotices}
}
if($records.Count -ne 2){throw 'Expected both .NET and Windows Desktop runtimes in this self-contained package.'}
$records | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $licenses 'runtime-manifest.json') -Encoding utf8
foreach($name in @('LICENSE','THIRD_PARTY_NOTICES.md','README.md')){Copy-Item -LiteralPath (Join-Path $repoRoot $name) -Destination $publishRoot}
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs') -Destination $publishRoot -Recurse -Force
$files=@(Get-ChildItem -LiteralPath $publishRoot -File -Recurse)
foreach($file in $files){
    $relative=[IO.Path]::GetRelativePath($publishRoot,$file.FullName).Replace('\','/')
    if($relative -match '(?i)(\.local\.json|\.jobs\.json|\.(owmdl|owmat|owentity|owanim\w*|blend|wem|ogg|wav)$|(^|/)DataTool\.(exe|dll)$)'){throw ('Private or game asset in package: '+$relative)}
    if($relative -match '(?i)\.(png|jpe?g|webp|dds|tga|tif)$' -and $relative -ne 'docs/images/offline-preview.png'){throw ('Unexpected image in public package: '+$relative)}
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($zipFull)) | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::Open($zipFull,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach($file in $files){
        if($file.Extension -eq '.pdb'){continue}
        $relative=[IO.Path]::GetRelativePath($publishRoot,$file.FullName).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,$relative,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally {$archive.Dispose()}
$digest=(Get-FileHash -LiteralPath $zipFull -Algorithm SHA256).Hash.ToLowerInvariant()
($digest+'  '+[IO.Path]::GetFileName($zipFull)) | Set-Content -LiteralPath ($zipFull+'.sha256') -Encoding ascii
Write-Output ('Packaged '+[IO.Path]::GetFileName($zipFull)+' with version-matched runtime notices; SHA-256 '+$digest)
