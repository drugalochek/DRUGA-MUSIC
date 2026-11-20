$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition 'using System;
using System.Runtime.InteropServices;
public static class NativeMethods { [DllImport("user32.dll", SetLastError=true)] public static extern bool DestroyIcon(IntPtr hIcon); }'

$source = Join-Path $PSScriptRoot "..\1795143870953.png"
$assets = Join-Path $PSScriptRoot "..\DrugaMusic\Assets"
$target = Join-Path $assets "sonora.ico"

if (-not (Test-Path $source)) { throw "Repository icon source was not found: $source" }
New-Item -ItemType Directory -Force -Path $assets | Out-Null

$inputImage = [System.Drawing.Image]::FromFile($source)
try {
    $bitmap = [System.Drawing.Bitmap]::new(256, 256, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::Black)
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $side = [Math]::Min($inputImage.Width, $inputImage.Height)
            $left = [int](($inputImage.Width - $side) / 2)
            $top = [int](($inputImage.Height - $side) / 2)
            $sourceRect = [System.Drawing.Rectangle]::new($left, $top, $side, $side)
            $destRect = [System.Drawing.Rectangle]::new(0, 0, 256, 256)
            $graphics.DrawImage($inputImage, $destRect, $sourceRect, [System.Drawing.GraphicsUnit]::Pixel)
        } finally { $graphics.Dispose() }

        $handle = $bitmap.GetHicon()
        try {
            $icon = [System.Drawing.Icon]::FromHandle($handle)
            $stream = [System.IO.File]::Create($target)
            try { $icon.Save($stream) } finally { $stream.Dispose() }
        } finally {
            [void][NativeMethods]::DestroyIcon($handle)
        }
    } finally { $bitmap.Dispose() }
} finally { $inputImage.Dispose() }

Write-Host "Generated $target from repository artwork."
