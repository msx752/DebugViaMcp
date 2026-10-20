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

function Add-Box($g, [float]$x, [float]$y, [float]$w, [float]$h, $fill, $line, [string]$title, [string]$sub, $titleColor, $subColor = $null, [float]$titleSize = 19) {
    if ($null -eq $subColor) { $subColor = $grey }
    $path = Get-RoundedPath $x $y $w $h 14
    $g.FillPath((New-Object Drawing.SolidBrush $fill), $path)
    $g.DrawPath((New-Object Drawing.Pen $line, 2.5), $path)
    $fmt = New-Object Drawing.StringFormat
    $fmt.Alignment = 'Center'
    $fmt.LineAlignment = 'Center'
    $titleFont = New-Object Drawing.Font 'Segoe UI Semibold', $titleSize
    $subFont = New-Object Drawing.Font 'Segoe UI', 13
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

# ---------- 1. How it works ----------
$c = New-Canvas
$g = $c.G
$white = [Drawing.Color]::White
Add-Text $g 'Let your AI agent debug. You hold the brake.' 0 44 1280 32 $dark 'Segoe UI Semibold' 'Center'
Add-Text $g 'Claude Code, Codex, Cursor or any MCP client drives the Visual Studio debugger. You approve every change.' 0 100 1280 16 $grey 'Segoe UI' 'Center'

Add-Box $g 60 180 320 150 $light $purple 'AI agent' 'Claude Code, Codex, Cursor, any MCP client' $dark $null 22
Add-Box $g 480 180 320 150 $dark $dark 'Debugging via MCP' 'the extension in Visual Studio asks you before anything changes' $white $white 22
Add-Box $g 900 180 320 150 $white $red 'Visual Studio debugger' 'your solution, your program' $red $grey 22
Add-Arrow $g 384 255 474 255 $purple
Add-Arrow $g 804 255 894 255 $red

Add-Text $g 'You decide what the agent may do - in Visual Studio, where the agent cannot change it' 0 372 1280 17 $dark 'Segoe UI Semibold' 'Center'
$tiers = @(
    @('Read', 'status, stack, variables, output', 'Allow', $green),
    @('Control', 'breakpoints, start, stop, step, build', 'Allow', $green),
    @('Mutate', 'set_value, side effects, attach', 'Asks you first', $amber))
$x = 60
foreach ($t in $tiers) {
    Add-Box $g $x 420 360 170 $light $t[3] $t[0] $t[1] $t[3]
    Add-Text $g $t[2] $x 500 360 15 $t[3] 'Segoe UI Semibold' 'Center'
    $x += 400
}

Add-Text $g 'No answer means no.     One click (Disconnect AI) cuts every agent off.     Everything stays on your machine.' 0 636 1280 15 $grey 'Segoe UI' 'Center'
$c.Bitmap.Save((Join-Path $outDir 'how-it-works.png'), [Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $c.Bitmap.Dispose()

# ---------- 2. Several Visual Studios ----------
$c = New-Canvas
$g = $c.G
Add-Text $g 'Several Visual Studios, one agent each' 0 36 1280 30 $dark 'Segoe UI Semibold' 'Center'
Add-Text $g 'The right Visual Studio is chosen by folder. Only one agent controls a Visual Studio at a time.' 0 88 1280 16 $grey 'Segoe UI' 'Center'

Add-Box $g 60 190 260 110 $light $purple 'Agent A' 'terminal in the backend folder' $dark
Add-Box $g 60 400 260 110 $light $purple 'Agent B' 'terminal in the scheduler folder' $dark
Add-Box $g 520 190 300 110 $dark $dark 'VS - backend API' 'held by Agent A' ([Drawing.Color]::White) ([Drawing.Color]::White)
Add-Box $g 520 400 300 110 $dark $dark 'VS - scheduler job' 'held by Agent B' ([Drawing.Color]::White) ([Drawing.Color]::White)
Add-Arrow $g 322 245 516 245 $green
Add-Arrow $g 322 455 516 455 $green
Add-Arrow $g 200 402 530 300 $red
Add-Text $g 'in_use: another agent holds this Visual Studio' 330 318 260 13 $red 'Segoe UI Semibold'

Add-Box $g 930 190 300 130 ([Drawing.Color]::White) $purple 'You approve' 'an agent from an unrelated folder asks first - once per connection' $dark
Add-Box $g 930 400 300 130 ([Drawing.Color]::White) $purple 'Stale build?' 'the agent is told, and Visual Studio offers an optional Build' $dark
Add-Text $g 'vs_release, closing the terminal or Disconnect AI frees a Visual Studio at once.' 0 640 1280 15 $grey 'Segoe UI' 'Center'
$c.Bitmap.Save((Join-Path $outDir 'several-visual-studios.png'), [Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $c.Bitmap.Dispose()
"wrote images to $outDir"
