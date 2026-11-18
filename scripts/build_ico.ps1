Add-Type -AssemblyName System.Drawing

$baseDir = "E:\AntiGravDev\SongSync\assets\images"
$outPath = Join-Path $baseDir "app_icon.ico"

$sizes = @(16, 24, 32, 48, 256)
$entries = @()
$streams = @()

foreach ($size in $sizes) {
    $srcPath = Join-Path $baseDir "icon_${size}x${size}.png"
    if (-not (Test-Path $srcPath)) {
        if ($size -eq 24) {
            $srcPath = Join-Path $baseDir "icon_32x32.png"
        } elseif ($size -eq 48) {
            $srcPath = Join-Path $baseDir "icon_64x64.png"
        } else {
            $srcPath = Join-Path $baseDir "icon_512x512.png"
        }
    }

    $srcBmp = [System.Drawing.Bitmap]::FromFile($srcPath)
    $targetBmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($targetBmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.DrawImage($srcBmp, 0, 0, $size, $size)
    $g.Dispose()
    $srcBmp.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $targetBmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $targetBmp.Dispose()

    $streams += ,$ms
    $entries += ,@{
        Width = $size
        Height = $size
        Length = $ms.Length
    }
}

$fileStream = [System.IO.File]::Create($outPath)
$bw = New-Object System.IO.BinaryWriter($fileStream)

# Header
$bw.Write([int16]0) # Reserved
$bw.Write([int16]1) # Type (1 = ICO)
$bw.Write([int16]$sizes.Count) # Image count

# Directory Entries
$offset = 6 + ($sizes.Count * 16)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $w = if ($entries[$i].Width -ge 256) { 0 } else { [byte]$entries[$i].Width }
    $h = if ($entries[$i].Height -ge 256) { 0 } else { [byte]$entries[$i].Height }
    $bw.Write([byte]$w)
    $bw.Write([byte]$h)
    $bw.Write([byte]0) # Color count
    $bw.Write([byte]0) # Reserved
    $bw.Write([int16]1) # Planes
    $bw.Write([int16]32) # Bits per pixel
    $bw.Write([int32]$entries[$i].Length) # Bytes in resource
    $bw.Write([int32]$offset) # Offset

    $offset += $entries[$i].Length
}

# Image Data
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $bytes = $streams[$i].ToArray()
    $bw.Write($bytes)
    $streams[$i].Dispose()
}

$bw.Flush()
$bw.Close()
$fileStream.Close()

Write-Output "Generated $outPath successfully."
