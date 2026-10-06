<#
.SYNOPSIS
    Draws the WinCapture logo and writes Logo.png, AppIcon.png and AppIcon.ico,
    and the two pictures of the setup wizard to packaging.

.DESCRIPTION
    The logo is a mix of two: the four tiles of the Windows logo, in the same
    arrangement as in the WinFFmpeg logo, and on them the shapes of the
    Snipping Tool logo: the dashes of a selection and the scissors that close
    its corner. The shapes are those of the original; the colours are not. Like
    the line on the WinFFmpeg logo they are white, with the blades of the
    scissors in light grey.

    The generated files are checked in; run this only to change the logo.
    Needs Windows PowerShell (System.Drawing).
#>
[CmdletBinding()]
param(
    [string]$OutputFolder
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not $OutputFolder) {
    $OutputFolder = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\src\WinCapture.App\Assets'
}

New-Item -ItemType Directory -Force $OutputFolder | Out-Null
$OutputFolder = (Resolve-Path $OutputFolder).Path

function Get-Color([string]$Hex) {
    [System.Drawing.Color]::FromArgb(
        255, [Convert]::ToInt32($Hex.Substring(0, 2), 16), [Convert]::ToInt32($Hex.Substring(2, 2), 16), [Convert]::ToInt32($Hex.Substring(4, 2), 16))
}

function New-SolidBrush([string]$Hex) {
    New-Object System.Drawing.SolidBrush (Get-Color $Hex)
}

# A gradient between two points.
function New-Gradient([double]$X1, [double]$Y1, [double]$X2, [double]$Y2, [string]$From, [string]$To) {
    $start = New-Object System.Drawing.PointF ([single]$X1), ([single]$Y1)
    $end = New-Object System.Drawing.PointF ([single]$X2), ([single]$Y2)
    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush $start, $end, (Get-Color $From), (Get-Color $To)
    $gradient.WrapMode = [System.Drawing.Drawing2D.WrapMode]::TileFlipXY
    return $gradient
}

# Turns the path data of an SVG into a path. Understands the commands the
# Snipping Tool logo uses: M, L, H, V, C and Z with absolute coordinates.
function ConvertTo-GraphicsPath([string]$Data) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath ([System.Drawing.Drawing2D.FillMode]::Winding)
    $invariant = [System.Globalization.CultureInfo]::InvariantCulture
    $tokens = @([regex]::Matches($Data, '[MLHVCZ]|-?\d*\.?\d+') | ForEach-Object { $_.Value })
    [single]$cx = 0
    [single]$cy = 0
    $command = ''
    $i = 0

    # The next number of the path data.
    function Read-Number {
        $value = [single]::Parse($tokens[$script:i], $invariant)
        $script:i++
        return $value
    }

    $script:i = 0
    while ($script:i -lt $tokens.Count) {
        if ($tokens[$script:i] -match '^[MLHVCZ]$') {
            $command = $tokens[$script:i]
            $script:i++
        }

        if ($command -eq 'M') {
            $cx = Read-Number
            $cy = Read-Number
            $path.StartFigure()
            # Further coordinate pairs after a move are lines.
            $command = 'L'
        }
        elseif ($command -eq 'L') {
            [single]$nx = Read-Number
            [single]$ny = Read-Number
            $path.AddLine($cx, $cy, $nx, $ny)
            $cx = $nx
            $cy = $ny
        }
        elseif ($command -eq 'H') {
            [single]$nx = Read-Number
            $path.AddLine($cx, $cy, $nx, $cy)
            $cx = $nx
        }
        elseif ($command -eq 'V') {
            [single]$ny = Read-Number
            $path.AddLine($cx, $cy, $cx, $ny)
            $cy = $ny
        }
        elseif ($command -eq 'C') {
            [single]$x1 = Read-Number
            [single]$y1 = Read-Number
            [single]$x2 = Read-Number
            [single]$y2 = Read-Number
            [single]$nx = Read-Number
            [single]$ny = Read-Number
            $path.AddBezier($cx, $cy, $x1, $y1, $x2, $y2, $nx, $ny)
            $cx = $nx
            $cy = $ny
        }
        elseif ($command -eq 'Z') {
            $path.CloseFigure()
            $command = ''
        }
        else {
            throw "Unexpected '$($tokens[$script:i])' in path data."
        }
    }

    return $path
}

# The shapes of the Snipping Tool logo, in its own coordinates: 256 units square,
# with the shapes between 28 and 256.
$SnippingDashes = 'M41 28H56V56H28V41C28 34.21 34.21 28 41 28ZM68 28H96V56H68ZM108 28H123C129.79 28 136 34.21 136 41V56H108ZM28 68H56V96H28ZM28 108H56V136H41C34.21 136 28 129.79 28 123Z'
$SnippingBladeUp = 'M136 56H136C152.304 56 164 67.696 164 84V192H136Z'
$SnippingBladeLeft = 'M56 136H192V164H83.5C67.487 164 56 152.513 56 136.5Z'
$SnippingBladeLeftHighlight = 'M0 136H134.75V143C134.75 145.462 132.212 148 129.75 148H0Z'
$SnippingHandleRight = 'M192 136H216V164H192ZM215 154.05H230V164H215ZM256.05 138C256.05 152.77 244.77 164.05 230 164.05C215.23 164.05 203.95 152.77 203.95 138C203.95 123.23 215.23 111.95 230 111.95C244.77 111.95 256.05 123.23 256.05 138ZM244.05 138C244.05 129.823 238.177 123.95 230 123.95C221.823 123.95 215.95 129.823 215.95 138C215.95 146.177 221.823 152.05 230 152.05C238.177 152.05 244.05 146.177 244.05 138Z'
$SnippingHandleBottom = 'M136 192H164V216H136ZM154.05 215H164V230H154.05ZM164.05 230C164.05 244.77 152.77 256.05 138 256.05C123.23 256.05 111.95 244.77 111.95 230C111.95 215.23 123.23 203.95 138 203.95C152.77 203.95 164.05 215.23 164.05 230ZM152.05 230C152.05 221.823 146.177 215.95 138 215.95C129.823 215.95 123.95 221.823 123.95 230C123.95 238.177 129.823 244.05 138 244.05C146.177 244.05 152.05 238.177 152.05 230Z'

# Draws the logo, $IconSize pixels square, centred on a transparent canvas.
function New-IconBitmap([int]$Width, [int]$Height, [double]$IconSize) {
    $bitmap = New-Object System.Drawing.Bitmap $Width, $Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    [single]$x = ($Width - $IconSize) / 2
    [single]$y = ($Height - $IconSize) / 2
    [single]$s = $IconSize

    # Four tiles in the arrangement of the Windows logo, the same as in WinFFmpeg.
    [single]$gap = $s * 0.06
    [single]$tile = ($s - $gap) / 2
    [single]$d = $tile * 0.24   # corner diameter
    $tiles = New-Object System.Drawing.Drawing2D.GraphicsPath
    foreach ($row in 0, 1) {
        foreach ($column in 0, 1) {
            [single]$tx = $x + $column * ($tile + $gap)
            [single]$ty = $y + $row * ($tile + $gap)
            $tiles.StartFigure()
            $tiles.AddArc($tx, $ty, $d, $d, 180, 90)
            $tiles.AddArc($tx + $tile - $d, $ty, $d, $d, 270, 90)
            $tiles.AddArc($tx + $tile - $d, $ty + $tile - $d, $d, $d, 0, 90)
            $tiles.AddArc($tx, $ty + $tile - $d, $d, $d, 90, 90)
            $tiles.CloseFigure()
        }
    }

    # The blue of the Windows logo and of the Snipping Tool's handles, a little
    # lighter in the top left than in the bottom right.
    $brush = New-Gradient $x $y ($x + $s) ($y + $s) '3EB4F6' '0668CC'
    $g.FillPath($brush, $tiles)

    # The Snipping Tool's shapes, drawn in its coordinates and placed so that
    # they sit in the middle of the tiles. Every size draws these same shapes;
    # there is no simplified variant for small icons.
    $state = $g.Save()
    [single]$k = $s * 0.80 / 228
    $g.TranslateTransform([single]($x + $s * 0.10 - 28 * $k), [single]($y + $s * 0.10 - 28 * $k))
    $g.ScaleTransform($k, $k)

    # The dashes of the selection.
    $g.FillPath((New-SolidBrush 'FFFFFF'), (ConvertTo-GraphicsPath $SnippingDashes))

    # The blade that points up: light grey, darker towards its handle as in the original.
    $g.FillPath((New-Gradient 0 56 0 192 'F1F4F7' 'C9D1D9'), (ConvertTo-GraphicsPath $SnippingBladeUp))

    # The blade that points left lies on top of it. Its upper half is lighter.
    $bladeLeft = ConvertTo-GraphicsPath $SnippingBladeLeft
    $g.FillPath((New-Gradient 192 0 56 0 'B4BEC8' 'CDD5DC'), $bladeLeft)
    $g.SetClip($bladeLeft)
    $g.FillPath((New-SolidBrush 'E3E8ED'), (ConvertTo-GraphicsPath $SnippingBladeLeftHighlight))
    $g.ResetClip()

    # The ring handles and the pivot.
    $g.FillPath((New-SolidBrush 'FFFFFF'), (ConvertTo-GraphicsPath $SnippingHandleRight))
    $g.FillPath((New-SolidBrush 'FFFFFF'), (ConvertTo-GraphicsPath $SnippingHandleBottom))
    $g.FillEllipse((New-SolidBrush 'FFFFFF'), 143.25, 143.25, 13.5, 13.5)
    $g.Restore($state)

    $brush.Dispose(); $tiles.Dispose(); $g.Dispose()
    return $bitmap
}

function Save-Png([string]$Name, [int]$Size) {
    $bitmap = New-IconBitmap $Size $Size $Size
    $bitmap.Save((Join-Path $OutputFolder $Name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

# The logo on its own, for the README.
Save-Png 'Logo.png' 256

# What Windows shows next to the app's notifications.
Save-Png 'AppIcon.png' 256

# The two pictures the setup wizard shows: the panel beside the welcome and finish
# pages, and the small one in the corner of the others. Inno Setup wants bitmaps
# without transparency, so the logo is drawn onto a solid background. Both are
# twice the size the wizard uses at 100%, which keeps them sharp on scaled displays.
function Save-WizardBitmap([string]$Name, [int]$Width, [int]$Height, [double]$IconSize, [double]$IconTop, [System.Drawing.Color]$Background) {
    $bitmap = New-Object System.Drawing.Bitmap $Width, $Height, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.Clear($Background)
    $logo = New-IconBitmap ([int]$IconSize) ([int]$IconSize) $IconSize
    $g.DrawImage($logo, [int](($Width - $IconSize) / 2), [int]$IconTop, [int]$IconSize, [int]$IconSize)
    $logo.Dispose(); $g.Dispose()
    $bitmap.Save((Join-Path $packagingFolder $Name), [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bitmap.Dispose()
}

$packagingFolder = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\packaging'
New-Item -ItemType Directory -Force $packagingFolder | Out-Null
$packagingFolder = (Resolve-Path $packagingFolder).Path
Save-WizardBitmap 'installer-side.bmp' 328 628 168 150 ([System.Drawing.Color]::FromArgb(255, 32, 32, 32))
Save-WizardBitmap 'installer-small.bmp' 110 110 94 8 ([System.Drawing.Color]::White)

# AppIcon.ico: PNG-compressed entries, which Windows has supported since Vista.
$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = foreach ($size in $sizes) {
    $bitmap = New-IconBitmap $size $size $size
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    , $stream.ToArray()
}

$ico = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $ico
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write($image) }
$writer.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $OutputFolder 'AppIcon.ico'), $ico.ToArray())
$writer.Dispose()

Write-Host "Wrote assets to $OutputFolder"
