# Tichu 게임 서버 운영 안내서 (M2b)

- 주소: `wss://play.<도메인>/ws` · 서버: Lightsail 서울 `<인스턴스 이름>` · 고정 IP `<고정 IP>`
- 구성: Caddy(443, 인증서 자동) → `127.0.0.1:7777` Tichu.Server(systemd `tichu-server`)
- 스펙: `docs/superpowers/specs/2026-10-07-mp-m2b-cloud-deploy-design.md`

## ⚠️ 먼저 알아둘 것
- **배포·재시작하면 진행 중인 판이 끊긴다.** 판이 없을 때만 한다(재접속은 M5).
- 서버는 **랜덤 매칭으로 여러 방**을 돌린다(`--max-rooms`, 기본 2). 접속하면 대기열에 들어가고(`Queued`), 4명이 모이면 바로 방이 열리며, 아니면 가장 오래 기다린 사람이 `--fill-after`초(기본 30) 기다린 뒤 대기 중인 사람들로 방을 열고 빈자리는 AI(Hard)가 채운다. 방이 상한이면 "서버가 붐빕니다 · 대기 순번 N"(Busy)으로 기다린다. 매치 중 사람이 나가면 그 방만 끝난다.
- 테스터는 접속 화면에 **전체 주소 `wss://play.<도메인>`** 을 입력한다. `play.<도메인>`만 쓰면 같은 Wi-Fi 개발용(`ws://…:7777`)으로 해석돼 접속이 안 된다.
- SSH 키: `C:\Users\user\.keys\<키 파일>` — **저장소에 넣지 않는다. 업로드 키와 함께 백업.**
- 도메인 자동 갱신이 켜져 있어야 한다. 만료되면 출시 앱 전체가 접속 불가.
- 스크립트는 PowerShell 7(`pwsh`)로 실행한다(UTF-8 한글 출력).

## 환경변수 (PC, User 범위 — 한 번만)
```powershell
[Environment]::SetEnvironmentVariable('TICHU_SERVER_HOST', 'play.<도메인>', 'User')
[Environment]::SetEnvironmentVariable('TICHU_SERVER_SSH_KEY', 'C:\Users\user\.keys\<키 파일>', 'User')
```

## 자주 쓰는 명령
| 하려는 일 | 명령 |
|---|---|
| 배포 | `pwsh tools/deploy-server.ps1` |
| 접속 확인 | `pwsh tools/smoke-server.ps1 -Url wss://play.<도메인>/ws` |
| 버전 거절 확인 | `pwsh tools/smoke-server.ps1 -Url wss://play.<도메인>/ws -NoVersion -ExpectReject` |
| SSH 접속 | `ssh -i $env:TICHU_SERVER_SSH_KEY ubuntu@play.<도메인>` |
| 로그(최근) | `sudo journalctl -u tichu-server -n 100 --no-pager` |
| 로그(실시간) | `sudo journalctl -u tichu-server -f` |
| 판 진행 중인지 | `sudo journalctl -u tichu-server --since today --no-pager \| grep -E "방 [0-9]+ (시작\|종료)"` → `방 N 시작`에 이후 `방 N 종료`가 짝지어 없으면 그 방은 진행 중 |
| 재시작 | `sudo systemctl restart tichu-server` |
| 게임 인자 변경(목표 점수 등) | `sudo nano /etc/tichu/server.env` → `sudo systemctl restart tichu-server` |
| 지금 릴리스 | `readlink /opt/tichu/current` |
| 직전 릴리스로 수동 롤백 | `ls /opt/tichu/releases` → `sudo ln -sfn /opt/tichu/releases/<직전> /opt/tichu/current && sudo systemctl restart tichu-server` |
| Caddy 로그(인증서) | `sudo journalctl -u caddy -n 100 --no-pager` |

## M3 업그레이드: server.env 마이그레이션
M3 이전에 세팅한 호스트의 `/etc/tichu/server.env`는 `setup-server.sh`가 보존하므로 구 좌석 인자(`seats 0,2`)가 남아 있다. 새 바이너리는 이 인자를 모르는 인자로 보고 종료(exit 2)해 `install-release.sh`가 롤백한다(안전하지만 배포 실패). **배포 전에** `sudo nano /etc/tichu/server.env`에서 좌석 인자(`seats …`)를 지우고 `--fill-after 30 --max-rooms 2`를 추가한다(기본값: `TICHU_GAME_ARGS=--difficulty Hard --target 1000 --fill-after 30 --max-rooms 2`).

## 스모크 스크립트 주의
`smoke-server.ps1`은 첫 메시지로 `Queued`를 기대한다. 재시작 직후(대기열이 빈 상태)이거나 대기 중인 사람이 없을 때만 돌린다. 붐비는 서버에서는 스모크 접속이 실제 방에 묶여(첫 메시지 `Lobby` → FAIL) 곧바로 끊기면서 진짜 플레이어의 방을 폴트시킬 수 있다.

## 서버를 처음부터 다시 만들 때
1. Lightsail 서울 · Ubuntu 24.04 · 1GB 인스턴스 → 고정 IP 연결 → 방화벽 22/80/443 → DNS `play` A 레코드를 새 IP로.
2. `scp -i <키> -r deploy/server ubuntu@<IP>:/tmp/tichu-setup` → `ssh -i <키> ubuntu@<IP> "sudo bash /tmp/tichu-setup/setup-server.sh play.<도메인>"`
3. `pwsh tools/deploy-server.ps1`
서버에는 보존할 상태가 없다(매치는 메모리, 영속 데이터 없음).

## 비용
인스턴스 월 약 $7(1GB) + 도메인 연 $<금액>.
