<#
.SYNOPSIS
    Tichu.Server 를 Lightsail 에 배포한다(M2b 스펙 §1.3 · §4).
.DESCRIPTION
    linux-x64 self-contained publish → tgz → 업로드 → 서버의 install-release.sh 가 바꿔치기·재시작·기동 확인
    (실패 시 직전 릴리스로 되돌림) → PC 에서 smoke-server.ps1 로 wss 접속 확인.
    ⚠️ 재시작하면 진행 중인 판이 끊긴다. 판이 없을 때만 실행한다.
    환경변수(User 범위): TICHU_SERVER_HOST(예: play.example.com), TICHU_SERVER_SSH_KEY(키 파일 경로).
.PARAMETER Yes
    확인 질문을 건너뛴다(사용자가 이미 대화에서 승인했을 때만).
#>
param([switch]$Yes)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

function Get-Setting([string]$name) {
    $v = [Environment]::GetEnvironmentVariable($name, 'Process')
    # 이미 떠 있던 셸은 나중에 등록한 User 변수를 상속받지 못한다 — 레지스트리에서 직접 읽는다
    if (-not $v) { $v = [Environment]::GetEnvironmentVariable($name, 'User') }
    if (-not $v) { throw "환경변수 $name 이 없습니다 (docs/ops/server-runbook.md 참고)" }
    return $v
}
$hostName = Get-Setting 'TICHU_SERVER_HOST'
$key = Get-Setting 'TICHU_SERVER_SSH_KEY'
if (-not (Test-Path $key)) { throw "SSH 키 파일이 없습니다: $key" }

if (-not $Yes) {
    $answer = Read-Host "배포하면 진행 중인 판이 끊깁니다. $hostName 에 배포할까요? (y/N)"
    if ($answer -ne 'y') { Write-Host "취소했습니다."; exit 1 }
}

$work = Join-Path ([IO.Path]::GetTempPath()) "tichu-deploy"
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
$publish = Join-Path $work "publish"
dotnet publish (Join-Path $root "core/src/Tichu.Server/Tichu.Server.csproj") -c Release -r linux-x64 --self-contained true -o $publish --nologo
if ($LASTEXITCODE -ne 0) { throw "publish 실패" }

$name = "tichu-server-" + (Get-Date -Format "yyyyMMdd-HHmmss")
$tgz = Join-Path $work "$name.tgz"
tar -czf $tgz -C $publish .
if ($LASTEXITCODE -ne 0) { throw "tar 실패" }

$ssh = @("-i", $key, "-o", "StrictHostKeyChecking=accept-new")
$target = "ubuntu@$hostName"
scp @ssh $tgz "${target}:/tmp/"
if ($LASTEXITCODE -ne 0) { throw "업로드 실패" }
ssh @ssh $target "sudo /opt/tichu/bin/install-release.sh /tmp/$name.tgz"
if ($LASTEXITCODE -ne 0) { throw "서버 설치 실패 — 위 로그 확인. 직전 릴리스로 되돌렸는지: ssh ... readlink /opt/tichu/current" }

& (Join-Path $PSScriptRoot "smoke-server.ps1") -Url "wss://$hostName/ws"
if ($LASTEXITCODE -ne 0) { throw "배포 후 접속 확인 실패" }
Write-Host "배포 완료: $name"
