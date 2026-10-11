# Generates the extension and package icons (src/VsMcpDebugger.Vsix/Resources). Run again after changing the design.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot '..\src\VsMcpDebugger.Vsix\Resources'
New-Item -ItemType Directory -Force $out | Out-Null

function New-Icon([int]$size, [string]$path) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Rounded square, Visual Studio purple.
    $r = [int]($size * 0.18)
    $rect = New-Object System.Drawing.Rectangle 0, 0, ($size - 1), ($size - 1)
    $gp = New-Object System.Drawing.Drawing2D.GraphicsPath
    $gp.AddArc($rect.X, $rect.Y, 2 * $r, 2 * $r, 180, 90)
    $gp.AddArc($rect.Right - 2 * $r, $rect.Y, 2 * $r, 2 * $r, 270, 90)
    $gp.AddArc($rect.Right - 2 * $r, $rect.Bottom - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $gp.AddArc($rect.X, $rect.Bottom - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $gp.CloseFigure()
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(104, 33, 122)), ([System.Drawing.Color]::FromArgb(60, 20, 90)), 45
    $g.FillPath($bg, $gp)

    # Code braces with a breakpoint dot between them.
    $font = New-Object System.Drawing.Font 'Consolas', ([single]($size * 0.46)), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $white = [System.Drawing.Brushes]::White
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
    $w = [single]($size * 0.4)
    $g.DrawString('{', $font, $white, (New-Object System.Drawing.RectangleF ([single]($size * 0.27 - $w / 2)), ([single](-$size * 0.04)), $w, $size), $fmt)
    $g.DrawString('}', $font, $white, (New-Object System.Drawing.RectangleF ([single]($size * 0.73 - $w / 2)), ([single](-$size * 0.04)), $w, $size), $fmt)
    $d = [int]($size * 0.2)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(229, 20, 0))), [int](($size - $d) / 2), [int](($size - $d) / 2), $d, $d)

    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

New-Icon 128 (Join-Path $out 'icon.png')
New-Icon 200 (Join-Path $out 'preview.png')
"Icons written to $out"
