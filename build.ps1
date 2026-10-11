#Requires -Version 5.1
param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug', [switch]$SkipVsix)
$ErrorActionPreference = 'Stop'
dotnet build VsMcpDebugger.Core.slnf -c $Configuration
if ($LASTEXITCODE) { exit $LASTEXITCODE }
dotnet test --solution VsMcpDebugger.Core.slnf -c $Configuration --no-build
if ($LASTEXITCODE) { exit $LASTEXITCODE }
if ($SkipVsix -or -not (Test-Path src/VsMcpDebugger.Vsix)) { return }
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -prerelease -products * -requires Microsoft.VisualStudio.Workload.VisualStudioExtension -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'No Visual Studio with the "Visual Studio extension development" workload found.' }
& $msbuild src/VsMcpDebugger.Vsix/VsMcpDebugger.Vsix.csproj /restore /p:Configuration=$Configuration /v:minimal
exit $LASTEXITCODE
