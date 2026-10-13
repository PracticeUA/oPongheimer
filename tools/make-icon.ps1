# Малює іконку застосунку (поле, дві ракетки, м'яч) і збирає її в багаторозмірний .ico.
# Запуск: powershell -File tools\make-icon.ps1
param([string]$OutFile = "$PSScriptRoot\..\src\Pongheimer.App\Assets\app.ico")

Add-Type -AssemblyName System.Drawing

function New-RoundedRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Кольори ті самі, що в Themes/Theme.xaml.
    $field = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#11161D'))
    $left = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#4FC3F7'))
    $right = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#FFB74D'))
    $ball = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)

    $s = [single]$size
    $paddleW = [Math]::Max(2, $s * 0.125)
    $paddleH = $s * 0.42
    $paddleR = $paddleW * 0.3
    $ballD = [Math]::Max(3, $s * 0.17)

    $g.FillPath($field, (New-RoundedRect 0 0 $s $s ($s * 0.2)))

    # Тонка рамка, щоб темне поле не зливалося з темною панеллю завдань.
    $edgeWidth = [Math]::Max(1, $s * 0.035)
    $edge = New-Object System.Drawing.Pen ([System.Drawing.ColorTranslator]::FromHtml('#3A4656')), $edgeWidth
    $inset = $edgeWidth / 2
    $g.DrawPath($edge, (New-RoundedRect $inset $inset ($s - $edgeWidth) ($s - $edgeWidth) ($s * 0.2 - $inset)))

    $g.FillPath($left, (New-RoundedRect ($s * 0.15) ($s * 0.17) $paddleW $paddleH $paddleR))
    $g.FillPath($right, (New-RoundedRect ($s * 0.85 - $paddleW) ($s * 0.41) $paddleW $paddleH $paddleR))
    $g.FillEllipse($ball, $s * 0.5 - $ballD / 2, $s * 0.36, $ballD, $ballD)

    $g.Dispose()
    return $bmp
}

function ConvertTo-Png([System.Drawing.Bitmap]$bmp) {
    $stream = New-Object System.IO.MemoryStream
    $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $stream.ToArray()
}

# Класичний запис іконки: заголовок DIB, пікселі BGRA знизу догори, далі 1-бітна маска прозорості.
# Так зберігаємо всі розміри, крім 256: PNG у малих розмірах розуміють не всі API Windows.
function ConvertTo-Dib([System.Drawing.Bitmap]$bmp) {
    $size = $bmp.Width
    $rect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
    $data = $bmp.LockBits($rect, 'ReadOnly', [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = New-Object byte[] ($size * $size * 4)
    for ($row = 0; $row -lt $size; $row++) {
        $source = [IntPtr]::Add($data.Scan0, $row * $data.Stride)
        [System.Runtime.InteropServices.Marshal]::Copy($source, $pixels, ($size - 1 - $row) * $size * 4, $size * 4)
    }
    $bmp.UnlockBits($data)

    $maskSize = [int]([Math]::Ceiling($size / 32.0) * 4) * $size
    $stream = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $stream
    $w.Write([uint32]40); $w.Write([int32]$size); $w.Write([int32]($size * 2))
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]0)
    $w.Write([uint32]($pixels.Length + $maskSize))
    $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
    $w.Write($pixels)
    $w.Write((New-Object byte[] $maskSize))   # прозорість задає альфа-канал, маска порожня
    $w.Flush()
    return , $stream.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = $sizes | ForEach-Object {
    $bmp = New-IconBitmap $_
    if ($_ -ge 256) { , (ConvertTo-Png $bmp) } else { , (ConvertTo-Dib $bmp) }
    $bmp.Dispose()
}

New-Item -ItemType Directory -Force (Split-Path $OutFile) | Out-Null
$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $out
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }   # 0 у форматі ICO означає 256
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write([byte[]]$image) }
$writer.Flush()
[System.IO.File]::WriteAllBytes($OutFile, $out.ToArray())
"Wrote $OutFile ($($out.Length) bytes, sizes: $($sizes -join ', '))"
