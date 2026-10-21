# Independently drawn geometric source; no third-party artwork.
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object Drawing.Bitmap 256, 256
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$path = New-Object Drawing.Drawing2D.GraphicsPath
$path.AddLines([Drawing.PointF[]]@([Drawing.PointF]::new(128,12),[Drawing.PointF]::new(228,58),[Drawing.PointF]::new(228,130)))
$path.AddBezier(228,130,228,181,179,221,128,245)
$path.AddBezier(128,245,77,221,28,181,28,130)
$path.AddLines([Drawing.PointF[]]@([Drawing.PointF]::new(28,130),[Drawing.PointF]::new(28,58),[Drawing.PointF]::new(128,12)))
$path.CloseFigure()
$fill = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(23,101,209))
$graphics.FillPath($fill,$path)
$pen = New-Object Drawing.Pen ([Drawing.Color]::White),14
$pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
$pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
$graphics.DrawLines($pen,[Drawing.PointF[]]@([Drawing.PointF]::new(91,92),[Drawing.PointF]::new(58,127),[Drawing.PointF]::new(91,162)))
$graphics.DrawLines($pen,[Drawing.PointF[]]@([Drawing.PointF]::new(165,92),[Drawing.PointF]::new(198,127),[Drawing.PointF]::new(165,162)))
$graphics.DrawLine($pen,141,84,115,170)
$png = New-Object IO.MemoryStream
$bitmap.Save($png,[Drawing.Imaging.ImageFormat]::Png)
$bytes=$png.ToArray()
$destination=Join-Path (Split-Path $PSScriptRoot) 'assets\icon.ico'
$stream=[IO.File]::Create($destination)
$writer=New-Object IO.BinaryWriter $stream
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]1)
$writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0)
$writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$bytes.Length); $writer.Write([uint32]22)
$writer.Write($bytes)
$writer.Dispose(); $png.Dispose(); $pen.Dispose(); $fill.Dispose(); $path.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
