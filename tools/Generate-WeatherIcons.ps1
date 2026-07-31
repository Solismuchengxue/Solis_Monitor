[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\reference\assets")
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$canvasSize = 65
$blue = [Drawing.ColorTranslator]::FromHtml("#368EFF")
$lightBlue = [Drawing.ColorTranslator]::FromHtml("#ADD1FF")
$yellow = [Drawing.ColorTranslator]::FromHtml("#FFB615")
$warm = [Drawing.ColorTranslator]::FromHtml("#FF625E")
$referenceAssetDirectory = Join-Path $PSScriptRoot "..\reference\assets"

function New-RoundPen {
    param(
        [Drawing.Color]$Color,
        [float]$Width
    )

    $pen = [Drawing.Pen]::new($Color, $Width)
    $pen.StartCap = [Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    return $pen
}

function Draw-Cloud {
    param(
        [Drawing.Graphics]$Graphics,
        [float]$X,
        [float]$Y,
        [float]$Scale = 1.0,
        [Drawing.Color]$Color = $blue
    )

    $brush = [Drawing.SolidBrush]::new($Color)
    try {
        $Graphics.FillEllipse($brush, $X, $Y + 12 * $Scale, 22 * $Scale, 22 * $Scale)
        $Graphics.FillEllipse($brush, $X + 12 * $Scale, $Y + 4 * $Scale, 29 * $Scale, 29 * $Scale)
        $Graphics.FillEllipse($brush, $X + 31 * $Scale, $Y + 13 * $Scale, 18 * $Scale, 18 * $Scale)
        $Graphics.FillRectangle(
            $brush,
            $X + 10 * $Scale,
            $Y + 20 * $Scale,
            32 * $Scale,
            14 * $Scale)
    }
    finally {
        $brush.Dispose()
    }
}

function Draw-Crescent {
    param(
        [Drawing.Graphics]$Graphics,
        [float]$X,
        [float]$Y,
        [float]$Diameter
    )

    $yellowBrush = [Drawing.SolidBrush]::new($yellow)
    $transparentBrush = [Drawing.SolidBrush]::new([Drawing.Color]::Transparent)
    try {
        $Graphics.FillEllipse($yellowBrush, $X, $Y, $Diameter, $Diameter)
        $previousMode = $Graphics.CompositingMode
        $Graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
        $Graphics.FillEllipse(
            $transparentBrush,
            $X + $Diameter * 0.34,
            $Y - $Diameter * 0.08,
            $Diameter,
            $Diameter)
        $Graphics.CompositingMode = $previousMode
    }
    finally {
        $transparentBrush.Dispose()
        $yellowBrush.Dispose()
    }
}

function Draw-Rain {
    param(
        [Drawing.Graphics]$Graphics,
        [Drawing.Color]$Color = $yellow,
        [float]$OffsetX = 0,
        [float]$OffsetY = 0
    )

    $pen = New-RoundPen -Color $Color -Width 4
    try {
        $Graphics.DrawLine($pen, 22 + $OffsetX, 43 + $OffsetY, 17 + $OffsetX, 54 + $OffsetY)
        $Graphics.DrawLine($pen, 34 + $OffsetX, 43 + $OffsetY, 29 + $OffsetX, 54 + $OffsetY)
        $Graphics.DrawLine($pen, 46 + $OffsetX, 43 + $OffsetY, 41 + $OffsetX, 54 + $OffsetY)
    }
    finally {
        $pen.Dispose()
    }
}

function Draw-SnowflakeSymbol {
    param(
        [Drawing.Graphics]$Graphics,
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$FontSize,
        [Drawing.Color]$Color
    )

    $glyph = [Drawing.Bitmap]::new(
        64,
        64,
        [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $glyphGraphics = [Drawing.Graphics]::FromImage($glyph)
    $font = [Drawing.Font]::new(
        "Segoe UI Symbol",
        $FontSize,
        [Drawing.FontStyle]::Regular,
        [Drawing.GraphicsUnit]::Pixel)
    $brush = [Drawing.SolidBrush]::new($Color)
    $format = [Drawing.StringFormat]::new()
    try {
        $glyphGraphics.Clear([Drawing.Color]::Transparent)
        $glyphGraphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $format.Alignment = [Drawing.StringAlignment]::Center
        $format.LineAlignment = [Drawing.StringAlignment]::Center
        $glyphGraphics.DrawString(
            [string][char]0x2744,
            $font,
            $brush,
            [Drawing.RectangleF]::new(0, 0, 64, 64),
            $format)

        $minX = 64
        $minY = 64
        $maxX = -1
        $maxY = -1
        for ($glyphY = 0; $glyphY -lt 64; $glyphY++) {
            for ($glyphX = 0; $glyphX -lt 64; $glyphX++) {
                if ($glyph.GetPixel($glyphX, $glyphY).A -gt 0) {
                    $minX = [Math]::Min($minX, $glyphX)
                    $minY = [Math]::Min($minY, $glyphY)
                    $maxX = [Math]::Max($maxX, $glyphX)
                    $maxY = [Math]::Max($maxY, $glyphY)
                }
            }
        }
        if ($maxX -lt $minX -or $maxY -lt $minY) {
            throw "Segoe UI Symbol does not contain the snowflake glyph."
        }

        $Graphics.DrawImage(
            $glyph,
            [Drawing.RectangleF]::new($X, $Y, $Width, $Height),
            [Drawing.RectangleF]::new(
                $minX,
                $minY,
                $maxX - $minX + 1,
                $maxY - $minY + 1),
            [Drawing.GraphicsUnit]::Pixel)
    }
    finally {
        $format.Dispose()
        $brush.Dispose()
        $font.Dispose()
        $glyphGraphics.Dispose()
        $glyph.Dispose()
    }
}

function Draw-Thermometer {
    param(
        [Drawing.Graphics]$Graphics,
        [float]$X,
        [float]$Y,
        [Drawing.Color]$Color
    )

    $pen = New-RoundPen -Color $Color -Width 7
    $brush = [Drawing.SolidBrush]::new($Color)
    try {
        $Graphics.DrawLine($pen, $X, $Y, $X, $Y + 24)
        $Graphics.FillEllipse($brush, $X - 6, $Y + 20, 12, 12)
    }
    finally {
        $brush.Dispose()
        $pen.Dispose()
    }
}

function Draw-ReferenceAsset {
    param(
        [Drawing.Graphics]$Graphics,
        [string]$Name,
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height
    )

    $path = Join-Path $referenceAssetDirectory $Name
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Reference weather asset not found: $path"
    }

    $image = [Drawing.Image]::FromFile($path)
    try {
        $Graphics.DrawImage($image, $X, $Y, $Width, $Height)
    }
    finally {
        $image.Dispose()
    }
}

function New-WeatherIcon {
    param(
        [string]$Name,
        [scriptblock]$Draw
    )

    $bitmap = [Drawing.Bitmap]::new(
        $canvasSize,
        $canvasSize,
        [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        & $Draw $graphics

        $path = Join-Path $OutputDirectory $Name
        $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
        Write-Host "Generated $path"
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

New-WeatherIcon "m18.png" {
    param($g)
    Draw-Crescent $g 11 10 42
}

New-WeatherIcon "m19.png" {
    param($g)
    Draw-Crescent $g 29 4 31
    Draw-Cloud $g 8 20 1.0
}

New-WeatherIcon "m20.png" {
    param($g)
    Draw-Crescent $g 34 1 29
    Draw-Cloud $g 8 13 1.0
    Draw-Rain $g $yellow 0 2
}

New-WeatherIcon "m21.png" {
    param($g)
    Draw-Cloud $g 8 3 1.0 $lightBlue
    $pen = New-RoundPen -Color $blue -Width 4
    try {
        $g.DrawLine($pen, 10, 43, 51, 43)
        $g.DrawLine($pen, 17, 51, 56, 51)
        $g.DrawLine($pen, 8, 59, 43, 59)
    }
    finally {
        $pen.Dispose()
    }
}

New-WeatherIcon "m22.png" {
    param($g)
    Draw-ReferenceAsset $g "m00.png" 2 0 45 45
    $pen = New-RoundPen -Color $lightBlue -Width 5
    try {
        $g.DrawLine($pen, 9, 40, 55, 40)
        $g.DrawLine($pen, 15, 49, 58, 49)
        $g.DrawLine($pen, 7, 58, 47, 58)
    }
    finally {
        $pen.Dispose()
    }
}

New-WeatherIcon "m23.png" {
    param($g)
    $pen = New-RoundPen -Color $yellow -Width 4.5
    $brush = [Drawing.SolidBrush]::new($yellow)
    $top = [Drawing.Drawing2D.GraphicsPath]::new()
    $bottom = [Drawing.Drawing2D.GraphicsPath]::new()
    try {
        $top.StartFigure()
        $top.AddLine(8, 18, 45, 18)
        $top.AddBezier(45, 18, 56, 18, 56, 30, 46, 30)
        $g.DrawPath($pen, $top)

        $g.DrawLine($pen, 17, 37, 57, 37)

        $bottom.StartFigure()
        $bottom.AddLine(8, 53, 40, 53)
        $bottom.AddBezier(40, 53, 49, 53, 50, 61, 43, 61)
        $g.DrawPath($pen, $bottom)

        $g.FillEllipse($brush, 6, 32, 6, 6)
        $g.FillEllipse($brush, 53, 49, 7, 7)
    }
    finally {
        $bottom.Dispose()
        $top.Dispose()
        $brush.Dispose()
        $pen.Dispose()
    }
}

New-WeatherIcon "m24.png" {
    param($g)
    Draw-ReferenceAsset $g "m00.png" 0 1 46 46
    Draw-Thermometer $g 47 21 $warm
}

New-WeatherIcon "m25.png" {
    param($g)
    Draw-SnowflakeSymbol $g 4 5 38 38 54 $blue
    Draw-Thermometer $g 49 20 $lightBlue
}

New-WeatherIcon "m26.png" {
    param($g)
    Draw-Cloud $g 8 16 1.0
    $pen = New-RoundPen -Color $yellow -Width 6
    $brush = [Drawing.SolidBrush]::new($yellow)
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    try {
        $path.AddBezier(25, 27, 26, 17, 43, 18, 42, 29)
        $path.AddBezier(42, 29, 42, 36, 34, 35, 34, 42)
        $g.DrawPath($pen, $path)
        $g.FillEllipse($brush, 31, 49, 7, 7)
    }
    finally {
        $path.Dispose()
        $brush.Dispose()
        $pen.Dispose()
    }
}
