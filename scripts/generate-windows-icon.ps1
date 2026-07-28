[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedRectanglePath {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconPng {
    param([int]$Size)

    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        $outerInset = [float]($Size * 0.035)
        $outerSize = [float]($Size - ($outerInset * 2))
        $outerPath = New-RoundedRectanglePath $outerInset $outerInset $outerSize $outerSize ([float]($Size * 0.205))
        $background = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 20, 18, 28))
        $border = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 55, 48, 72), [float][Math]::Max(1, $Size * 0.012))
        try {
            $graphics.FillPath($background, $outerPath)
            $graphics.DrawPath($border, $outerPath)
        }
        finally {
            $outerPath.Dispose()
            $background.Dispose()
            $border.Dispose()
        }

        $glyph = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 196, 181, 253), [float][Math]::Max(1.35, $Size * 0.052))
        $glyph.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $glyph.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $glyph.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        try {
            foreach ($angle in @(0, 60, 120)) {
                $state = $graphics.Save()
                try {
                    $graphics.TranslateTransform([float]($Size / 2), [float]($Size / 2))
                    $graphics.RotateTransform([float]$angle)
                    $graphics.TranslateTransform([float](-$Size / 2), [float](-$Size / 2))
                    $capsule = New-RoundedRectanglePath `
                        ([float]($Size * 0.235)) `
                        ([float]($Size * 0.395)) `
                        ([float]($Size * 0.53)) `
                        ([float]($Size * 0.21)) `
                        ([float]($Size * 0.105))
                    try {
                        $graphics.DrawPath($glyph, $capsule)
                    }
                    finally {
                        $capsule.Dispose()
                    }
                }
                finally {
                    $graphics.Restore($state)
                }
            }
        }
        finally {
            $glyph.Dispose()
        }

        $center = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 20, 18, 28))
        $centerDot = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 196, 181, 253))
        try {
            $graphics.FillEllipse($center, [float]($Size * 0.405), [float]($Size * 0.405), [float]($Size * 0.19), [float]($Size * 0.19))
            $graphics.FillEllipse($centerDot, [float]($Size * 0.468), [float]($Size * 0.468), [float]($Size * 0.064), [float]($Size * 0.064))
        }
        finally {
            $center.Dispose()
            $centerDot.Dispose()
        }

        $stream = New-Object System.IO.MemoryStream
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            return $stream.ToArray()
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = foreach ($size in $sizes) {
    [PSCustomObject]@{
        Size = $size
        Data = [byte[]](New-IconPng $size)
    }
}

$fullPath = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($fullPath)) | Out-Null
$file = [System.IO.File]::Create($fullPath)
$writer = New-Object System.IO.BinaryWriter($file)
try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$images.Count)

    $offset = 6 + (16 * $images.Count)
    foreach ($image in $images) {
        $dimension = if ($image.Size -ge 256) { 0 } else { $image.Size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$image.Data.Length)
        $writer.Write([UInt32]$offset)
        $offset += $image.Data.Length
    }

    foreach ($image in $images) {
        $writer.Write($image.Data)
    }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}

Write-Host "Generated Windows icon: $fullPath"
