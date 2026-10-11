#Requires -Version 5.1
# Builds the release artifacts locally: VSIX + dotnet tool package + SHA256 checksums in ./artifacts.
# Publishes nothing. The publish commands are printed at the end for when the maintainer decides to release.
param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root 'artifacts'
Push-Location $root
try {
    if (Test-Path $artifacts) { Remove-Item $artifacts -Recurse -Force }
    New-Item -ItemType Directory $artifacts | Out-Null

    dotnet build VsMcpDebugger.Core.slnf -c Release
    if ($LASTEXITCODE) { throw 'dotnet build failed' }
    if (-not $SkipTests) {
        dotnet test --solution VsMcpDebugger.Core.slnf -c Release --no-build
        if ($LASTEXITCODE) { throw 'tests failed' }
    }

    dotnet pack src/VsMcpDebugger.Mcp/VsMcpDebugger.Mcp.csproj -c Release --no-build -o $artifacts
    if ($LASTEXITCODE) { throw 'dotnet pack failed' }

    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    $msbuild = & $vswhere -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $msbuild) { throw 'No Visual Studio with MSBuild found (VS 2022 17.14+ or VS 2026).' }
    & $msbuild src/VsMcpDebugger.Vsix/VsMcpDebugger.Vsix.csproj /restore /p:Configuration=Release /p:DeployExtension=false /v:minimal -nodeReuse:false
    if ($LASTEXITCODE) { throw 'VSIX build failed' }
    Copy-Item src/VsMcpDebugger.Vsix/bin/Release/net48/VsMcpDebugger.Vsix.vsix (Join-Path $artifacts 'VsMcpDebugger.vsix')

    $sums = Get-ChildItem $artifacts -File | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
    Set-Content (Join-Path $artifacts 'SHA256SUMS.txt') $sums -Encoding ascii

    $version = ([xml](Get-Content src/VsMcpDebugger.Vsix/source.extension.vsixmanifest)).PackageManifest.Metadata.Identity.Version
    Write-Host ''
    Write-Host "Artifacts for $version in ${artifacts}:" -ForegroundColor Green
    Get-ChildItem $artifacts | ForEach-Object { Write-Host "  $($_.Name)" }
    Write-Host ''
    Write-Host 'Nothing was published. When you decide to release:' -ForegroundColor Yellow
    Write-Host '  1. Put your Marketplace publisher id into marketplace/publishManifest.json ("publisher").'
    Write-Host '  2. VsixPublisher.exe publish -payload artifacts\VsMcpDebugger.vsix -publishManifest marketplace\publishManifest.json -personalAccessToken <PAT>'
    Write-Host "  3. dotnet nuget push artifacts\VsMcpDebugger.$version.nupkg --source https://api.nuget.org/v3/index.json --api-key <KEY>"
    Write-Host '  (VsixPublisher.exe ships with Visual Studio: <VS>\VSSDK\VisualStudioIntegration\Tools\Bin\VsixPublisher.exe)'
}
finally {
    Pop-Location
}
