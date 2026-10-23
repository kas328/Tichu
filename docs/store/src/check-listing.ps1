# 등록정보 글자 수·필수 문구 검사. 저장소 루트에서 실행:
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File docs/store/src/check-listing.ps1
# listing.html 의 <pre id="title|short|full"> 내용을 센다. 위반 시 exit 1.
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
$path = Join-Path $root 'docs/store/listing.html'
if (-not (Test-Path $path)) { Write-Output "FAIL docs/store/listing.html missing"; exit 1 }
$html = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)

$disclaimer = '본 앱은 Tichu 의 저작권자·상표권자 및 공식 라이선스 앱과 관련이 없는 독립 제작물입니다.'
$limits = [ordered]@{ title = 30; short = 80; full = 4000; online = 4000 }
$fails = @()
$fields = @{}
foreach ($id in $limits.Keys) {
    $m = [regex]::Match($html, '<pre id="' + $id + '"[^>]*>([\s\S]*?)</pre>')
    if (-not $m.Success) { $fails += "FAIL $id missing"; continue }
    $text = [System.Net.WebUtility]::HtmlDecode($m.Groups[1].Value).Replace("`r`n", "`n").Trim()   # 줄바꿈은 한 글자
    $fields[$id] = $text
    Write-Output ("{0} {1}/{2}" -f $id, $text.Length, $limits[$id])
    if ($text.Length -gt $limits[$id]) { $fails += "FAIL $id too long ($($text.Length) > $($limits[$id]))" }
    foreach ($bad in @('최강', '최고의')) { if ($text.Contains($bad)) { $fails += "FAIL $id contains '$bad'" } }
}
if ($fields.title -and $fields.title -ne '용과 봉황: 팀 카드게임') { $fails += "FAIL title must be exactly '용과 봉황: 팀 카드게임'" }
if ($fields.full -and -not $fields.full.Contains($disclaimer)) { $fails += "FAIL full lacks disclaimer" }
# 온라인 단락은 별도 블록(M2b 배포 뒤에만 붙임). 합쳐도 4000자 이내여야 한다.
if ($fields.full -and $fields.full.Contains('온라인 대전')) { $fails += "FAIL full must not contain the online section (keep it in <pre id=online>)" }
if ($fields.full -and $fields.online) {
    $joined = $fields.full.Length + 2 + $fields.online.Length
    Write-Output "full+online $joined/4000"
    if ($joined -gt 4000) { $fails += "FAIL full+online too long ($joined > 4000)" }
}

if ($fails.Count -gt 0) { $fails | ForEach-Object { Write-Output $_ }; exit 1 }
Write-Output 'OK'
exit 0
