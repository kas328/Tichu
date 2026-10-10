<#
.SYNOPSIS
    서버에 WebSocket 으로 붙어 첫 메시지를 확인한다(M2b 완료 기준 확인용).
.DESCRIPTION
    기본: 현재 프로토콜 버전(core/src/Tichu.Net/ProtocolVersion.cs 에서 읽음)으로 접속해 Queued(대기열 상태)를 기대한다.
    -ExpectReject: 업데이트 안내(SessionFaulted "앱을 업데이트해 주세요…")를 기대한다.
    -NoVersion: v 쿼리를 붙이지 않는다(구 APK 흉내).
    대기열에 잠깐 들어갔다 바로 나온다(충원 대기보다 짧아 방이 생기지 않는다).
    종료 코드: 0 = 기대대로, 1 = 아님.
#>
param(
    [Parameter(Mandatory)][string]$Url,
    [int]$Version = 0,
    [switch]$NoVersion,
    [switch]$ExpectReject
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

if ($Version -eq 0) {
    $m = Select-String -Path (Join-Path $root "core/src/Tichu.Net/ProtocolVersion.cs") -Pattern 'const int Current = (\d+)'
    $Version = [int]$m.Matches[0].Groups[1].Value
}
$uri = if ($NoVersion) { [Uri]$Url } else { [Uri]("$Url" + "?v=$Version&id=$([guid]::NewGuid().ToString('N'))") }

$ws = [System.Net.WebSockets.ClientWebSocket]::new()
$cts = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(10))
$code = 1
try {
    $null = $ws.ConnectAsync($uri, $cts.Token).GetAwaiter().GetResult()
    $buf = [byte[]]::new(65536)
    $r = $ws.ReceiveAsync([ArraySegment[byte]]::new($buf), $cts.Token).GetAwaiter().GetResult()
    if ($r.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Close) {
        Write-Host "서버가 메시지 없이 닫음: $($ws.CloseStatus) $($ws.CloseStatusDescription)"
    } else {
        $msg = [Text.Encoding]::UTF8.GetString($buf, 0, $r.Count) | ConvertFrom-Json
        Write-Host "받음: Kind=$($msg.Kind) $($msg.Reason)"
        $ok = if ($ExpectReject) { $msg.Kind -eq 'SessionFaulted' -and $msg.Reason -like '앱을 업데이트해 주세요*' } else { $msg.Kind -eq 'Queued' }
        if ($ok) { $code = 0 }
    }
} catch {
    Write-Host "접속 실패: $($_.Exception.GetBaseException().Message)"
} finally {
    if ($ws.State -eq [System.Net.WebSockets.WebSocketState]::Open) {
        try { $null = $ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "smoke", [Threading.CancellationToken]::None).GetAwaiter().GetResult() } catch { }
    }
    $ws.Dispose()
}
Write-Host ($(if ($code -eq 0) { "OK" } else { "FAIL" }) + " — $uri")
exit $code
