#Requires -Version 5.1
# Fails when the version is not the same everywhere it is written (see docs/releasing.md).
# Prints the version on success; with -GitHubOutput it also writes version=<x> to $env:GITHUB_OUTPUT.
param([switch]$GitHubOutput)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$props = [xml](Get-Content (Join-Path $root 'Directory.Build.props'))
$prefix = ($props.Project.PropertyGroup | ForEach-Object { $_.VersionPrefix } | Where-Object { $_ } | Select-Object -First 1).Trim()
$manifest = [xml](Get-Content (Join-Path $root 'src/DebugViaMcp.Vsix/source.extension.vsixmanifest'))
$vsix = $manifest.PackageManifest.Metadata.Identity.Version
$errors = @()
if ($prefix -ne $vsix) { $errors += "Directory.Build.props VersionPrefix ($prefix) differs from the manifest Identity/@Version ($vsix)." }
if ($vsix -notmatch '^\d+\.\d+\.\d+$') { $errors += "Manifest version '$vsix' is not MAJOR.MINOR.PATCH." }
$changelog = Get-Content (Join-Path $root 'CHANGELOG.md') -Raw
if ($changelog -notmatch "(?m)^## $([regex]::Escape($vsix))\b") { $errors += "CHANGELOG.md has no '## $vsix' section." }
if ($errors) { $errors | ForEach-Object { Write-Error $_ -ErrorAction Continue }; exit 1 }
Write-Host "Version $vsix is consistent."
if ($GitHubOutput -and $env:GITHUB_OUTPUT) { "version=$vsix" | Out-File $env:GITHUB_OUTPUT -Append -Encoding utf8 }
