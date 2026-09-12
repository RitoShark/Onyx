$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$assetDirectory = Join-Path $PSScriptRoot '../Onyx/Assets'
[xml]$svg = Get-Content -LiteralPath (Join-Path $assetDirectory 'onyx-logo.svg') -Raw
$paths = $svg.DocumentElement.ChildNodes | Where-Object LocalName -eq 'path'
$drawings = foreach ($path in $paths) {
    '            <GeometryDrawing Brush="{0}" Geometry="{1}" />' -f $path.fill, $path.d
}
$drawingXaml = @"
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <DrawingImage x:Key="OnyxLogo">
        <DrawingImage.Drawing>
            <DrawingGroup>
$($drawings -join "`n")
            </DrawingGroup>
        </DrawingImage.Drawing>
    </DrawingImage>
</ResourceDictionary>
"@
[IO.File]::WriteAllText((Join-Path $assetDirectory 'Brand.xaml'), $drawingXaml)
$frames = foreach ($size in @(16, 24, 32, 48, 64, 128, 256, 512)) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    $context.PushTransform((New-Object System.Windows.Media.ScaleTransform ($size / 512), ($size / 512)))
    foreach ($path in $paths) {
        $brush = [Windows.Media.BrushConverter]::new().ConvertFromInvariantString($path.fill)
        $context.DrawGeometry($brush, $null, [Windows.Media.Geometry]::Parse($path.d))
    }
    $context.Pop()
    $context.Close()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    $encoder.Save($stream)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    if ($size -eq 512) {
        [IO.File]::WriteAllBytes((Join-Path $assetDirectory 'onyx-logo.png'), $bytes)
    } else {
        [pscustomobject]@{ Size = $size; Bytes = $bytes }
    }
}
$iconStream = [IO.File]::Create((Join-Path $assetDirectory 'onyx.ico'))
$writer = [IO.BinaryWriter]::new($iconStream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally {
    $writer.Dispose()
}
