param([string]$OutIco, [string]$OutSheet)
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function P([double]$x, [double]$y) { New-Object System.Drawing.PointF([single]$x, [single]$y) }
function C([int]$r, [int]$g, [int]$b, [int]$a = 255) { [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }
function Brush($c) { New-Object System.Drawing.SolidBrush($c) }
function LBrush($p1, $p2, $c1, $c2) { New-Object System.Drawing.Drawing2D.LinearGradientBrush($p1, $p2, $c1, $c2) }
function MkPen($c, [double]$w, [bool]$dash = $false) {
    $p = New-Object System.Drawing.Pen($c, $w); $p.LineJoin = 'Round'; $p.StartCap = 'Round'; $p.EndCap = 'Round'
    if ($dash) { $p.DashPattern = [single[]]@(2.2, 2.0) }
    $p
}
function Lerp($a, $b, [double]$t) { P ($a.X + ($b.X - $a.X) * $t) ($a.Y + ($b.Y - $a.Y) * $t) }
function Shift($pts, [double]$dx, [double]$dy) { @($pts | ForEach-Object { P ($_.X + $dx) ($_.Y + $dy) }) }
function Arr($pts) { [System.Drawing.PointF[]]$pts }

$script:GX = $null
$script:SZ = 256

function FillQ($pts, $brush) { $script:GX.FillPolygon($brush, (Arr $pts)) }
function LineP($pen, $pts) { $script:GX.DrawLines($pen, (Arr $pts)) }

# 사다리꼴 단면 [좌하, 우하, 우상, 좌상]
function Sec([double]$x, [double]$y, [double]$wb, [double]$wt, [double]$h) {
    $o = ($wb - $wt) / 2
    @((P $x $y), (P ($x + $wb) $y), (P ($x + $wb - $o) ($y - $h)), (P ($x + $o) ($y - $h)))
}

function Draw-Icon {
    $small = $script:SZ -le 32
    $tiny = $script:SZ -le 24

    # ---- 배경 ----
    $r = 50; $x0 = 8; $y0 = 8; $w = 240; $h = 240
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x0, $y0, $r, $r, 180, 90); $path.AddArc($x0 + $w - $r, $y0, $r, $r, 270, 90)
    $path.AddArc($x0 + $w - $r, $y0 + $h - $r, $r, $r, 0, 90); $path.AddArc($x0, $y0 + $h - $r, $r, $r, 90, 90)
    $path.CloseFigure()
    $script:GX.FillPath((LBrush (P 0 8) (P 256 248) (C 40 56 86) (C 14 20 34)), $path)
    # 위쪽 은은한 빛
    $pg = [System.Drawing.Drawing2D.PathGradientBrush]::new((Arr @((P -40 -60), (P 296 -60), (P 296 150), (P -40 150))))
    $pg.CenterColor = (C 90 150 220 70); $pg.SurroundColors = [System.Drawing.Color[]]@((C 90 150 220 0))
    $pg.CenterPoint = (P 170 40)
    $script:GX.SetClip($path)
    $script:GX.FillRectangle($pg, -40, -60, 336, 210)
    $script:GX.ResetClip()
    $script:GX.DrawPath((MkPen (C 255 255 255 34) 2), $path)

    # ---- 형상: 성토(사다리꼴) + 포장층, 앞-왼쪽 아래에서 뒤-오른쪽 위로 뻗음 ----
    $F = Sec 22 208 112 62 36
    $D = @(94, -90)                     # 뒤로 뻗는 방향
    $split = 0.5                         # 여기까지 솔리드, 그 뒤는 반투명
    $M = Shift $F ($D[0] * $split) ($D[1] * $split)
    $B = Shift $F $D[0] $D[1]
    # 포장층 단면: 성토 윗면 위에 얹힘
    function PavSec($S) { $a = Lerp $S[3] $S[2] 0.07; $b = Lerp $S[3] $S[2] 0.93; @($a, $b, (P ($b.X - 3) ($b.Y - 11)), (P ($a.X + 3) ($a.Y - 11))) }
    $pF = PavSec $F; $pM = PavSec $M; $pB = PavSec $B

    # ---- 반투명(설계 모델) 부분 ----
    $glass = Brush (C 190 220 255 30)
    FillQ @($M[1], $B[1], $B[2], $M[2]) $glass
    FillQ @($M[3], $M[2], $B[2], $B[3]) $glass
    FillQ @($pM[3], $pM[2], $pB[2], $pB[3]) (Brush (C 190 220 255 26))
    FillQ $B (Brush (C 190 220 255 22))
    $edge = MkPen (C 214 234 255 225) ($(if ($small) { 5 } else { 2.6 }))
    $hid = MkPen (C 214 234 255 95) 2 $true
    if (-not $small) {
        # 안 보이는 모서리(점선)
        LineP $hid @($M[0], $B[0], $B[1]); LineP $hid @($B[0], $B[3])
        # 코리더 측점 단면선(리브)
        $rib = MkPen (C 170 210 255 105) 1.6
        foreach ($t in 0.2, 0.4, 0.6, 0.8) {
            $q = @($M | ForEach-Object { $_ }) ; $q = Shift $M ($D[0] * (1 - $split) * $t) ($D[1] * (1 - $split) * $t)
            $pq = PavSec $q
            LineP $rib @($q[1], $q[2], $pq[1], $pq[2], $pq[3], $q[3])
        }
    }
    LineP $edge @($M[1], $B[1], $B[2], $B[3], $M[3])
    LineP $edge @($M[2], $B[2])
    LineP $edge @($pM[2], $pB[2], $pB[3], $pM[3])
    LineP $edge @($pB[1], $pB[2]); LineP $edge @($pB[0], $pB[3])

    # ---- 그림자 ----
    foreach ($i in 0..5) {
        $o = 4 + $i * 2.2
        FillQ (Shift @($F[0], $F[1], $M[1], $M[0]) ($o * 0.4) $o) (Brush (C 0 0 0 16))
    }

    # ---- 솔리드(실제 모델) 부분 ----
    # 오른쪽 경사면
    FillQ @($F[1], $M[1], $M[2], $F[2]) (LBrush $F[1] $M[2] (C 178 92 14) (C 128 62 8))
    # 성토 윗면(어깨)
    FillQ @($F[3], $F[2], $M[2], $M[3]) (LBrush $F[3] $M[2] (C 255 204 120) (C 236 164 66))
    # 앞 단면
    FillQ $F (LBrush (P 0 ($F[3].Y)) (P 0 ($F[0].Y)) (C 252 172 64) (C 220 120 22))
    # 포장층
    FillQ @($pF[1], $pM[1], $pM[2], $pF[2]) (Brush (C 52 58 68))
    FillQ @($pF[3], $pF[2], $pM[2], $pM[3]) (LBrush $pF[3] $pM[2] (C 104 112 126) (C 78 86 100))
    FillQ $pF (Brush (C 66 72 84))
    if (-not $tiny) {
        $cl = MkPen (C 255 255 255 200) ($(if ($small) { 4 } else { 2.4 })) $true
        $a = Lerp $pF[3] $pF[2] 0.5; $b = Lerp $pM[3] $pM[2] 0.5
        LineP $cl @($a, $b)
    }
    # 밝은 모서리(하이라이트)와 얇은 윤곽
    $hl = MkPen (C 255 236 200 190) ($(if ($small) { 3.5 } else { 2 }))
    LineP $hl @($F[0], $F[3]); LineP $hl @($F[2], $F[1])
    $ol = MkPen (C 20 14 8 120) ($(if ($small) { 3 } else { 1.6 }))
    $script:GX.DrawPolygon($ol, (Arr $F))
    LineP $ol @($F[1], $M[1]); LineP $ol @($F[2], $M[2])
    $script:GX.DrawPolygon($ol, (Arr $pF))

    # ---- 경계: 실체화되는 빛 ----
    $glowPts = @($M[1], $M[2], $pM[1], $pM[2], $pM[3], $M[3])
    foreach ($gw in @(@(16, 26), @(10, 48), @(5.5, 95))) { $gp = MkPen (C 110 225 255 $gw[1]) $gw[0]; $gp.StartCap = 'Flat'; $gp.EndCap = 'Flat'; LineP $gp $glowPts }
    LineP (MkPen (C 225 250 255 255) ($(if ($small) { 4 } else { 2.4 }))) $glowPts
    if (-not $small) {
        foreach ($sp in @(@(190, 98, 4.6), @(156, 110, 3), @(206, 132, 2.8))) {
            $x = $sp[0]; $y = $sp[1]; $s = $sp[2]
            $script:GX.FillPolygon((Brush (C 220 248 255 230)), (Arr @((P $x ($y - 3 * $s)), (P ($x + $s * 0.7) ($y - $s * 0.7)), (P ($x + 3 * $s) $y), (P ($x + $s * 0.7) ($y + $s * 0.7)), (P $x ($y + 3 * $s)), (P ($x - $s * 0.7) ($y + $s * 0.7)), (P ($x - 3 * $s) $y), (P ($x - $s * 0.7) ($y - $s * 0.7)))))
        }
    }
}

function Render([int]$S) {
    $bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $script:GX = [System.Drawing.Graphics]::FromImage($bmp)
    $script:GX.SmoothingMode = 'AntiAlias'; $script:GX.PixelOffsetMode = 'HighQuality'; $script:GX.CompositingQuality = 'HighQuality'
    $script:GX.Clear([System.Drawing.Color]::Transparent)
    $script:SZ = $S
    $k = $S / 256.0; $script:GX.ScaleTransform([single]$k, [single]$k)
    Draw-Icon
    $script:GX.Dispose()
    return $bmp
}

# ICO
$frames = @()
foreach ($s in 256, 64, 48, 32, 24, 16) {
    $b = Render $s; $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
    $frames += , @($s, $ms.ToArray())
}
$fs = [System.IO.File]::Create($OutIco); $bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$frames.Count)
$off = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $d = if ($f[0] -ge 256) { 0 } else { $f[0] }
    $bw.Write([byte]$d); $bw.Write([byte]$d); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$f[1].Length); $bw.Write([UInt32]$off)
    $off += $f[1].Length
}
foreach ($f in $frames) { $bw.Write([byte[]]$f[1]) }
$bw.Close()

# 미리보기: 큰 그림 + 크기별(밝은/어두운 배경)
$sheet = New-Object System.Drawing.Bitmap(860, 300)
$sg = [System.Drawing.Graphics]::FromImage($sheet)
$sg.Clear([System.Drawing.Color]::White)
$sg.FillRectangle((Brush (C 32 32 32)), 560, 0, 300, 300)
$big = Render 256; $sg.DrawImage($big, 22, 22, 256, 256); $big.Dispose()
foreach ($side in 0, 1) {
    $x = 288; if ($side -eq 1) { $x = 578 }
    foreach ($s in 96, 48, 32, 24, 16) {
        $b = Render $s; $sg.DrawImage($b, $x, (200 - $s), $s, $s); $b.Dispose(); $x += $s + 12
    }
}
$sg.Dispose()
$sheet.Save($OutSheet, [System.Drawing.Imaging.ImageFormat]::Png)
"ok"
