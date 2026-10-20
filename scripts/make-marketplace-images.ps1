# Renders the two explanatory diagrams of the Marketplace page (docs/marketplace/images/*.png, 1280x720).
# They are schematic illustrations, not screenshots. Usage: powershell -File scripts\make-marketplace-images.ps1
Add-Type -AssemblyName System.Drawing

$outDir = Join-Path $PSScriptRoot '..\docs\marketplace\images'
New-Item -ItemType Directory -Force $outDir | Out-Null
$outDir = (Resolve-Path $outDir).Path

$purple = [Drawing.ColorTranslator]::FromHtml('#68217A')
$dark = [Drawing.ColorTranslator]::FromHtml('#3C145A')
$red = [Drawing.ColorTranslator]::FromHtml('#E51400')
$light = [Drawing.ColorTranslator]::FromHtml('#F5F2F8')
$grey = [Drawing.ColorTranslator]::FromHtml('#5A5A66')
$green = [Drawing.ColorTranslator]::FromHtml('#2E7D32')
$amber = [Drawing.ColorTranslator]::FromHtml('#B26A00')

function New-Canvas {
    $bmp = New-Object Drawing.Bitmap 1280, 720
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([Drawing.Color]::White)
    return @{ Bitmap = $bmp; G = $g }
}

function Get-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Add-Box($g, [float]$x, [float]$y, [float]$w, [float]$h, $fill, $line, [string]$title, [string]$sub, $titleColor, $subColor = $null, [float]$titleSize = 19, [float]$subSize = 13) {
    if ($null -eq $subColor) { $subColor = $grey }
    $path = Get-RoundedPath $x $y $w $h 14
    $g.FillPath((New-Object Drawing.SolidBrush $fill), $path)
    $g.DrawPath((New-Object Drawing.Pen $line, 2.5), $path)
    $fmt = New-Object Drawing.StringFormat
    $fmt.Alignment = 'Center'
    $fmt.LineAlignment = 'Center'
    $titleFont = New-Object Drawing.Font 'Segoe UI Semibold', $titleSize
    $subFont = New-Object Drawing.Font 'Segoe UI', $subSize
    $titleRect = New-Object Drawing.RectangleF $x, ($y + 10), $w, ($h / 2)
    $subRect = New-Object Drawing.RectangleF ($x + 8), ($y + $h / 2), ($w - 16), ($h / 2 - 8)
    $g.DrawString($title, $titleFont, (New-Object Drawing.SolidBrush $titleColor), $titleRect, $fmt)
    $g.DrawString($sub, $subFont, (New-Object Drawing.SolidBrush $subColor), $subRect, $fmt)
}

function Add-Text($g, [string]$text, [float]$x, [float]$y, [float]$w, [float]$size, $color, [string]$fontName = 'Segoe UI', [string]$align = 'Near') {
    $fmt = New-Object Drawing.StringFormat
    $fmt.Alignment = $align
    $font = New-Object Drawing.Font $fontName, $size
    $g.DrawString($text, $font, (New-Object Drawing.SolidBrush $color), (New-Object Drawing.RectangleF $x, $y, $w, 80), $fmt)
}

function Add-Arrow($g, [float]$x1, [float]$y1, [float]$x2, [float]$y2, $color) {
    $pen = New-Object Drawing.Pen $color, 3.5
    $pen.CustomEndCap = New-Object Drawing.Drawing2D.AdjustableArrowCap 5, 6
    $g.DrawLine($pen, $x1, $y1, $x2, $y2)
}

# The Marketplace shows these at roughly 60% of their width, so the text is large and there is little of it.

# ---------- 1. How it works ----------
$c = New-Canvas
$g = $c.G
$white = [Drawing.Color]::White
Add-Text $g 'Let your AI agent debug. You hold the brake.' 0 40 1280 38 $dark 'Segoe UI Semibold' 'Center'
Add-Text $g 'Claude Code, Codex, Cursor or any MCP client' 0 104 1280 20 $grey 'Segoe UI' 'Center'

Add-Box $g 50 170 340 170 $light $purple 'AI agent' "asks for a breakpoint,`na step, a value" $dark $null 26 18
Add-Box $g 470 170 340 170 $dark $dark 'Debugging via MCP' "asks you before`nanything changes" $white $white 24 18
Add-Box $g 890 170 340 170 $white $red 'Visual Studio' "your debugger,`nyour program" $red $grey 26 18
Add-Arrow $g 394 255 466 255 $purple
Add-Arrow $g 814 255 886 255 $red

Add-Text $g 'You choose what the agent may do' 0 372 1280 24 $dark 'Segoe UI Semibold' 'Center'
$tiers = @(
    @('Read', 'status, stack, variables', 'Allow', $green),
    @('Control', 'breakpoints, step, build', 'Allow', $green),
    @('Mutate', 'set_value, attach', 'Asks you first', $amber))
$x = 50
foreach ($t in $tiers) {
    Add-Box $g $x 420 360 190 $light $t[3] $t[0] $t[1] $t[3] $null 28 18
    Add-Text $g $t[2] $x 508 360 20 $t[3] 'Segoe UI Semibold' 'Center'
    $x += 410
}

Add-Text $g 'No answer means no.   One click cuts every agent off.' 0 642 1280 20 $grey 'Segoe UI' 'Center'
$c.Bitmap.Save((Join-Path $outDir 'how-it-works.png'), [Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $c.Bitmap.Dispose()

# ---------- 2. Several Visual Studios ----------
$c = New-Canvas
$g = $c.G
Add-Text $g 'Several Visual Studios, one agent each' 0 40 1280 38 $dark 'Segoe UI Semibold' 'Center'
Add-Text $g 'The right one is picked by folder. One agent at a time.' 0 104 1280 20 $grey 'Segoe UI' 'Center'

Add-Box $g 50 190 290 140 $light $purple 'Agent A' 'in the backend folder' $dark $null 26 18
Add-Box $g 50 420 290 140 $light $purple 'Agent B' 'in the scheduler folder' $dark $null 26 18
Add-Box $g 470 190 330 140 $dark $dark 'VS: backend API' 'held by Agent A' $white $white 26 18
Add-Box $g 470 420 330 140 $dark $dark 'VS: scheduler job' 'held by Agent B' $white $white 26 18
Add-Arrow $g 344 260 466 260 $green
Add-Arrow $g 344 490 466 490 $green
Add-Arrow $g 200 418 560 334 $red
Add-Text $g 'in_use' 380 398 120 22 $red 'Segoe UI Semibold'

Add-Box $g 900 190 340 150 $white $purple 'You approve' "an agent from another`nfolder asks first" $dark $null 26 18
Add-Box $g 900 420 340 150 $white $purple 'Stale build?' "the agent is told;`nyou can Build" $dark $null 26 18
Add-Text $g 'vs_release or Disconnect AI frees a Visual Studio at once.' 0 642 1280 20 $grey 'Segoe UI' 'Center'
$c.Bitmap.Save((Join-Path $outDir 'several-visual-studios.png'), [Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $c.Bitmap.Dispose()
"wrote images to $outDir"
