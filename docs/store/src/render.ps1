# HTML 원본 → PNG. 저장소 루트에서 실행:
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File docs/store/src/render.ps1
Add-Type -AssemblyName System.Drawing
$edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
$src  = $PSScriptRoot
$root = Resolve-Path (Join-Path $src '..\..\..')

$jobs = @(
    @{ Html = 'icon.html';           Out = 'docs/store/icon-512.png';                  W = 512;  H = 512 },
    @{ Html = 'feature.html';        Out = 'docs/store/feature-1024x500.png';          W = 1024; H = 500; NoAlpha = $true },
    @{ Html = 'appicon-bg.html';     Out = 'Assets/_Project/Art/AppIcon/icon-bg.png';     W = 432; H = 432 },
    @{ Html = 'appicon-fg.html';     Out = 'Assets/_Project/Art/AppIcon/icon-fg.png';     W = 432; H = 432 },
    @{ Html = 'appicon-legacy.html'; Out = 'Assets/_Project/Art/AppIcon/icon-legacy.png'; W = 432; H = 432 }
)

foreach ($j in $jobs) {
    $out = Join-Path $root $j.Out
    New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
    $url = 'file:///' + ((Join-Path $src $j.Html) -replace '\\', '/')
    & $edge --headless=new --disable-gpu --hide-scrollbars --default-background-color=00000000 `
        "--window-size=$($j.W),$($j.H)" "--screenshot=$out" $url 2>$null | Out-Null
    # Play 규격: 알파 없는 24비트로 다시 저장
    if ($j.NoAlpha) {
        $img = New-Object System.Drawing.Bitmap $out
        $rgb = New-Object System.Drawing.Bitmap $img.Width, $img.Height, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
        $g = [System.Drawing.Graphics]::FromImage($rgb); $g.DrawImage($img, 0, 0, $img.Width, $img.Height); $g.Dispose()
        $img.Dispose()
        $rgb.Save($out, [System.Drawing.Imaging.ImageFormat]::Png); $rgb.Dispose()
    }
    Write-Output "rendered $($j.Out)"
}
