<#
  层：工具 —— 生成示例 Mod 用的 PNG（自己写 PNG 容器 + zlib，不引图像库，也不用游戏美术）

  用法：
    pwsh -File tools/make-sample-texture.ps1 -Out samples/TextureMod/assets/pylon.png
    pwsh -File tools/make-sample-texture.ps1 -Out samples/FrameMod/assets/npcs/TestNpc.png -Size 32 -Style slime
    pwsh -File tools/make-sample-texture.ps1 -Transparent1x1 -Hex   # 内建常量用
#>
[CmdletBinding(DefaultParameterSetName = 'Sample')]
param(
    [Parameter(ParameterSetName = 'Sample')]
    [string]$Out = 'samples/TextureMod/assets/pylon.png',
    [Parameter(ParameterSetName = 'Sample')]
    [int]$Size = 32,
    [Parameter(ParameterSetName = 'Sample')]
    [ValidateSet('slime', 'potion', 'buff', 'fireball', 'sword', 'crystal', 'material')]
    [string]$Style,
    [Parameter(ParameterSetName = 'Transparent')]
    [switch]$Transparent1x1,
    [switch]$Hex
)

$ErrorActionPreference = 'Stop'

function Get-Crc32 {
    param([byte[]]$Data)
    # 全程 uint64 + 十进制常量：PowerShell 把 0xFFFFFFFF 这类字面量解析成 int32(-1)，
    # 而 -bxor/-shl 在 int32 上会溢出成负数，转 uint32 时直接报错。
    [uint64]$crc = 4294967295
    foreach ($b in $Data) {
        $crc = $crc -bxor [uint64]$b
        for ($i = 0; $i -lt 8; $i++) {
            if ($crc -band 1) { $crc = (($crc -shr 1) -bxor [uint64]3988292384) } else { $crc = $crc -shr 1 }
        }
    }
    return [uint32]($crc -bxor [uint64]4294967295)
}

function Get-Adler32 {
    param([byte[]]$Data)
    [uint32]$a = 1; [uint32]$b = 0
    foreach ($byte in $Data) {
        $a = ($a + $byte) % 65521
        $b = ($b + $a) % 65521
    }
    return [uint32](($b -shl 16) -bor $a)
}

function Compress-Zlib {
    param([byte[]]$Raw)
    $ms = [System.IO.MemoryStream]::new()
    $ms.WriteByte(0x78); $ms.WriteByte(0x9C)                      # zlib 头（默认压缩级别）
    $deflate = [System.IO.Compression.DeflateStream]::new(
        $ms, [System.IO.Compression.CompressionLevel]::Optimal, $true)
    $deflate.Write($Raw, 0, $Raw.Length)
    $deflate.Dispose()                                            # leaveOpen：zlib 尾要接在 deflate 流后面
    $adler = Get-Adler32 -Data $Raw
    foreach ($shift in 24, 16, 8, 0) { $ms.WriteByte([byte](($adler -shr $shift) -band 0xFF)) }
    return $ms.ToArray()
}

function New-Chunk {
    param([string]$Type, [byte[]]$Data)
    $body = [byte[]]::new(4 + $Data.Length)
    [System.Text.Encoding]::ASCII.GetBytes($Type).CopyTo($body, 0)
    $Data.CopyTo($body, 4)
    $ms = [System.IO.MemoryStream]::new()
    foreach ($shift in 24, 16, 8, 0) { $ms.WriteByte([byte](($Data.Length -shr $shift) -band 0xFF)) }
    $ms.Write($body, 0, $body.Length)
    $crc = Get-Crc32 -Data $body
    foreach ($shift in 24, 16, 8, 0) { $ms.WriteByte([byte](($crc -shr $shift) -band 0xFF)) }
    return $ms.ToArray()
}

function New-Png {
    param([int]$Width, [int]$Height, [scriptblock]$Pixel)

    $raw = [System.IO.MemoryStream]::new()
    for ($y = 0; $y -lt $Height; $y++) {
        $raw.WriteByte(0)                                          # 每行的过滤器类型：0 = None
        for ($x = 0; $x -lt $Width; $x++) {
            $rgba = & $Pixel $x $y
            foreach ($v in $rgba) { $raw.WriteByte([byte]$v) }
        }
    }

    $ihdr = [System.IO.MemoryStream]::new()
    foreach ($v in @($Width, $Height)) { foreach ($shift in 24, 16, 8, 0) { $ihdr.WriteByte([byte](($v -shr $shift) -band 0xFF)) } }
    $ihdr.WriteByte(8)                                             # 位深
    $ihdr.WriteByte(6)                                             # 颜色类型 6 = RGBA
    $ihdr.WriteByte(0); $ihdr.WriteByte(0); $ihdr.WriteByte(0)      # 压缩/过滤/隔行

    $png = [System.IO.MemoryStream]::new()
    $png.Write([byte[]](0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A), 0, 8)
    $png.Write((New-Chunk -Type 'IHDR' -Data $ihdr.ToArray()), 0, 25)
    $idat = New-Chunk -Type 'IDAT' -Data (Compress-Zlib -Raw $raw.ToArray())
    $png.Write($idat, 0, $idat.Length)
    $png.Write((New-Chunk -Type 'IEND' -Data ([byte[]]::new(0))), 0, 12)
    return $png.ToArray()
}

if ($Transparent1x1) {
    $png = New-Png -Width 1 -Height 1 -Pixel { param($x, $y) @(0, 0, 0, 0) }
} elseif ($Style) {
    # 示例内容各用一张明显不同的图（不然怪物/药水/增益全是同一张棋盘，根本分不清），
    # 仍然一眼看出"不是游戏美术"：纯几何色块。
    $s = $Size
    $cx = [math]::Floor(($s - 1) / 2)
    $cy = [math]::Floor(($s - 1) / 2)
    $png = New-Png -Width $s -Height $s -Pixel {
        param($x, $y)
        $dx = $x - $cx
        $dy = $y - $cy
        $d = [math]::Sqrt($dx * $dx + $dy * $dy)

        switch ($Style) {
            'slime' {   # 绿色史莱姆：圆身 + 两只眼 + 高光
                if ($d -le ($s * 0.46)) {
                    if ($d -le ($s * 0.10)) { return @(255, 255, 255, 255) }
                    if (($x -ge ($cx - [math]::Floor($s * 0.18))) -and ($x -le ($cx - [math]::Floor($s * 0.06))) -and
                        ($y -ge ($cy - [math]::Floor($s * 0.16))) -and ($y -le ($cy - [math]::Floor($s * 0.04)))) { return @(20, 20, 30, 255) }
                    if (($x -ge ($cx + [math]::Floor($s * 0.06))) -and ($x -le ($cx + [math]::Floor($s * 0.18))) -and
                        ($y -ge ($cy - [math]::Floor($s * 0.16))) -and ($y -le ($cy - [math]::Floor($s * 0.04)))) { return @(20, 20, 30, 255) }
                    return @(86, 196, 90, 255)
                }
                if ($d -le ($s * 0.49)) { return @(52, 140, 60, 255) }
                return @(0, 0, 0, 0)
            }
            'potion' {  # 药水：玻璃瓶 + 红色液体 + 瓶塞
                $bodyW = [math]::Floor($s * 0.30)
                $neckW = [math]::Floor($s * 0.12)
                if ($y -ge ($cy + [math]::Floor($s * 0.02)) -and
                    (($x -ge ($cx - $bodyW)) -and ($x -le ($cx + $bodyW)))) {
                    if (($x -ge ($cx - $bodyW + 1)) -and ($x -le ($cx + $bodyW - 1))) { return @(214, 64, 76, 255) }
                    return @(240, 240, 250, 255)
                }
                if ($y -ge ($cy - [math]::Floor($s * 0.22)) -and $y -lt ($cy + [math]::Floor($s * 0.02)) -and
                    (($x -ge ($cx - $neckW)) -and ($x -le ($cx + $neckW)))) { return @(240, 240, 250, 255) }
                if ($y -lt ($cy - [math]::Floor($s * 0.22)) -and $y -ge ($cy - [math]::Floor($s * 0.32)) -and
                    (($x -ge ($cx - $neckW - 1)) -and ($x -le ($cx + $neckW + 1)))) { return @(150, 100, 50, 255) }
                return @(0, 0, 0, 0)
            }
            'buff' {    # 增益：绿色向上箭头 + 金色底
                if ($y -ge ($cy + 1) -and $y -le ($cy + [math]::Floor($s * 0.30)) -and
                    (($x -ge ($cx - [math]::Floor($s * 0.06))) -and ($x -le ($cx + [math]::Floor($s * 0.06))))) { return @(240, 210, 90, 255) }
                if ($y -ge ($cy - [math]::Floor($s * 0.34)) -and $y -le ($cy + 1) -and
                    ([math]::Abs($dx) -le ([math]::Floor($s * 0.30) - [math]::Abs($dy)))) { return @(110, 220, 120, 255) }
                return @(0, 0, 0, 0)
            }
            'fireball' { # 火球：橙黄圆 + 亮核
                if ($d -le ($s * 0.30)) { return @(255, 246, 170, 255) }
                if ($d -le ($s * 0.46)) { return @(255, 150, 40, 255) }
                if ($d -le ($s * 0.50)) { return @(200, 70, 20, 255) }
                return @(0, 0, 0, 0)
            }
            'sword' {   # 剑：斜的钢刃 + 金色护手
                if ([math]::Abs(($x - $cx) + ($y - $cy)) -le 1 -and $d -le ($s * 0.44)) { return @(225, 232, 245, 255) }
                if ($y -ge ($cy + [math]::Floor($s * 0.30)) -and
                    ([math]::Abs($dx) -le [math]::Floor($s * 0.14))) { return @(210, 170, 60, 255) }
                if ($y -ge ($cy + [math]::Floor($s * 0.34)) -and
                    ([math]::Abs($dx) -le [math]::Floor($s * 0.05))) { return @(120, 80, 40, 255) }
                return @(0, 0, 0, 0)
            }
            'crystal' { # 召唤物：紫色菱形水晶
                if (([math]::Abs($dx) + [math]::Abs($dy)) -le ($s * 0.42)) {
                    if (([math]::Abs($dx) + [math]::Abs($dy)) -le ($s * 0.16)) { return @(235, 210, 255, 255) }
                    return @(150, 80, 220, 255)
                }
                return @(0, 0, 0, 0)
            }
            default {   # 材料：灰色矿石块
                if (($x -ge ($cx - [math]::Floor($s * 0.34))) -and ($x -le ($cx + [math]::Floor($s * 0.34))) -and
                    ($y -ge ($cy - [math]::Floor($s * 0.30))) -and ($y -le ($cy + [math]::Floor($s * 0.30)))) {
                    if ((($x + $y) % 7) -eq 0) { return @(200, 205, 215, 255) }
                    return @(120, 126, 138, 255)
                }
                return @(0, 0, 0, 0)
            }
        }
    }
} else {
    # 四色棋盘 + 洋红边框 + 橙色对角线：通用占位图（明显不是游戏美术）。
    $png = New-Png -Width $Size -Height $Size -Pixel {
        param($x, $y)
        $cell = [int]([math]::Floor($x / 4) + [math]::Floor($y / 4)) % 2
        if ($x -lt 2 -or $y -lt 2 -or $x -ge $Size - 2 -or $y -ge $Size - 2) { return @(255, 0, 200, 255) }
        if ($x -eq $y -or $x -eq ($y + 1)) { return @(255, 140, 0, 255) }
        if ($cell -eq 0) { return @(42, 161, 152, 255) }
        return @(11, 27, 43, 255)
    }
}

if ($Hex) {
    ($png | ForEach-Object { $_.ToString('X2') }) -join ''
    return
}

$full = [System.IO.Path]::GetFullPath($Out)
$dir = [System.IO.Path]::GetDirectoryName($full)
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
[System.IO.File]::WriteAllBytes($full, $png)
Write-Host ("[贴图] {0} ← {1} 字节（{2}×{2} RGBA）" -f $Out, $png.Length, $Size)
