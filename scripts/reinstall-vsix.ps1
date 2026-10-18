#Requires -Version 5.1
<#
.SYNOPSIS
  Removes the installed VsMcpDebugger extension and installs the VSIX built from this repository.

.DESCRIPTION
  For development: Visual Studio treats a VSIX with the same version number as "already installed", so the old build has to
  be removed first. Paths are relative to this repository, so the script works from any clone.
  It never starts or closes Visual Studio: close it yourself first (the script refuses to run while devenv.exe is running).

.PARAMETER Build
  Build the VSIX first (MSBuild of Visual Studio, Release by default).

.PARAMETER Configuration
  Release (default) or Debug. Selects which build output is installed.

.PARAMETER VsixPath
  Install this VSIX instead of the one built in the repository.

.PARAMETER UninstallOnly
  Only remove the extension.

.EXAMPLE
  powershell -File scripts\reinstall-vsix.ps1 -Build
#>
param(
    [switch]$Build,
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [string]$VsixPath,
    [switch]$UninstallOnly
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\VsMcpDebugger.Vsix\VsMcpDebugger.Vsix.csproj'
$manifestPath = Join-Path $root 'src\VsMcpDebugger.Vsix\source.extension.vsixmanifest'
if (-not $VsixPath) { $VsixPath = Join-Path $root "src\VsMcpDebugger.Vsix\bin\$Configuration\net48\VsMcpDebugger.Vsix.vsix" }

if (Get-Process devenv -ErrorAction SilentlyContinue) {
    throw 'Visual Studio is running. Close it first (this script never closes it), then run it again.'
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'vswhere.exe not found: is Visual Studio installed?' }
$install = & $vswhere -latest -prerelease -products * -property installationPath | Select-Object -First 1
$instanceId = & $vswhere -latest -prerelease -products * -property instanceId | Select-Object -First 1
if (-not $install) { throw 'No Visual Studio installation found.' }
$installer = Join-Path $install 'Common7\IDE\VSIXInstaller.exe'
if (-not (Test-Path $installer)) { throw "VSIXInstaller.exe not found in $install" }

$identity = ([xml](Get-Content $manifestPath -Raw)).PackageManifest.Metadata.Identity
$extensionId = $identity.Id
Write-Host "Extension $extensionId ($($identity.Version)) in $install" -ForegroundColor Cyan

if ($Build -and -not $UninstallOnly) {
    $msbuild = & $vswhere -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $msbuild) { throw 'MSBuild of Visual Studio not found.' }
    Write-Host "Building $Configuration ..." -ForegroundColor Cyan
    & $msbuild $project /restore "/p:Configuration=$Configuration" /p:DeployExtension=false /v:minimal -nodeReuse:false
    if ($LASTEXITCODE) { throw 'VSIX build failed.' }
}

Write-Host 'Removing the installed extension ...' -ForegroundColor Cyan
$uninstall = Start-Process -FilePath $installer -ArgumentList @('/quiet', "/uninstall:$extensionId", "/instanceIds:$instanceId") -Wait -PassThru
if ($uninstall.ExitCode -ne 0) {
    Write-Host "  VSIXInstaller exit code $($uninstall.ExitCode) (the extension was probably not installed; continuing)." -ForegroundColor Yellow
}

if ($UninstallOnly) {
    Write-Host 'Removed. Start Visual Studio again.' -ForegroundColor Green
    return
}

if (-not (Test-Path $VsixPath)) { throw "VSIX not found: $VsixPath (use -Build to create it)." }
Write-Host "Installing $VsixPath ..." -ForegroundColor Cyan
$installRun = Start-Process -FilePath $installer -ArgumentList @('/quiet', "/instanceIds:$instanceId", "`"$VsixPath`"") -Wait -PassThru
if ($installRun.ExitCode -ne 0) { throw "VSIXInstaller failed with exit code $($installRun.ExitCode)." }

Write-Host ''
Write-Host 'Installed. Start Visual Studio, open a solution and look for this line in View > Output > VsMcpDebugger:' -ForegroundColor Green
Write-Host "  VsMcpDebugger $($identity.Version) ready (..., protocol 2.2)"
