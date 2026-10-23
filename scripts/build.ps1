#Requires -Version 5.1
param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug', [switch]$SkipVsix)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)   # the solution files are in the repository root
# No node reuse and no shared compiler: they would stay running after the build, each holding ~100 MB until they time out.
dotnet build DebugViaMcp.Core.slnf -c $Configuration -nodeReuse:false -p:UseSharedCompilation=false
if ($LASTEXITCODE) { exit $LASTEXITCODE }
dotnet test --solution DebugViaMcp.Core.slnf -c $Configuration --no-build
if ($LASTEXITCODE) { exit $LASTEXITCODE }
if ($SkipVsix -or -not (Test-Path src/DebugViaMcp.Vsix)) { return }
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'No Visual Studio with MSBuild found (VS 2022 17.14+ or VS 2026).' }
# DeployExtension=false: a build must not install the extension into the Visual Studio of whoever runs it (and fails when it is not installed there).
& $msbuild src/DebugViaMcp.Vsix/DebugViaMcp.Vsix.csproj /restore /p:Configuration=$Configuration /p:DeployExtension=false /v:minimal -nodeReuse:false /p:UseSharedCompilation=false
exit $LASTEXITCODE
