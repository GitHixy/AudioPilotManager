<#
.SYNOPSIS
    Builds release packages of Audio Pilot Manager.

.DESCRIPTION
    Produces, in .\artifacts:
      - AudioPilotManager-<version>-win-x64-portable.zip    (single self-contained .exe, no install needed)
      - AudioPilotManager-<version>-win-arm64-portable.zip
      - AudioPilotManager-<version>-Setup.exe               (installer, if Inno Setup 6 is available)
      - SHA256SUMS.txt                                      (checksums for everything above)

    Needs the .NET 10 SDK. The installer additionally needs Inno Setup 6 (ISCC.exe on PATH or in
    its default install folder); without it the portable builds are still produced.

.EXAMPLE
    ./build.ps1
    ./build.ps1 -Version 1.2.0
#>
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src/AudioPilotManager/AudioPilotManager.csproj'
$artifacts = Join-Path $root 'artifacts'

if (-not $Version) {
    $Version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
Write-Host "Building Audio Pilot Manager $Version" -ForegroundColor Cyan

if (Test-Path $artifacts) { Remove-Item $artifacts -Recurse -Force }
New-Item -ItemType Directory -Path $artifacts | Out-Null

foreach ($rid in 'win-x64', 'win-arm64') {
    $out = Join-Path $artifacts "publish/$rid"
    dotnet publish $project -c Release -r $rid --self-contained true `
        -p:Version=$Version `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -o $out
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid" }

    $zip = Join-Path $artifacts "AudioPilotManager-$Version-$rid-portable.zip"
    Compress-Archive -Path (Join-Path $out 'AudioPilotManager.exe'), (Join-Path $root 'LICENSE') -DestinationPath $zip
    Write-Host "  -> $zip" -ForegroundColor Green
}

if (-not $SkipInstaller) {
    $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
    if (-not $iscc) {
        $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") |
            Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    if ($iscc) {
        & $iscc "/DAppVersion=$Version" "/DSourceDir=$(Join-Path $artifacts 'publish/win-x64')" "/O$artifacts" (Join-Path $root 'installer/AudioPilotManager.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed' }
    }
    else {
        Write-Warning 'Inno Setup 6 not found: skipping the installer (portable builds are ready).'
    }
}

# Checksums let anyone verify a download is exactly what was built here.
$sums = Get-ChildItem $artifacts -File | Where-Object { $_.Extension -in '.zip', '.exe' } | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
$sums | Set-Content (Join-Path $artifacts 'SHA256SUMS.txt') -Encoding ascii
Write-Host "Done. Packages are in $artifacts" -ForegroundColor Cyan
