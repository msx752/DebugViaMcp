#Requires -Version 5.1
<#
.SYNOPSIS
  Removes the installed Debugging via MCP extension and installs the VSIX built from this repository.

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

.PARAMETER InstallerTimeoutSeconds
  How long VSIXInstaller may take for the removal or the installation (default 180). A stuck installer is stopped after that
  (only the one this script started) instead of hanging forever.

.EXAMPLE
  powershell -File scripts\reinstall-vsix.ps1 -Build
#>
param(
    [switch]$Build,
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [string]$VsixPath,
    [switch]$UninstallOnly,
    [int]$InstallerTimeoutSeconds = 180
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\DebugViaMcp.Vsix\DebugViaMcp.Vsix.csproj'
$manifestPath = Join-Path $root 'src\DebugViaMcp.Vsix\source.extension.vsixmanifest'
if (-not $VsixPath) { $VsixPath = Join-Path $root "src\DebugViaMcp.Vsix\bin\$Configuration\net48\DebugViaMcp.Vsix.vsix" }

if (Get-Process devenv -ErrorAction SilentlyContinue) {
    throw 'Visual Studio is running. Close it first (this script never closes it), then run it again.'
}

# Visual Studio installs per-user extensions into the real user profile. If this shell runs with a redirected profile
# (USERPROFILE / LOCALAPPDATA pointing somewhere else, as some tools do), VSIXInstaller would install into that other
# place and Visual Studio would never see the extension. Point the installer at the real profile.
$realHome = [Environment]::GetFolderPath('UserProfile')
if ($env:USERPROFILE -ne $realHome) {
    Write-Host "Profile environment is redirected ($env:USERPROFILE); installing into the real profile $realHome." -ForegroundColor Yellow
    $env:USERPROFILE = $realHome
    $env:LOCALAPPDATA = Join-Path $realHome 'AppData\Local'
    $env:APPDATA = Join-Path $realHome 'AppData\Roaming'
}

# Runs VSIXInstaller and waits for it, but not forever. Returns the exit code, or $null when it had to be stopped.
function Invoke-VsixInstaller([string]$What, [string[]]$Arguments) {
    $running = Get-Process VSIXInstaller -ErrorAction SilentlyContinue
    if ($running) { throw "Another VSIXInstaller is running (process id $($running.Id -join ', ')). Wait for it to finish, then run this script again." }
    $run = Start-Process -FilePath $installer -ArgumentList $Arguments -PassThru
    if ($run.WaitForExit($InstallerTimeoutSeconds * 1000)) { return $run.ExitCode }
    Write-Host "VSIXInstaller did not finish $What within $InstallerTimeoutSeconds s; stopping it (only the one this script started)." -ForegroundColor Yellow
    Stop-Process -Id $run.Id -Force -ErrorAction SilentlyContinue
    return $null
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
$uninstallCode = Invoke-VsixInstaller 'the removal' @('/quiet', "/uninstall:$extensionId", "/instanceIds:$instanceId")
if ($null -eq $uninstallCode) {
    throw 'The removal got stuck. Close every Visual Studio window and any other VSIXInstaller, then run this script again. The extension''s MCP server runs from %LOCALAPPDATA%\DebugViaMcp\bin, not from the extension folder, so a running agent does not block the removal.'
}
if ($uninstallCode -ne 0) {
    Write-Host "  VSIXInstaller exit code $uninstallCode (the extension was probably not installed; continuing)." -ForegroundColor Yellow
}

if ($UninstallOnly) {
    Write-Host 'Removed. Start Visual Studio again.' -ForegroundColor Green
    return
}

if (-not (Test-Path $VsixPath)) { throw "VSIX not found: $VsixPath (use -Build to create it)." }
Write-Host "Installing $VsixPath ..." -ForegroundColor Cyan
$installCode = Invoke-VsixInstaller 'the installation' @('/quiet', "/instanceIds:$instanceId", "`"$VsixPath`"")
if ($null -eq $installCode) { throw 'The installation got stuck. Close every Visual Studio window and any other VSIXInstaller, then run this script again.' }
if ($installCode -ne 0) { throw "VSIXInstaller failed with exit code $installCode." }

Write-Host ''
Write-Host 'Installed. Start Visual Studio, open a solution and look for this line in View > Output > Debugging via MCP:' -ForegroundColor Green
Write-Host "  Debugging via MCP $($identity.Version) ready (..., protocol 2.3)"
