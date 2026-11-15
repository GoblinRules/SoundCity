param([Parameter(Mandatory = $true)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$taskRoot = Split-Path -Parent $PSScriptRoot
$bitmap = New-Object Drawing.Bitmap(492, 942)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$icon = [Drawing.Image]::FromFile((Join-Path $taskRoot 'assets\soundcity-icon.png'))
$titleFont = New-Object Drawing.Font('Segoe UI', 36, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
$captionFont = New-Object Drawing.Font('Segoe UI', 17, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
$ink = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(243, 241, 236))
$muted = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(165, 175, 191))
$orange = New-Object Drawing.Pen([Drawing.Color]::FromArgb(255, 152, 76), 3)
$format = New-Object Drawing.StringFormat
$format.Alignment = [Drawing.StringAlignment]::Center
try {
    $graphics.Clear([Drawing.Color]::FromArgb(9, 11, 16))
    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $graphics.DrawImage($icon, 126, 192, 240, 240)
    $graphics.DrawString('SoundCity', $titleFont, $ink, (New-Object Drawing.RectangleF(0, 485, 492, 60)), $format)
    $graphics.DrawLine($orange, 206, 570, 286, 570)
    $graphics.DrawString('YOUR SOUND. YOUR SPACE.', $captionFont, $muted, (New-Object Drawing.RectangleF(0, 603, 492, 40)), $format)
    $graphics.DrawString('WINDOWS DESKTOP', $captionFont, $muted, (New-Object Drawing.RectangleF(0, 843, 492, 40)), $format)
    $bitmap.Save([IO.Path]::GetFullPath($OutputPath), [Drawing.Imaging.ImageFormat]::Png)
} finally {
    foreach ($resource in @($format, $orange, $muted, $ink, $captionFont, $titleFont, $icon, $graphics, $bitmap)) { $resource.Dispose() }
}
