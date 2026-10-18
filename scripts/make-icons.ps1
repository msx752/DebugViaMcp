# Renders src/DebugViaMcp.Vsix/Resources/icon.svg (the source of truth) to the PNGs the VSIX manifest and the NuGet
# package need: icon.png (128 px) and preview.png (200 px). Uses headless Microsoft Edge or Chrome.
# Run again after editing icon.svg.
$ErrorActionPreference = 'Stop'
$resources = Resolve-Path (Join-Path $PSScriptRoot '..\src\DebugViaMcp.Vsix\Resources')
$svg = Join-Path $resources 'icon.svg'

$browser = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Google\Chrome\Application\chrome.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $browser) { throw 'Microsoft Edge or Google Chrome is required to render the icon.' }

$temp = Join-Path ([IO.Path]::GetTempPath()) ('debugviamcp-icons-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $temp | Out-Null
try {
    $markup = Get-Content $svg -Raw
    foreach ($size in 128, 200) {
        # An HTML wrapper with a transparent background, the SVG scaled to exactly size x size.
        $html = Join-Path $temp "icon-$size.html"
        Set-Content $html -Encoding utf8 -Value ("<!doctype html><html><head><style>html,body{margin:0;background:transparent}" +
            "svg{display:block;width:${size}px;height:${size}px}</style></head><body>$markup</body></html>")
        $png = Join-Path $temp "icon-$size.png"
        & $browser --headless=new --disable-gpu --hide-scrollbars --default-background-color=00000000 `
            "--window-size=$size,$size" "--screenshot=$png" "--user-data-dir=$temp\profile" ([Uri]$html).AbsoluteUri | Out-Null
        if (-not (Test-Path $png)) { throw "Rendering $size px failed." }
        $target = if ($size -eq 128) { 'icon.png' } else { 'preview.png' }
        Copy-Item $png (Join-Path $resources $target) -Force
    }
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}
"Rendered icon.png and preview.png from icon.svg in $resources"
