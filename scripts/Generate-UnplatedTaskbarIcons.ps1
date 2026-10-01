$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $root 'src\Meter\assets\codex-info.ico'
$imageDirectory = Join-Path $root 'src\WidgetPackage\Images'
$sizes = @(16, 20, 24, 30, 32, 36, 40, 44, 48, 60, 64, 72, 80, 96, 256)

$sourceIcon = [System.Drawing.Icon]::new($sourcePath, 256, 256)
try {
    $source = $sourceIcon.ToBitmap()
    try {
        foreach ($size in $sizes) {
            $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            try {
                $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
                try {
                    $graphics.Clear([System.Drawing.Color]::Transparent)
                    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                    $graphics.DrawImage($source, 0, 0, $size, $size)
                }
                finally { $graphics.Dispose() }

                $path = Join-Path $imageDirectory "Square44x44Logo.targetsize-$($size)_altform-unplated.png"
                $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
                if ($bitmap.GetPixel(0, 0).A -ne 0) { throw "Taskbar icon is not transparent: $path" }
            }
            finally { $bitmap.Dispose() }
        }
    }
    finally { $source.Dispose() }
}
finally { $sourceIcon.Dispose() }
