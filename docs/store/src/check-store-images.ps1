# 스토어·런처 이미지 규격 검사. 저장소 루트에서 실행:
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File docs/store/src/check-store-images.ps1
# 위반마다 "FAIL <path> <이유>", 전부 통과면 "OK n files". 위반 시 exit 1.
Add-Type -AssemblyName System.Drawing
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')

# alpha: 'any' | 'none' | 'channel'(알파 채널만 있으면 됨) | 'required'(채널 + 투명 모서리)
$rules = @(
    @{ Path = 'docs/store/icon-512.png';                  W = 512;  H = 512;  Alpha = 'channel' },   # Play: 32비트 PNG
    @{ Path = 'docs/store/feature-1024x500.png';          W = 1024; H = 500;  Alpha = 'none' },
    @{ Path = 'Assets/_Project/Art/AppIcon/icon-bg.png';     W = 432;  H = 432;  Alpha = 'any' },
    @{ Path = 'Assets/_Project/Art/AppIcon/icon-fg.png';     W = 432;  H = 432;  Alpha = 'required'; SafeRadius = 132 },
    @{ Path = 'Assets/_Project/Art/AppIcon/icon-legacy.png'; W = 432;  H = 432;  Alpha = 'any' }
)
$screensDir = Join-Path $root 'docs/store/screens'
if (Test-Path $screensDir) {
    Get-ChildItem $screensDir -Filter *.png | ForEach-Object {
        $rules += @{ Path = "docs/store/screens/$($_.Name)"; W = 1920; H = 1080; Alpha = 'none' }
    }
}

$fails = @()
foreach ($r in $rules) {
    $full = Join-Path $root $r.Path
    if (-not (Test-Path $full)) { $fails += "FAIL $($r.Path) missing"; continue }
    $img = New-Object System.Drawing.Bitmap $full
    try {
        if ($img.Width -ne $r.W -or $img.Height -ne $r.H) {
            $fails += "FAIL $($r.Path) size $($img.Width)x$($img.Height), want $($r.W)x$($r.H)"
        }
        $hasAlpha = [System.Drawing.Image]::IsAlphaPixelFormat($img.PixelFormat)
        if ($r.Alpha -eq 'channel' -and -not $hasAlpha) { $fails += "FAIL $($r.Path) needs 32-bit alpha channel ($($img.PixelFormat))" }
        if ($r.Alpha -eq 'none' -and $hasAlpha) { $fails += "FAIL $($r.Path) has alpha channel ($($img.PixelFormat))" }
        if ($r.Alpha -eq 'required') {
            if (-not $hasAlpha) { $fails += "FAIL $($r.Path) needs alpha channel ($($img.PixelFormat))" }
            else {
                $corners = @($img.GetPixel(0, 0), $img.GetPixel($img.Width - 1, 0),
                             $img.GetPixel(0, $img.Height - 1), $img.GetPixel($img.Width - 1, $img.Height - 1))
                if ($corners | Where-Object { $_.A -ne 0 }) { $fails += "FAIL $($r.Path) corners not transparent" }
            }
        }
        if ($r.SafeRadius -and $hasAlpha) {
            $cx = ($img.Width - 1) / 2.0; $cy = ($img.Height - 1) / 2.0; $max = 0.0
            for ($y = 0; $y -lt $img.Height; $y += 2) {
                for ($x = 0; $x -lt $img.Width; $x += 2) {
                    if ($img.GetPixel($x, $y).A -gt 0) {
                        $d = [Math]::Sqrt(($x - $cx) * ($x - $cx) + ($y - $cy) * ($y - $cy))
                        if ($d -gt $max) { $max = $d }
                    }
                }
            }
            if ($max -gt $r.SafeRadius) { $fails += "FAIL $($r.Path) outside safe zone (max r=$([int]$max), limit $($r.SafeRadius))" }
        }
    } finally { $img.Dispose() }
}

if ($fails.Count -gt 0) { $fails | ForEach-Object { Write-Output $_ }; exit 1 }
Write-Output "OK $($rules.Count) files"
exit 0
