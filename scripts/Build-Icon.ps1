Add-Type -AssemblyName System.Drawing
$assetDir = Join-Path $PSScriptRoot '..\src\DeskPlan\Assets'
[void][System.IO.Directory]::CreateDirectory($assetDir)
function RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = $r * 2
    $p.AddArc($x,$y,$d,$d,180,90)
    $p.AddArc($x+$w-$d,$y,$d,$d,270,90)
    $p.AddArc($x+$w-$d,$y+$h-$d,$d,$d,0,90)
    $p.AddArc($x,$y+$h-$d,$d,$d,90,90)
    $p.CloseFigure()
    return ,$p
}
$frames = [System.Collections.Generic.List[byte[]]]::new()
$sizes = @(16,24,32,48,64,128,256)
foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new($size,$size)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.ScaleTransform($size/256.0,$size/256.0)
    $base = RoundedPath 12 12 232 232 52
    $paper = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#F5F5F7'))
    $red = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#FF453A'))
    $ink = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#303033'))
    $muted = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#D1D1D6'))
    $g.FillPath($paper,$base)
    $g.SetClip($base)
    $g.FillRectangle($red,12,12,232,64)
    $g.ResetClip()
    foreach ($x in @(77,165)) {
        $binding = RoundedPath $x 32 14 26 7
        $g.FillPath($paper,$binding)
        $binding.Dispose()
    }
    foreach ($point in @(@(54,103),@(112,103),@(170,103),@(54,161),@(112,161),@(170,161))) {
        $tile = RoundedPath $point[0] $point[1] 32 32 8
        if ($point[0] -eq 112 -and $point[1] -eq 161) { $g.FillPath($red,$tile) }
        elseif ($point[1] -eq 103) { $g.FillPath($ink,$tile) }
        else { $g.FillPath($muted,$tile) }
        $tile.Dispose()
    }
    $memory = [System.IO.MemoryStream]::new()
    $bitmap.Save($memory,[System.Drawing.Imaging.ImageFormat]::Png)
    $frames.Add($memory.ToArray())
    if ($size -eq 256) { $bitmap.Save((Join-Path $assetDir 'DeskPlan.png'),[System.Drawing.Imaging.ImageFormat]::Png) }
    $memory.Dispose(); $g.Dispose(); $bitmap.Dispose()
    $base.Dispose(); $paper.Dispose(); $red.Dispose(); $ink.Dispose(); $muted.Dispose()
}
$stream = [System.IO.File]::Create((Join-Path $assetDir 'DeskPlan.ico'))
$writer = [System.IO.BinaryWriter]::new($stream)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
for ($i = 0; $i -lt $frames.Count; $i++) {
    $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($frame in $frames) { $writer.Write($frame) }
$writer.Dispose(); $stream.Dispose()
