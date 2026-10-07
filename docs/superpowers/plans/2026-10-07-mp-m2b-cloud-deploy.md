# 멀티플레이 M2b — 클라우드 배포 · `wss://` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** M2 서버를 AWS Lightsail 서울에 올려, 폰이 모바일 데이터로 `wss://play.<도메인>/ws`에 접속해 매치를 끝까지 둔다.

**Architecture:** 게임 코드는 거의 그대로 둔다. 손대는 코드는 세 가지다. ① `ServerAddress`의 `wss` 포트 버그 수정 ② 서버 `--host` 옵션 ③ 프로토콜 버전 확인(`?v=1`, 불일치 시 `SessionFaulted` 후 닫기). 서버 앞에는 Caddy를 두어 TLS를 끝내고 루프백 7777로 넘긴다. 프로세스는 systemd가 관리한다. 배포는 PC의 `tools/deploy-server.ps1`이 linux-x64 self-contained 결과물을 올리고, 서버 쪽 `install-release.sh`가 바꿔치기·재시작·기동 확인을 하며 실패하면 되돌린다. AWS 콘솔 작업은 Claude in Chrome으로 진행한다.

**Tech Stack:** C# (core netstandard2.1 · LangVersion 9 / 서버 net10.0) · ASP.NET Core 최소 API · `System.Net.WebSockets` · NUnit 4(dotnet) / NUnit 3.5(Unity) · Unity 6000.3.17f1 · Ubuntu 24.04 LTS · Caddy(Ubuntu 패키지) · systemd · Windows OpenSSH(`ssh.exe`·`scp.exe`) · PowerShell 7 · Claude in Chrome

**Spec:** `docs/superpowers/specs/2026-10-07-mp-m2b-cloud-deploy-design.md`

## Global Constraints

- 호스팅: **AWS Lightsail 서울(ap-northeast-2) · Ubuntu 24.04 LTS · 메모리 1GB 플랜 · 고정 IP.**
- 주소: **`wss://play.<도메인>/ws`**. 도메인은 AWS에서 구매하고 **"tichu"가 들어가지 않는 이름**으로 한다. 자동 갱신을 켠다.
- 방화벽: **TCP 22 · 80 · 443만.** 7777은 열지 않는다. 서버는 `--host 127.0.0.1 --port 7777`.
- 게임 인자 기본값: `--seats 0,2 --difficulty Hard --target 1000`. 서버의 `/etc/tichu/server.env`에 `TICHU_GAME_ARGS`로 둔다.
- 프로토콜: `ProtocolVersion.Current = 1`, 쿼리 키 `v`. 불일치 사유 문구는 정확히 **`앱을 업데이트해 주세요 (서버 프로토콜 v1)`**.
- **`MatchSession` · `RemoteAgent` · `GameDriver` · 매치 흐름 · 싱글플레이 동작은 바꾸지 않는다.**
- **정본은 `core/`.** `core/src/Tichu.Net`이나 `core/tests/Tichu.Core.Tests`를 고쳤으면 `pwsh tools/sync-core-to-unity.ps1`을 돌리고 Unity 사본까지(`.meta` 포함) 커밋한다. `-Check`로 드리프트가 없는지 확인한다.
- `Tichu.Core.Tests`는 Unity(NUnit 3.5)로 미러된다. `Assert.That` + `Is.*` + `Does.StartWith/Contain(string)`만 쓰고 NUnit 4 전용 API는 쓰지 않는다. 미러되는 테스트는 Newtonsoft 타입을 쓰지 않는다. **dotnet 그린 ≠ Unity 그린.**
- `Tichu.Server` · `Tichu.Server.Tests`는 net10.0이고 미러하지 않는다.
- **비밀 정보:** SSH 키는 `C:\Users\user\.keys\`에만 둔다. 저장소에는 키·IP 외 접속 정보를 넣지 않는다. 스크립트는 환경변수 `TICHU_SERVER_HOST`(도메인)와 `TICHU_SERVER_SSH_KEY`(키 경로)를 User 범위에서 읽는다.
- **멈춤 규칙(사용자 지시):** 결제 화면에서는 멈추고 금액을 보여 준다. 로그인·2단계 인증·결제 정보 입력·구매 확정은 사용자가 한다. **실제 서버에 SSH 명령을 처음 실행하기 전**과 **배포(재시작) 전**에는 사용자 확인을 받는다.
- 커밋 메시지는 한국어로 쓰고 끝에 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`를 붙인다. 브랜치는 `feat/mp-m2b`.
- ⚠️ **Unity에서 `Tichu.Core.Tests`를 통째로 실행하지 않는다**(10만 판 시뮬레이터). Unity 검증은 `Tichu.Presentation.Tests` 어셈블리만 돌린다. 이 어셈블리를 돌려도 컴파일은 전 어셈블리가 되므로 미러 코드의 컴파일 오류도 잡힌다. 미러된 core 테스트는 `-testFilter`에 **네임스페이스를 포함한 전체 클래스명**을 줘서 따로 돌린다.
- **Unity 검증 절차**(Unity MCP 미연결 기준, **에디터를 닫은 상태**에서. `Get-Process Unity`가 비어 있어야 한다):
  ```powershell
  Remove-Item "$env:TEMP\r.xml","$env:TEMP\r.log" -ErrorAction SilentlyContinue
  $p = Start-Process -FilePath "E:\6000.3.17f1\Editor\Unity.exe" -PassThru -ArgumentList @("-batchmode","-runTests","-projectPath","C:\Users\user\Desktop\Project\Tichu","-testPlatform","EditMode","-assemblyNames","<어셈블리>","-testFilter","<필터>","-testResults","$env:TEMP\r.xml","-logFile","$env:TEMP\r.log")
  ```
  `$env:TEMP\r.xml`이 생길 때까지 폴링한다. `-Wait`는 쓰지 않는다(Android 플랫폼이면 ADB 서버 때문에 프로세스가 끝나지 않는다). 결과는 `([xml](Get-Content $env:TEMP\r.xml)).'test-run'`의 `result`·`total`·`passed`·`failed`로 읽는다. **`total`이 0이면 필터가 빗나간 것이므로 실패로 본다.** XML이 안 생기면 `r.log`에서 `error CS`를 찾는다. 끝나면 `taskkill /PID $p.Id /F`.

## Review Focus

1. **구 APK(버전 쿼리 없음)가 접속** — 무한 대기나 "연결할 수 없음"이 아니라 "앱을 업데이트해 주세요"를 보고 메뉴로 돌아가야 한다. 구 APK는 포트 버그 때문에 클라우드에 닿지 못하므로 로컬 서버로 확인한다 → Task 2 `Missing_wrong_or_garbage_version_gets_the_update_notice_and_is_closed`("" 케이스) + Task 7 Step 2.
2. **버전이 다른 접속이 좌석을 차지하거나 매치를 시작시킴** — `--seats 0,2`에서 구버전 기기 하나와 정상 기기 하나가 붙어도 방이 차면 안 된다 → Task 2 `Rejected_connection_does_not_take_a_seat`.
3. **배포가 실패해 서버가 죽은 채 남음** — 새 결과물이 기동하지 못하면 직전 릴리스로 되돌아가 서비스가 살아 있어야 한다 → Task 6 Step 6 롤백 훈련(깨진 릴리스 설치 → exit 1 · 이전 릴리스 복귀 · smoke 통과).
4. **테스터가 도메인만 입력(스킴 생략)** — `play.<도메인>`은 `ws://…:7777`로 해석돼 접속에 실패한다. 이건 버그가 아니라 개발용 화면의 규칙이다(출시 빌드는 M4에서 주소를 앱에 넣는다). 테스터 안내문에 전체 주소 `wss://play.<도메인>`을 적는다 → Task 1 테스트(`play.example.com` → `ws://…:7777/ws` 현행 유지 고정) + Task 7 안내문.
5. **진행 중인 판이 있는데 배포/스모크** — 재시작하면 판이 끊긴다. 판이 진행 중이면 스모크 접속은 "room full"로 닫힌다. 스크립트는 멈추지 말고 원인을 출력해야 한다 → Task 4 `deploy-server.ps1` 확인 질문 + `smoke-server.ps1` 닫힘 사유 출력과 10초 시간 제한, Task 4 Step 5 로컬 검증.

---

## File Structure

| 파일 | 책임 | 태스크 |
|---|---|---|
| `core/src/Tichu.Net/ServerAddress.cs` | 입력 주소 → WebSocket URI (wss 포트 수정) | 1 |
| `core/tests/Tichu.Core.Tests/ServerAddressTests.cs` | 주소 정규화 테스트(미러됨) | 1 |
| `core/src/Tichu.Net/ProtocolVersion.cs` (신규) | 버전 상수 · URI 부착 · 수락 판정 · 안내 문구 | 2 |
| `core/tests/Tichu.Core.Tests/ProtocolVersionTests.cs` (신규) | 위 순수 함수 테스트(미러됨) | 2 |
| `core/src/Tichu.Net/WebSocketClientTransport.cs` | 접속 시 버전 부착 | 2 |
| `core/src/Tichu.Server/WebSocketServerTransport.cs` | `RejectAsync`(사유 하나 보내고 닫기) | 2 |
| `core/src/Tichu.Server/ServerApp.cs` | `/ws`에서 좌석 배정 전 버전 확인 | 2 |
| `core/tests/Tichu.Server.Tests/ServerTransportTests.cs` | 버전 관문 테스트 + 원시 접속 헬퍼 | 2 |
| `core/tests/Tichu.Server.Tests/EndToEndTests.cs` | 원시 소켓 2곳 버전 부착 | 2 |
| `core/src/Tichu.Server/ServerOptions.cs` · `Program.cs` | `--host` | 3 |
| `core/tests/Tichu.Server.Tests/ServerOptionsTests.cs` | `--host` 테스트 | 3 |
| `deploy/server/tichu-server.service` · `Caddyfile.template` · `server.env.default` · `setup-server.sh` · `install-release.sh` | 서버 쪽 구성·세팅·릴리스 설치 | 4 |
| `tools/deploy-server.ps1` · `tools/smoke-server.ps1` | PC 쪽 배포 · 접속 확인 | 4 |
| `docs/ops/server-runbook.md` | 운영 안내서 | 4 (Task 6·8에서 실제 값 채움) |

---

### Task 1: `ServerAddress` — `wss` 포트 생략 시 443 유지

**Files:**
- Modify: `core/src/Tichu.Net/ServerAddress.cs`
- Test: `core/tests/Tichu.Core.Tests/ServerAddressTests.cs`
- Mirror: `Assets/_Project/Net/ServerAddress.cs` · `Assets/_Project/Tests/EditMode/ServerAddressTests.cs` (sync 스크립트가 만든다)

**Interfaces:**
- Consumes: 없음
- Produces: `ServerAddress.TryParse(string? input, out Uri? uri)` — 시그니처는 그대로다. `wss` + 포트 생략 → 443(URI 문자열에는 포트가 나타나지 않음).

- [ ] **Step 1: 테스트를 기대 동작으로 고친다(실패 확인용)**

`core/tests/Tichu.Core.Tests/ServerAddressTests.cs`의 `Accepted_forms_normalize` TestCase 목록을 아래로 바꾼다. 기존 `wss://example.com/ws` → `:7777` 케이스는 **버그를 고정하고 있던 것**이라 지운다.

```csharp
        [TestCase("192.168.0.5", "ws://192.168.0.5:7777/ws")]
        [TestCase("  192.168.0.5  ", "ws://192.168.0.5:7777/ws")]
        [TestCase("192.168.0.5:9000", "ws://192.168.0.5:9000/ws")]
        [TestCase("ws://192.168.0.5:7777/ws", "ws://192.168.0.5:7777/ws")]
        [TestCase("ws://my-pc.local", "ws://my-pc.local:7777/ws")]
        [TestCase("play.example.com", "ws://play.example.com:7777/ws")]          // 스킴 생략 = 같은 Wi-Fi 개발용 ws (현행 유지)
        [TestCase("wss://play.example.com", "wss://play.example.com/ws")]        // 포트 생략 → 443(TLS 프록시)
        [TestCase("wss://play.example.com/ws", "wss://play.example.com/ws")]
        [TestCase("wss://play.example.com:8443", "wss://play.example.com:8443/ws")]
        public void Accepted_forms_normalize(string input, string expected)
```

- [ ] **Step 2: 실패를 확인한다**

Run: `dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~ServerAddressTests" --nologo`
Expected: FAIL 2건 — `wss://play.example.com`과 `wss://play.example.com/ws`가 `wss://play.example.com:7777/ws`로 나온다. 나머지는 PASS.

- [ ] **Step 3: 최소 구현**

`core/src/Tichu.Net/ServerAddress.cs`에서 요약 주석과 포트 줄을 바꾼다.

```csharp
    /// <summary>
    /// 사람이 입력한 서버 주소를 WebSocket URI 로 정규화한다. 받는 형식:
    /// "192.168.0.5" · "192.168.0.5:9000" · "ws://host:7777/ws" · "wss://play.example.com"(앞뒤 공백 허용).
    /// 스킴 생략 → ws. 포트 생략 → ws 는 7777(같은 Wi-Fi 개발 서버), wss 는 443 그대로(TLS 프록시, M2b).
    /// 경로 생략 → /ws. http 등 다른 스킴은 거절.
    /// </summary>
```

```csharp
            var b = new UriBuilder(parsed);
            if (parsed.IsDefaultPort && parsed.Scheme == "ws") b.Port = DefaultPort;
            if (b.Path == "/" || b.Path.Length == 0) b.Path = "/ws";
```

- [ ] **Step 4: 통과 확인 + 음성 대조**

Run: 위와 같은 `dotnet test` 명령 → Expected: PASS(전 케이스).
음성 대조: `&& parsed.Scheme == "ws"`를 잠시 지우고 다시 실행 → **wss 두 케이스만** FAIL인지 본다 → 원복 → PASS.

- [ ] **Step 5: Unity 미러 동기 + Unity 검증**

Run: `pwsh tools/sync-core-to-unity.ps1` → `pwsh tools/sync-core-to-unity.ps1 -Check` (Expected: 드리프트 없음).
Unity 검증 절차(Global Constraints)를 두 번 돌린다.
- `-assemblyNames Tichu.Core.Tests -testFilter Tichu.Core.Tests.ServerAddressTests` → Expected: `failed=0`, `total=15`(받는 형식 9 + 거절 6 — dotnet 결과와 같은 수).
- `-assemblyNames Tichu.Presentation.Tests`(필터 없음) → Expected: `failed=0`, `total>0`.

- [ ] **Step 6: 커밋**

```bash
git add core/src/Tichu.Net/ServerAddress.cs core/tests/Tichu.Core.Tests/ServerAddressTests.cs Assets/_Project/Net/ServerAddress.cs Assets/_Project/Tests/EditMode/ServerAddressTests.cs
git commit -m "fix(net): wss 주소에서 포트를 생략하면 443을 유지한다

포트 생략 시 스킴과 상관없이 7777을 붙여 wss://play.<도메인> 이
:7777 로 바뀌어 TLS 프록시(443)에 닿지 못했다. ws 만 7777 기본값을 쓴다.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: 프로토콜 버전 확인

**Files:**
- Create: `core/src/Tichu.Net/ProtocolVersion.cs`
- Create: `core/tests/Tichu.Core.Tests/ProtocolVersionTests.cs`
- Modify: `core/src/Tichu.Net/WebSocketClientTransport.cs` (`ConnectAsync` 한 줄)
- Modify: `core/src/Tichu.Server/WebSocketServerTransport.cs` (`RejectAsync` 추가)
- Modify: `core/src/Tichu.Server/ServerApp.cs` (`/ws` 핸들러)
- Modify: `core/tests/Tichu.Server.Tests/ServerTransportTests.cs` (헬퍼 + 테스트 4개)
- Modify: `core/tests/Tichu.Server.Tests/EndToEndTests.cs` (원시 소켓 2곳)
- Mirror: `Assets/_Project/Net/ProtocolVersion.cs`(+`.meta`) · `Assets/_Project/Net/WebSocketClientTransport.cs` · `Assets/_Project/Tests/EditMode/ProtocolVersionTests.cs`(+`.meta`)

**Interfaces:**
- Consumes: `ServerEvent.SessionFaulted(string)`, `ProtocolJson.Serialize(ServerEvent)`, 기존 `Connection` 내부 클래스(`Enqueue`·`Complete`·`WriterTask`)
- Produces:
  - `public static class ProtocolVersion { const int Current = 1; const string QueryKey = "v"; static Uri Attach(Uri); static bool Accepts(string?); static string UpdateRequiredReason { get; } }` (namespace `Tichu.Net`)
  - `public Task WebSocketServerTransport.RejectAsync(WebSocket ws, string reason)`
  - 서버 로그 한 줄: `프로토콜 버전 불일치 — 거절 (받은 값: <값|없음>, 서버: 1)`

- [ ] **Step 1: 순수 함수 테스트를 쓴다**

`core/tests/Tichu.Core.Tests/ProtocolVersionTests.cs`:

```csharp
using System;
using System.Globalization;
using NUnit.Framework;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    public class ProtocolVersionTests
    {
        private static readonly string V = ProtocolVersion.Current.ToString(CultureInfo.InvariantCulture);

        [TestCase("wss://play.example.com/ws", "wss://play.example.com/ws?v=")]
        [TestCase("ws://192.168.0.5:7777/ws", "ws://192.168.0.5:7777/ws?v=")]
        [TestCase("wss://play.example.com/ws?room=7", "wss://play.example.com/ws?room=7&v=")]
        public void Attach_adds_the_current_version_as_query(string input, string expectedPrefix)
        {
            Assert.That(ProtocolVersion.Attach(new Uri(input)).ToString(), Is.EqualTo(expectedPrefix + V));
        }

        [Test]
        public void Current_version_is_accepted()
        {
            Assert.That(ProtocolVersion.Accepts(V), Is.True);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("0")]
        [TestCase("abc")]
        [TestCase("1.0")]
        [TestCase(" 1")]
        [TestCase("+1")]
        [TestCase("1,1")]   // ASP.NET 은 중복 쿼리를 쉼표로 잇는다
        public void Anything_else_is_rejected(string? value)
        {
            Assert.That(ProtocolVersion.Accepts(value), Is.False);
        }

        [Test]
        public void Next_version_is_rejected()
        {
            Assert.That(ProtocolVersion.Accepts((ProtocolVersion.Current + 1).ToString(CultureInfo.InvariantCulture)), Is.False);
        }

        [Test]
        public void Update_reason_names_the_server_version()
        {
            Assert.That(ProtocolVersion.UpdateRequiredReason, Is.EqualTo("앱을 업데이트해 주세요 (서버 프로토콜 v" + V + ")"));
        }
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

Run: `dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~ProtocolVersionTests" --nologo`
Expected: 빌드 실패 `CS0103: The name 'ProtocolVersion' does not exist`.

- [ ] **Step 3: `ProtocolVersion` 구현**

`core/src/Tichu.Net/ProtocolVersion.cs`:

```csharp
using System;
using System.Globalization;

namespace Tichu.Net
{
    /// <summary>
    /// 와이어 형식 버전(M2b 스펙 §5.3). 서버가 인터넷에 올라간 뒤로는 서버와 폰에 깔린 앱이 따로
    /// 업데이트되므로, 메시지 형식이 바뀌는 변경(M3 등)은 Current 를 올린다. 클라이언트는 접속 URI
    /// 쿼리 v 로 싣고, 서버는 좌석을 주기 전에 확인해 다르면 업데이트 안내(SessionFaulted)를 보내고 닫는다.
    /// </summary>
    public static class ProtocolVersion
    {
        public const int Current = 1;
        public const string QueryKey = "v";

        /// <summary>접속 URI 에 v=Current 를 붙인다. 이미 다른 쿼리가 있으면 &amp; 로 잇는다.</summary>
        public static Uri Attach(Uri uri)
        {
            bool hasQuery = uri.Query.Length > 1;   // "" 또는 "?a=b"
            string head = uri.GetLeftPart(hasQuery ? UriPartial.Query : UriPartial.Path);
            return new Uri(head + (hasQuery ? "&" : "?") + QueryKey + "=" + Current.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>서버: 받은 쿼리 값이 지금 버전인가. 없음·부호·공백·소수·중복·다른 값은 전부 false.</summary>
        public static bool Accepts(string? value) =>
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int v) && v == Current;

        public static string UpdateRequiredReason =>
            "앱을 업데이트해 주세요 (서버 프로토콜 v" + Current.ToString(CultureInfo.InvariantCulture) + ")";
    }
}
```

- [ ] **Step 4: 통과 확인 + 음성 대조**

Run: Step 2와 같은 명령 → Expected: PASS(전 케이스).
음성 대조: `Attach`가 `return uri;`만 하도록 잠시 바꿈 → `Attach_*` 3건만 FAIL → 원복.

- [ ] **Step 5: 서버 관문 테스트를 쓴다**

`core/tests/Tichu.Server.Tests/ServerTransportTests.cs`:

(a) 기존 `Connect` 헬퍼를 아래 두 개로 바꾼다. 기존 테스트는 모두 `Connect`를 쓰므로 의미가 그대로 유지된다(정상 버전으로 접속).

```csharp
        /// <summary>앱과 같은 정상 접속 — 현재 프로토콜 버전을 붙인다.</summary>
        private static Task<ClientWebSocket> Connect(Uri uri) => ConnectRaw(ProtocolVersion.Attach(uri));

        /// <summary>URI 를 그대로 쓴다(버전 관문 테스트용).</summary>
        private static async Task<ClientWebSocket> ConnectRaw(Uri uri)
        {
            var ws = new ClientWebSocket();
            await ws.ConnectAsync(uri, CancellationToken.None);
            return ws;
        }
```

(b) 클래스 끝에 테스트 4개를 추가한다.

```csharp
        [TestCase("")]
        [TestCase("?v=0")]
        [TestCase("?v=999")]
        [TestCase("?v=abc")]
        public async Task Missing_wrong_or_garbage_version_gets_the_update_notice_and_is_closed(string query)
        {
            await using var server = await TestServer.StartAsync(Lobby2());
            using var old = await ConnectRaw(new Uri(server.WsUri + query));

            var ev = await Receive(old);

            Assert.That(ev!.Kind, Is.EqualTo(ServerEventKind.SessionFaulted));
            Assert.That(ev.Reason, Is.EqualTo(ProtocolVersion.UpdateRequiredReason));
            Assert.That(await ReadUntilClosed(old), Is.EqualTo(WebSocketCloseStatus.PolicyViolation));
            await server.WaitForLogAsync("프로토콜 버전 불일치");
        }

        [Test]
        public async Task Rejected_connection_does_not_take_a_seat()
        {
            await using var server = await TestServer.StartAsync(Lobby2());
            using (var old = await ConnectRaw(server.WsUri))   // 버전 없음 = 구 APK
            {
                await Receive(old);
                await ReadUntilClosed(old);
            }

            using var a = await Connect(server.WsUri);
            var ev = await Receive(a);

            Assert.That(ev!.Kind, Is.EqualTo(ServerEventKind.Lobby));
            Assert.That(ev.YourSeat, Is.EqualTo(0), "거절된 접속이 좌석 0 을 쓰면 안 된다");
            Assert.That(ev.Connected, Is.EqualTo(1));
        }

        [Test]
        public async Task Client_transport_attaches_the_current_version()
        {
            await using var server = await TestServer.StartAsync(Lobby2());
            using var client = await WebSocketClientTransport.ConnectAsync(server.WsUri, TimeSpan.FromSeconds(5), CancellationToken.None);

            ServerEvent? first = null;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (first == null && DateTime.UtcNow < deadline)
            {
                var got = client.Drain();
                if (got.Count > 0) first = got[0];
                else await Task.Delay(20);
            }

            Assert.That(first, Is.Not.Null, "5초 안에 아무 이벤트도 오지 않았다");
            Assert.That(first!.Kind, Is.EqualTo(ServerEventKind.Lobby), "버전이 안 붙으면 SessionFaulted 가 온다");
        }
```

(c) `core/tests/Tichu.Server.Tests/EndToEndTests.cs`의 `Late_cleanup_of_an_old_connection_does_not_break_the_next_match`에서 원시 소켓 접속 두 줄을 바꾼다.

```csharp
            await stale.ConnectAsync(ProtocolVersion.Attach(server.WsUri), CancellationToken.None);
```
```csharp
            await leaver.ConnectAsync(ProtocolVersion.Attach(server.WsUri), CancellationToken.None);
```

- [ ] **Step 6: 실패를 확인한다**

Run: `dotnet test core/tests/Tichu.Server.Tests/Tichu.Server.Tests.csproj --filter "FullyQualifiedName~ServerTransportTests" --nologo`
Expected: `Missing_wrong_or_garbage_*` 4건 FAIL(첫 이벤트가 `SessionFaulted`가 아니라 `Lobby`). `Rejected_connection_does_not_take_a_seat` FAIL(관문이 없어 구 접속이 좌석 0을 받고 서버가 닫지 않으므로 `ReadUntilClosed`가 10초 수신 시간 제한으로 예외 — 실패까지 ~10초 걸린다). `Client_transport_attaches_the_current_version`은 **PASS일 수 있다**(관문이 아직 없음 — Step 8의 음성 대조로 검증한다). 기존 테스트는 PASS.

- [ ] **Step 7: 서버 구현**

`core/src/Tichu.Server/WebSocketServerTransport.cs` — `HandleAsync` 바로 위에 추가한다.

```csharp
        /// <summary>
        /// 좌석을 주지 않고 사유 하나(SessionFaulted)만 보낸 뒤 닫는다 — 프로토콜 버전 불일치(M2b §5.3).
        /// 좌석표·대기실은 건드리지 않는다. 바로 끊으면 클라이언트가 아직 읽지 않은 사유가 버려질 수
        /// 있으므로 상대의 닫기 응답을 최대 2초 기다린다.
        /// </summary>
        public async Task RejectAsync(WebSocket ws, string reason)
        {
            var conn = new Connection(ws);
            conn.Enqueue(ProtocolJson.Serialize(ServerEvent.SessionFaulted(reason)));
            conn.Complete(WebSocketCloseStatus.PolicyViolation, "protocol version");
            await conn.WriterTask;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var buf = new byte[256];
            try
            {
                while (ws.State == WebSocketState.CloseSent)
                {
                    var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), cts.Token);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
        }
```

`core/src/Tichu.Server/ServerApp.cs` — `using Tichu.Net;`을 추가하고 `/ws` 핸들러를 바꾼다.

```csharp
            app.Map("/ws", (RequestDelegate)(async ctx =>
            {
                if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
                string? version = ctx.Request.Query[ProtocolVersion.QueryKey];
                using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
                if (!ProtocolVersion.Accepts(version))
                {
                    log($"프로토콜 버전 불일치 — 거절 (받은 값: {(string.IsNullOrEmpty(version) ? "없음" : version)}, 서버: {ProtocolVersion.Current})");
                    await transport.RejectAsync(ws, ProtocolVersion.UpdateRequiredReason);
                    return;
                }
                await transport.HandleAsync(ws, ctx.RequestAborted);
            }));
```

- [ ] **Step 8: 클라이언트 구현**

`core/src/Tichu.Net/WebSocketClientTransport.cs`의 `ConnectAsync`에서 접속 줄만 바꾸고, 요약 주석에 한 줄을 덧붙인다.

```csharp
        /// <summary>접속한다. 접속 URI 에 프로토콜 버전(v)을 붙인다. 시간 초과·거절·주소 오류는 예외로 올라온다.</summary>
```
```csharp
                try { await ws.ConnectAsync(ProtocolVersion.Attach(uri), linked.Token).ConfigureAwait(false); }
```

- [ ] **Step 9: 전체 통과 + 음성 대조**

Run: `dotnet test core/tests/Tichu.Server.Tests/Tichu.Server.Tests.csproj --nologo` → Expected: 전부 PASS(기존 E2E 포함).
Run: `dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --nologo` → Expected: 전부 PASS([Explicit] 제외).
음성 대조 두 가지:
- `ConnectAsync`의 `ProtocolVersion.Attach(uri)`를 `uri`로 잠시 되돌림 → `Client_transport_attaches_the_current_version`과 E2E 완주 테스트가 FAIL → 원복.
- `ServerApp`의 `if (!ProtocolVersion.Accepts(version))` 블록을 잠시 주석 처리 → 관문 테스트 5건 FAIL → 원복.

- [ ] **Step 10: Unity 미러 동기 + Unity 검증**

Run: `pwsh tools/sync-core-to-unity.ps1` → `-Check`(드리프트 없음).
Unity 검증 절차(Global Constraints)를 두 번 돌린다. 배치 실행이 신규 `.cs`의 `.meta`를 만든다.
- `-assemblyNames Tichu.Core.Tests -testFilter "Tichu.Core.Tests.ProtocolVersionTests;Tichu.Core.Tests.ServerAddressTests"` → Expected: `failed=0`, `total`이 dotnet의 두 클래스 합과 같다.
- `-assemblyNames Tichu.Presentation.Tests` → Expected: `failed=0`, `total>0`.
Run: `git status --short Assets/_Project` → `ProtocolVersion.cs.meta`와 `ProtocolVersionTests.cs.meta`가 새로 생겼는지 확인한다.

- [ ] **Step 11: 커밋**

```bash
git add core/src/Tichu.Net/ProtocolVersion.cs core/src/Tichu.Net/WebSocketClientTransport.cs core/src/Tichu.Server/WebSocketServerTransport.cs core/src/Tichu.Server/ServerApp.cs core/tests/Tichu.Core.Tests/ProtocolVersionTests.cs core/tests/Tichu.Server.Tests/ServerTransportTests.cs core/tests/Tichu.Server.Tests/EndToEndTests.cs Assets/_Project/Net/ProtocolVersion.cs Assets/_Project/Net/ProtocolVersion.cs.meta Assets/_Project/Net/WebSocketClientTransport.cs Assets/_Project/Tests/EditMode/ProtocolVersionTests.cs Assets/_Project/Tests/EditMode/ProtocolVersionTests.cs.meta
git commit -m "feat(net): 프로토콜 버전 확인 — 불일치 접속엔 업데이트 안내 후 닫는다

클라이언트는 접속 URI 에 ?v=1 을 붙이고, 서버는 좌석을 주기 전에 확인한다.
다르거나 없으면 SessionFaulted(\"앱을 업데이트해 주세요 …\") 하나를 보내고 닫으며
좌석·대기실은 건드리지 않는다. 클라이언트는 기존 폴트→메뉴 경로로 안내를 띄운다.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: 서버 `--host` 옵션

**Files:**
- Modify: `core/src/Tichu.Server/ServerOptions.cs`
- Modify: `core/src/Tichu.Server/Program.cs`
- Test: `core/tests/Tichu.Server.Tests/ServerOptionsTests.cs`

**Interfaces:**
- Consumes: 없음
- Produces: `ServerOptions.Host`(string, 기본 `"0.0.0.0"`), CLI `--host <주소>`. Task 4의 systemd 유닛이 `--host 127.0.0.1`을 넘긴다.

- [ ] **Step 1: 테스트를 쓴다**

`ServerOptionsTests.cs`의 `Defaults_match_the_spec`에 한 줄을 추가한다.
```csharp
            Assert.That(o.Host, Is.EqualTo("0.0.0.0"));
```
`All_flags_parse`의 인자 배열 끝에 `"--host", "127.0.0.1"`을 추가하고 단언을 한 줄 추가한다.
```csharp
                "--port", "9000", "--ai-delay", "0", "--seed", "42", "--host", "127.0.0.1"
```
```csharp
            Assert.That(o.Host, Is.EqualTo("127.0.0.1"));
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test core/tests/Tichu.Server.Tests/Tichu.Server.Tests.csproj --filter "FullyQualifiedName~ServerOptionsTests" --nologo`
Expected: 빌드 실패 `CS1061: 'ServerOptions' does not contain a definition for 'Host'`.

- [ ] **Step 3: 구현**

`ServerOptions.cs` — 클래스 요약 주석의 CLI 목록에 `--host`를 넣고, `Port` 위에 속성을 추가한다.
```csharp
    /// CLI 로 받는 값: --seats --difficulty --target --host --port --ai-delay --seed.
```
```csharp
        /// <summary>바인딩 주소. 기본 0.0.0.0 = 같은 Wi-Fi 개발 테스트. 클라우드는 127.0.0.1(Caddy 뒤, M2b).</summary>
        public string Host { get; set; } = "0.0.0.0";
```
`Parse`의 switch에 추가한다.
```csharp
                    case "--host":       o.Host = value; break;
```
`Program.cs` — 사용법 문구와 바인딩을 바꾼다.
```csharp
    Console.Error.WriteLine("사용법: --seats 0,2 --difficulty Hard --target 1000 --port 7777 [--host 0.0.0.0] [--ai-delay 900] [--seed N]");
```
```csharp
var app = ServerApp.Build(opt, $"http://{opt.Host}:{opt.Port}");
```

- [ ] **Step 4: 통과 확인 + 바인딩 실측**

Run: Step 2 명령 → Expected: PASS.
실측: 서버를 루프백에 띄우고 루프백 접속은 되고 LAN 주소 접속은 안 되는지 본다.
```powershell
$p = Start-Process dotnet -ArgumentList "run","--project","core/src/Tichu.Server","--","--host","127.0.0.1","--port","7799" -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 8
curl.exe -s -o NUL -w "%{http_code}`n" http://127.0.0.1:7799/ws          # Expected: 400 (WebSocket 아닌 요청)
$lan = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -like '192.168.*' } | Select-Object -First 1).IPAddress
Test-NetConnection $lan -Port 7799 -InformationLevel Quiet                # Expected: False
Stop-Process -Id $p.Id -Force; Get-Process Tichu.Server -ErrorAction SilentlyContinue | Stop-Process -Force
```
⚠️ 서버가 떠 있으면 core 재빌드 때 DLL이 잠긴다. 반드시 종료를 확인한다.

- [ ] **Step 5: 커밋**

```bash
git add core/src/Tichu.Server/ServerOptions.cs core/src/Tichu.Server/Program.cs core/tests/Tichu.Server.Tests/ServerOptionsTests.cs
git commit -m "feat(server): --host 옵션 — 클라우드에선 루프백에만 바인딩한다

기본값 0.0.0.0 은 같은 Wi-Fi 개발 테스트 그대로. 클라우드는 Caddy 뒤라 127.0.0.1.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: 배포 자산 — 서버 구성 · 세팅 · 릴리스 설치 · 배포/스모크 스크립트 · 안내서

**Files:**
- Create: `deploy/server/tichu-server.service`
- Create: `deploy/server/server.env.default`
- Create: `deploy/server/Caddyfile.template`
- Create: `deploy/server/setup-server.sh`
- Create: `deploy/server/install-release.sh`
- Create: `tools/deploy-server.ps1`
- Create: `tools/smoke-server.ps1`
- Create: `docs/ops/server-runbook.md`

**Interfaces:**
- Consumes: `Tichu.Server --host --port` (Task 3), `ProtocolVersion.Current`·`?v=`·업데이트 안내 문구(Task 2)
- Produces:
  - 서버 경로: `/opt/tichu/releases/<이름>/`, 심볼릭 링크 `/opt/tichu/current`, `/opt/tichu/bin/install-release.sh`, `/etc/tichu/server.env`
  - `sudo bash setup-server.sh <도메인>` (멱등)
  - `sudo /opt/tichu/bin/install-release.sh <tgz 경로>` → 0 = 설치·기동 확인, 1 = 실패(직전 릴리스로 되돌림)
  - `pwsh tools/deploy-server.ps1 [-Yes]`
  - `pwsh tools/smoke-server.ps1 -Url <wss://…/ws> [-NoVersion] [-Version N] [-ExpectReject]` → 종료 코드 0 = 기대대로, 1 = 아님

- [ ] **Step 1: systemd 유닛 · 기본 게임 인자 · Caddyfile 템플릿**

`deploy/server/tichu-server.service`:
```ini
# Tichu 게임 서버(M2b). setup-server.sh 가 /etc/systemd/system 에 설치한다.
# 바인딩은 여기 고정(루프백 = Caddy 뒤), 게임 인자는 /etc/tichu/server.env 의 TICHU_GAME_ARGS.
[Unit]
Description=Tichu game server
After=network-online.target
Wants=network-online.target

[Service]
User=tichu
Group=tichu
EnvironmentFile=/etc/tichu/server.env
WorkingDirectory=/opt/tichu/current
ExecStart=/opt/tichu/current/Tichu.Server --host 127.0.0.1 --port 7777 $TICHU_GAME_ARGS
Restart=always
RestartSec=2
TimeoutStopSec=10
# 1GB 머신: 서버 GC 는 코어마다 힙을 잡아 메모리를 더 쓴다
Environment=DOTNET_gcServer=0

[Install]
WantedBy=multi-user.target
```

`deploy/server/server.env.default`:
```sh
# Tichu 게임 서버 인자(M2b). 고친 뒤: sudo systemctl restart tichu-server (진행 중인 판이 끊긴다)
TICHU_GAME_ARGS=--seats 0,2 --difficulty Hard --target 1000
```

`deploy/server/Caddyfile.template`:
```
# M2b: TLS(wss) 종단 + 게임 서버 프록시. __DOMAIN__ 은 setup-server.sh 가 채운다.
__DOMAIN__ {
	reverse_proxy 127.0.0.1:7777
}
```

- [ ] **Step 2: 서버 세팅 스크립트(1회, 멱등)**

`deploy/server/setup-server.sh`:
```bash
#!/usr/bin/env bash
# Lightsail 서버 1회 세팅(M2b 스펙 §4). 여러 번 돌려도 같은 결과(멱등).
# 사용: sudo bash setup-server.sh <도메인>
#   같은 폴더에 tichu-server.service · server.env.default · Caddyfile.template · install-release.sh 가 있어야 한다.
set -euo pipefail
domain="${1:?도메인을 인자로 주세요 (예: play.example.com)}"
here="$(cd "$(dirname "$0")" && pwd)"

export DEBIAN_FRONTEND=noninteractive
apt-get update -q
apt-get install -y -q caddy libicu74   # libicu74: .NET 세계화(self-contained 도 시스템 ICU 를 쓴다)

id tichu >/dev/null 2>&1 || useradd --system --home-dir /opt/tichu --shell /usr/sbin/nologin tichu
install -d -o tichu -g tichu /opt/tichu /opt/tichu/releases
install -d /opt/tichu/bin /etc/tichu
install -m 755 "$here/install-release.sh" /opt/tichu/bin/install-release.sh
install -m 644 "$here/tichu-server.service" /etc/systemd/system/tichu-server.service
# 게임 인자는 운영 중에 고칠 수 있으므로 이미 있으면 덮어쓰지 않는다
[ -f /etc/tichu/server.env ] || install -m 644 "$here/server.env.default" /etc/tichu/server.env
sed "s/__DOMAIN__/$domain/g" "$here/Caddyfile.template" > /etc/caddy/Caddyfile

systemctl daemon-reload
systemctl enable tichu-server   # 첫 배포(install-release.sh)가 시작한다
systemctl enable caddy
systemctl reload-or-restart caddy
echo "세팅 완료: $domain — 다음은 PC 에서 tools/deploy-server.ps1"
```

- [ ] **Step 3: 서버 쪽 릴리스 설치 스크립트**

`deploy/server/install-release.sh`:
```bash
#!/usr/bin/env bash
# 서버 쪽 릴리스 설치(M2b). deploy-server.ps1 이 sudo 로 부른다.
# tgz 를 releases/<이름> 에 풀고 current 를 바꾼 뒤 재시작한다. 30초 안에 7777 을 듣지 않으면
# 직전 릴리스로 되돌리고 exit 1. 성공하면 현재·직전 릴리스만 남긴다.
set -euo pipefail
tgz="${1:?tgz 경로}"
base=/opt/tichu
name="$(basename "$tgz" .tgz)"
dest="$base/releases/$name"

prev="$(readlink -f "$base/current" 2>/dev/null || true)"
rm -rf "$dest"
mkdir -p "$dest"
tar -xzf "$tgz" -C "$dest"
chmod +x "$dest/Tichu.Server"
chown -R tichu:tichu "$dest"
ln -sfn "$dest" "$base/current"
systemctl restart tichu-server

healthy() {
  for _ in $(seq 1 30); do
    if systemctl is-active --quiet tichu-server && ss -ltnH 'sport = :7777' | grep -q .; then return 0; fi
    sleep 1
  done
  return 1
}

if healthy; then
  for d in "$base"/releases/*; do
    if [ "$d" != "$dest" ] && [ "$d" != "$prev" ]; then rm -rf "$d"; fi
  done
  rm -f "$tgz"
  echo "설치 완료: $name"
  exit 0
fi

echo "기동 확인 실패: $name" >&2
journalctl -u tichu-server -n 30 --no-pager >&2 || true
if [ -n "$prev" ] && [ -d "$prev" ] && [ "$prev" != "$dest" ]; then
  ln -sfn "$prev" "$base/current"
  systemctl restart tichu-server
  echo "직전 릴리스로 되돌림: $(basename "$prev")" >&2
fi
exit 1
```

- [ ] **Step 4: PC 쪽 스모크 · 배포 스크립트**

`tools/smoke-server.ps1`:
```powershell
<#
.SYNOPSIS
    서버에 WebSocket 으로 붙어 첫 메시지를 확인한다(M2b 완료 기준 확인용).
.DESCRIPTION
    기본: 현재 프로토콜 버전(core/src/Tichu.Net/ProtocolVersion.cs 에서 읽음)으로 접속해 Lobby 를 기대한다.
    -ExpectReject: 업데이트 안내(SessionFaulted "앱을 업데이트해 주세요…")를 기대한다.
    -NoVersion: v 쿼리를 붙이지 않는다(구 APK 흉내).
    매치가 진행 중이면 서버가 "room full" 로 닫는다 — 그 사유를 출력하고 실패로 끝낸다.
    접속한 동안 대기실 좌석 하나를 잠깐 쓰고, 닫으면 반납된다.
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
$uri = if ($NoVersion) { [Uri]$Url } else { [Uri]("$Url" + "?v=$Version") }

$ws = [System.Net.WebSockets.ClientWebSocket]::new()
$cts = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(10))
$code = 1
try {
    $ws.ConnectAsync($uri, $cts.Token).GetAwaiter().GetResult()
    $buf = [byte[]]::new(65536)
    $r = $ws.ReceiveAsync([ArraySegment[byte]]::new($buf), $cts.Token).GetAwaiter().GetResult()
    if ($r.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Close) {
        Write-Host "서버가 메시지 없이 닫음: $($ws.CloseStatus) $($ws.CloseStatusDescription)"
    } else {
        $msg = [Text.Encoding]::UTF8.GetString($buf, 0, $r.Count) | ConvertFrom-Json
        Write-Host "받음: Kind=$($msg.Kind) $($msg.Reason)"
        $ok = if ($ExpectReject) { $msg.Kind -eq 'SessionFaulted' -and $msg.Reason -like '앱을 업데이트해 주세요*' } else { $msg.Kind -eq 'Lobby' }
        if ($ok) { $code = 0 }
    }
} catch {
    Write-Host "접속 실패: $($_.Exception.GetBaseException().Message)"
} finally {
    if ($ws.State -eq [System.Net.WebSockets.WebSocketState]::Open) {
        try { $ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "smoke", [Threading.CancellationToken]::None).GetAwaiter().GetResult() } catch { }
    }
    $ws.Dispose()
}
Write-Host ($(if ($code -eq 0) { "OK" } else { "FAIL" }) + " — $uri")
exit $code
```

`tools/deploy-server.ps1`:
```powershell
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
```

- [ ] **Step 5: 로컬 검증(서버 없이 할 수 있는 만큼)**

```powershell
# 1) 셸 스크립트 문법 (Git Bash)
bash -n deploy/server/setup-server.sh; bash -n deploy/server/install-release.sh        # Expected: 출력 없음
# 2) 줄바꿈이 LF 인지(.gitattributes eol=lf) — CRLF 면 서버에서 '\r' 오류
git add deploy/server; git ls-files --eol deploy/server                                 # Expected: 모두 i/lf
# 3) PowerShell 구문
foreach ($f in "tools/deploy-server.ps1","tools/smoke-server.ps1") { $e=$null; [void][Management.Automation.Language.Parser]::ParseFile((Resolve-Path $f), [ref]$null, [ref]$e); "$f errors=$($e.Count)" }   # Expected: errors=0
# 4) linux-x64 self-contained publish 가 되는지
dotnet publish core/src/Tichu.Server/Tichu.Server.csproj -c Release -r linux-x64 --self-contained true -o "$env:TEMP\tichu-pub-check" --nologo   # Expected: 성공, Tichu.Server(확장자 없음) 생성
Test-Path "$env:TEMP\tichu-pub-check\Tichu.Server"                                       # Expected: True
# 5) 스모크 스크립트를 로컬 서버로 검증(ws, 루프백)
$p = Start-Process dotnet -ArgumentList "run","--project","core/src/Tichu.Server","--","--host","127.0.0.1","--port","7798" -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 8
pwsh tools/smoke-server.ps1 -Url ws://127.0.0.1:7798/ws; "exit=$LASTEXITCODE"                          # Expected: Kind=Lobby, OK, exit=0
pwsh tools/smoke-server.ps1 -Url ws://127.0.0.1:7798/ws -NoVersion -ExpectReject; "exit=$LASTEXITCODE"  # Expected: 앱을 업데이트해 주세요…, OK, exit=0
pwsh tools/smoke-server.ps1 -Url ws://127.0.0.1:7798/ws -Version 999 -ExpectReject; "exit=$LASTEXITCODE" # Expected: OK, exit=0
pwsh tools/smoke-server.ps1 -Url ws://127.0.0.1:7798/ws -NoVersion; "exit=$LASTEXITCODE"                # Expected: FAIL, exit=1 (Lobby 를 기대했는데 거절)
pwsh tools/smoke-server.ps1 -Url ws://127.0.0.1:1/ws; "exit=$LASTEXITCODE"                             # Expected: 접속 실패…, FAIL, exit=1 (10초 안에 끝남)
Stop-Process -Id $p.Id -Force; Get-Process Tichu.Server -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item "$env:TEMP\tichu-pub-check" -Recurse -Force
```

- [ ] **Step 6: 운영 안내서**

`docs/ops/server-runbook.md` — 아래 내용으로 만든다. `<도메인>`·`<고정 IP>`는 Task 6에서 실제 값으로 채운다.

````markdown
# Tichu 게임 서버 운영 안내서 (M2b)

- 주소: `wss://play.<도메인>/ws` · 서버: Lightsail 서울 `<인스턴스 이름>` · 고정 IP `<고정 IP>`
- 구성: Caddy(443, 인증서 자동) → `127.0.0.1:7777` Tichu.Server(systemd `tichu-server`)
- 스펙: `docs/superpowers/specs/2026-10-07-mp-m2b-cloud-deploy-design.md`

## ⚠️ 먼저 알아둘 것
- **배포·재시작하면 진행 중인 판이 끊긴다.** 판이 없을 때만 한다(재접속은 M5).
- 서버는 **방 하나, 한 번에 한 매치**. 판이 진행 중이면 새 접속은 "room full"로 닫힌다.
- 테스터는 접속 화면에 **전체 주소 `wss://play.<도메인>`** 을 입력한다. `play.<도메인>`만 쓰면 같은 Wi-Fi 개발용(`ws://…:7777`)으로 해석돼 접속이 안 된다.
- SSH 키: `C:\Users\user\.keys\<키 파일>` — **저장소에 넣지 않는다. 업로드 키와 함께 백업.**
- 도메인 자동 갱신이 켜져 있어야 한다. 만료되면 출시 앱 전체가 접속 불가.

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
| 판 진행 중인지 | 로그 마지막 줄이 `매치 시작` 이후이고 `다음 매치 대기`가 없으면 진행 중 |
| 재시작 | `sudo systemctl restart tichu-server` |
| 게임 인자 변경(목표 점수 등) | `sudo nano /etc/tichu/server.env` → `sudo systemctl restart tichu-server` |
| 지금 릴리스 | `readlink /opt/tichu/current` |
| 직전 릴리스로 수동 롤백 | `ls /opt/tichu/releases` → `sudo ln -sfn /opt/tichu/releases/<직전> /opt/tichu/current && sudo systemctl restart tichu-server` |
| Caddy 로그(인증서) | `sudo journalctl -u caddy -n 100 --no-pager` |

## 서버를 처음부터 다시 만들 때
1. Lightsail 서울 · Ubuntu 24.04 · 1GB 인스턴스 → 고정 IP 연결 → 방화벽 22/80/443 → DNS `play` A 레코드를 새 IP로.
2. `scp -i <키> -r deploy/server ubuntu@<IP>:/tmp/tichu-setup` → `ssh -i <키> ubuntu@<IP> "sudo bash /tmp/tichu-setup/setup-server.sh play.<도메인>"`
3. `pwsh tools/deploy-server.ps1`
서버에는 보존할 상태가 없다(매치는 메모리, 영속 데이터 없음).

## 비용
인스턴스 월 약 $7(1GB) + 도메인 연 $<금액>(Task 5에서 확정).
````

- [ ] **Step 7: 커밋**

```bash
git add deploy/server tools/deploy-server.ps1 tools/smoke-server.ps1 docs/ops/server-runbook.md
git commit -m "feat(ops): M2b 배포 자산 — systemd·Caddy 세팅, 릴리스 설치(자동 롤백), 배포·스모크 스크립트

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: AWS 준비 — 인스턴스 · 고정 IP · 방화벽 · 도메인 · DNS (브라우저 제어)

> 이 태스크는 코드가 아니라 사용자 Chrome에서 하는 콘솔 작업이다. **Claude in Chrome**으로 진행한다. 먼저 `claude-in-chrome` 스킬을 불러오고, `tabs_context_mcp` → `tabs_create_mcp`로 **새 탭**에서 작업한다. 단계마다 스크린샷으로 확인한다. 페이지 구성이 아래 설명과 다르면 추측해서 누르지 말고 멈춰서 보고한다. 결과는 마지막에 확인 명령으로 검증한다.

**Files:** 없음(결과 값은 Task 6에서 안내서에 기록)

**Interfaces:**
- Produces: 인스턴스 이름(`tichu-seoul-1`), 고정 IP(`<IP>`), 도메인(`<도메인>`), DNS `play.<도메인>` → `<IP>`, SSH 키 `C:\Users\user\.keys\tichu-lightsail-seoul.pem`

- [ ] **Step 1: AWS 로그인 (사용자)**

새 탭에서 `https://lightsail.aws.amazon.com/`을 연다. 로그인 화면이면 **멈추고** 사용자에게 로그인과 2단계 인증을 부탁한다. AWS 계정이 없으면 가입(결제 수단·전화 인증)은 사용자가 직접 한다. 콘솔 홈이 보이면 진행한다.

- [ ] **Step 2: 인스턴스 생성**

Lightsail → Create instance:
- 리전(Location): **Seoul (ap-northeast-2)**, 가용 영역은 기본값
- 플랫폼 Linux/Unix → OS Only → **Ubuntu 24.04 LTS**
- SSH 키: 리전 **기본 키**를 쓴다(Step 5에서 다운로드)
- 네트워크: 듀얼 스택(IPv4 포함)
- 플랜: **1GB 메모리** 번들(월 약 $7). 화면의 실제 월 금액을 읽는다.
- 이름: `tichu-seoul-1`

**멈춤:** "Create instance"를 누르기 전에 리전·OS·플랜·월 금액을 스크린샷과 함께 보여 주고 사용자 승인을 받는다. 승인 후 생성하고, 상태가 Running이 될 때까지 기다린다.

- [ ] **Step 3: 고정 IP 연결**

Networking → Create static IP → 서울 → 인스턴스 `tichu-seoul-1`에 연결 → 이름 `tichu-seoul-ip`. 인스턴스에 붙어 있는 동안은 무료다(화면 문구로 확인). 할당된 IP를 기록한다.

- [ ] **Step 4: 방화벽**

인스턴스 → Networking → IPv4 Firewall: **SSH 22 · HTTP 80 · HTTPS 443만** 남긴다. 기본은 22·80이므로 HTTPS 443을 추가한다. 7777 같은 다른 규칙이 있으면 지운다. IPv6 방화벽도 같은 규칙인지 확인한다. 결과를 스크린샷으로 남긴다.

- [ ] **Step 5: SSH 키 다운로드·보관**

Account → SSH keys → 서울 리전 기본 키 다운로드(`.pem`). 다운로드 폴더에서 옮기고 권한을 잠근다(Windows OpenSSH는 권한이 넓은 키를 거부한다).
```powershell
$dl = Get-ChildItem "$env:USERPROFILE\Downloads\LightsailDefaultKey-ap-northeast-2*.pem" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
New-Item -ItemType Directory -Force "C:\Users\user\.keys" | Out-Null
Move-Item $dl.FullName "C:\Users\user\.keys\tichu-lightsail-seoul.pem"
icacls "C:\Users\user\.keys\tichu-lightsail-seoul.pem" /inheritance:r
icacls "C:\Users\user\.keys\tichu-lightsail-seoul.pem" /grant:r "$($env:USERNAME):(R)"
icacls "C:\Users\user\.keys\tichu-lightsail-seoul.pem"     # Expected: 현재 사용자 (R) 한 줄만
```

- [ ] **Step 6: 도메인 이름 정하기 (사용자 결정)**

Lightsail → Domains & DNS → Register domain(없으면 Route 53 → Registered domains → Register domains). 후보 이름 몇 개의 가용 여부와 연 가격을 검색해 표로 보여 준다. 조건은 "tichu" 미포함, `.com` 우선, 짧고 철자가 쉬운 이름이다. **멈춤:** 사용자가 이름을 고른다.

- [ ] **Step 7: 도메인 구매 (사용자 확정)**

선택한 도메인을 장바구니에 넣고 다음을 확인한다: **자동 갱신 ON**, **개인정보 보호(Privacy protection) ON**, 등록 기간 1년. 연락처 정보 입력은 사용자가 한다. **멈춤:** 결제 화면에서 금액을 보여 주고, 구매 확정 클릭은 사용자가 한다.
구매 뒤 등록자 이메일로 **도메인 연락처 확인 메일**이 온다. 사용자에게 15일 안에 링크를 눌러야 한다고 알린다(누르지 않으면 도메인이 정지된다).
등록 완료까지 몇 분에서 몇 시간이 걸릴 수 있다. 상태가 성공으로 바뀔 때까지 기다린다.

- [ ] **Step 8: DNS A 레코드**

Lightsail에서 등록했으면 Lightsail DNS 영역에, Route 53에서 등록했으면 자동 생성된 Route 53 호스팅 영역에 레코드를 추가한다. 레코드는 **A 레코드 `play` → 고정 IP**이고, 다른 레코드는 만들지 않는다.
확인:
```powershell
Resolve-DnsName play.<도메인> -Type A -Server 8.8.8.8      # Expected: IPAddress = <고정 IP> (전파에 몇 분 걸릴 수 있다 — 1분 간격으로 최대 30분 재시도)
```

- [ ] **Step 9: 결과 보고**

인스턴스 이름, 고정 IP, 도메인, 월·연 비용, 키 경로를 사용자에게 보고한다. 비밀 정보는 없다(IP·도메인은 공개 정보).

---

### Task 6: 서버 세팅 → 첫 배포 → 완료 기준 2·3·4·5·7

**Files:**
- Modify: `docs/ops/server-runbook.md` (실제 값 채우기)

**Interfaces:**
- Consumes: Task 4의 스크립트, Task 5의 IP·도메인·키
- Produces: 동작 중인 `wss://play.<도메인>/ws`

> ⚠️ PowerShell 도구는 호출 사이에 변수를 유지하지 않는다. 이 태스크와 Task 7에서 `$key`·`$h`를 쓰는 **모든 호출의 맨 앞에** 다음 줄을 넣는다.
> `$key = [Environment]::GetEnvironmentVariable('TICHU_SERVER_SSH_KEY','User'); $h = "ubuntu@" + [Environment]::GetEnvironmentVariable('TICHU_SERVER_HOST','User')`
> (Step 2·3은 DNS 전파 전일 수 있으므로 `$h = "ubuntu@<고정 IP>"`로 쓴다.)

- [ ] **Step 1: 환경변수 등록**

```powershell
[Environment]::SetEnvironmentVariable('TICHU_SERVER_HOST', 'play.<도메인>', 'User')
[Environment]::SetEnvironmentVariable('TICHU_SERVER_SSH_KEY', 'C:\Users\user\.keys\tichu-lightsail-seoul.pem', 'User')
```

- [ ] **Step 2: 첫 SSH (사용자 확인 후)**

**멈춤:** "이제 서버에 SSH로 명령을 실행합니다(읽기 전용 확인부터)"라고 알리고 승인을 받는다.
```powershell
$key = [Environment]::GetEnvironmentVariable('TICHU_SERVER_SSH_KEY','User'); $h = "ubuntu@<고정 IP>"
ssh -i $key -o StrictHostKeyChecking=accept-new $h "lsb_release -ds; free -m | head -2; nproc"
# Expected: Ubuntu 24.04.x LTS, total ≈ 900MB 대, nproc 2
```

- [ ] **Step 3: 세팅 스크립트 실행**

```powershell
scp -i $key -r deploy/server "${h}:/tmp/tichu-setup"
ssh -i $key $h "sudo bash /tmp/tichu-setup/setup-server.sh play.<도메인>"
# Expected: 마지막 줄 "세팅 완료: play.<도메인> …"
```
`caddy` 패키지를 찾지 못하면(universe 비활성) 다음을 먼저 실행하고 다시 돌린다: `ssh -i $key $h "sudo add-apt-repository -y universe"`.
확인:
```powershell
ssh -i $key $h "systemctl is-enabled tichu-server caddy; systemctl is-active caddy; cat /etc/tichu/server.env | tail -1"
# Expected: enabled / enabled / active / TICHU_GAME_ARGS=--seats 0,2 --difficulty Hard --target 1000
```

- [ ] **Step 4: 첫 배포 (사용자 확인 후)**

**멈춤:** 배포는 서버를 (재)시작한다. 첫 배포라 끊길 판은 없지만, 규칙대로 승인을 받는다.
```powershell
pwsh tools/deploy-server.ps1 -Yes
# Expected: "설치 완료: tichu-server-…" → smoke "받음: Kind=Lobby" "OK" → "배포 완료"
```
smoke가 TLS 오류로 실패하면 Caddy가 아직 인증서를 받는 중일 수 있다. `ssh … "sudo journalctl -u caddy -n 50 --no-pager"`에서 `certificate obtained successfully`를 확인한 뒤 smoke만 다시 돌린다. 추측해서 고치지 않는다.

- [ ] **Step 5: 완료 기준 2·5·6(PC 쪽)**

```powershell
curl.exe -s -o NUL -w "%{http_code}`n" https://play.<도메인>/ws; "exit=$LASTEXITCODE"   # DoD 2 — Expected: 400, exit=0 (인증서 검증 통과 + 앱이 비WebSocket 요청에 400)
Test-NetConnection <고정 IP> -Port 7777 -InformationLevel Quiet                          # DoD 5 — Expected: False
ssh -i $key $h "ss -ltnH 'sport = :7777'"                                               # DoD 5 — Expected: 127.0.0.1:7777 만
pwsh tools/smoke-server.ps1 -Url wss://play.<도메인>/ws -NoVersion -ExpectReject          # DoD 6(PC) — Expected: OK
pwsh tools/smoke-server.ps1 -Url wss://play.<도메인>/ws -Version 999 -ExpectReject        # DoD 6(PC) — Expected: OK
```

- [ ] **Step 6: 롤백 훈련(Review Focus 3)**

깨진 릴리스를 일부러 설치해, 실패 시 직전 릴리스로 돌아가는지 확인한다.
원격 스크립트에 `\r`이 섞이면 bash가 깨지므로 CR을 지운 뒤 넘긴다.
```powershell
$remote = @'
set -e
mkdir -p /tmp/broken && printf '#!/bin/sh\nexit 1\n' > /tmp/broken/Tichu.Server
tar -czf /tmp/tichu-server-broken.tgz -C /tmp/broken .
before=$(readlink /opt/tichu/current)
sudo /opt/tichu/bin/install-release.sh /tmp/tichu-server-broken.tgz && echo "UNEXPECTED SUCCESS" || echo "exit=$? (expected 1)"
after=$(readlink /opt/tichu/current)
[ "$before" = "$after" ] && echo "ROLLBACK OK: $after" || echo "ROLLBACK FAILED: $before -> $after"
sudo rm -rf /opt/tichu/releases/tichu-server-broken /tmp/broken /tmp/tichu-server-broken.tgz
'@ -replace "`r", ""
ssh -i $key $h $remote
pwsh tools/smoke-server.ps1 -Url wss://play.<도메인>/ws      # Expected: OK (서비스가 살아 있다)
```
Expected: `exit=1 (expected 1)`, `ROLLBACK OK: …`, smoke OK.

- [ ] **Step 7: 완료 기준 3 — 프로세스가 죽으면 다시 뜬다**

```powershell
ssh -i $key $h "sudo systemctl show -p NRestarts tichu-server; sudo systemctl kill -s SIGKILL tichu-server; sleep 6; systemctl is-active tichu-server; sudo systemctl show -p NRestarts tichu-server"
# Expected: NRestarts 가 1 늘고 active
pwsh tools/smoke-server.ps1 -Url wss://play.<도메인>/ws      # Expected: OK
```

- [ ] **Step 8: 완료 기준 4 — 재부팅 후 다시 뜬다**

```powershell
ssh -i $key $h "sudo reboot"
# 90초 동안 15초 간격으로 smoke 재시도
$ok = $false; foreach ($i in 1..6) { Start-Sleep 15; pwsh tools/smoke-server.ps1 -Url wss://play.<도메인>/ws; if ($LASTEXITCODE -eq 0) { $ok = $true; break } }; "rebooted ok=$ok"
# Expected: rebooted ok=True
```

- [ ] **Step 9: 완료 기준 7 — 배포 스크립트로 갱신**

```powershell
$before = ssh -i $key $h "readlink /opt/tichu/current"
pwsh tools/deploy-server.ps1 -Yes
$after = ssh -i $key $h "readlink /opt/tichu/current"
"$before -> $after"                                     # Expected: 서로 다른 릴리스 이름, smoke OK
ssh -i $key $h "ls /opt/tichu/releases | wc -l"         # Expected: 2 (현재 + 직전)
```

- [ ] **Step 10: 안내서에 실제 값 기록 + 커밋**

`docs/ops/server-runbook.md`의 `<도메인>`, `<고정 IP>`, `<인스턴스 이름>`, `<키 파일>`, 도메인 연 금액을 실제 값으로 바꾼다(키 **경로**만 적고 키 내용은 적지 않는다).
```bash
git add docs/ops/server-runbook.md
git commit -m "docs(ops): 서버 안내서에 실제 주소·인스턴스 기록 (M2b 완료 기준 2·3·4·5·7 통과)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: 클라이언트 실기 — 구 APK 안내 · 에디터 · APK 빌드 · S23 LTE (완료 기준 1·6·8)

**Files:** 없음(검증만). 발견된 버그는 이 계획 밖이다 → 사용자에게 보고하고 수정 여부를 묻는다.

- [ ] **Step 1: 테스터 안내문 준비**

사용자에게 전달할 안내를 채팅에 쓴다.
- 접속 주소는 **`wss://play.<도메인>`** 전체를 입력한다(도메인만 쓰면 안 된다).
- 테스트 중에는 서버 목표 점수를 200으로 낮춘다(아래 Step 4). 끝나면 1000으로 되돌린다.

- [ ] **Step 2: 완료 기준 6 — 지금 폰에 깔린 구 APK(새 APK 설치 전에!)**

구 APK는 포트 버그 때문에 클라우드에 닿지 못한다. 그래서 같은 Wi-Fi의 **로컬 서버(새 코드)** 로 확인한다.
```powershell
$p = Start-Process dotnet -ArgumentList "run","--project","core/src/Tichu.Server","--","--seats","0,2","--target","200" -PassThru
# PC IP 확인: (Get-NetIPAddress -AddressFamily IPv4 | ? IPAddress -like '192.168.*').IPAddress
```
사용자: S23(Wi-Fi) → 친구방 → `<PC IP>` 입력 → 접속.
Expected: "앱을 업데이트해 주세요 (서버 프로토콜 v1)" 알림이 뜨고 메뉴로 돌아온다. 서버 콘솔에 `프로토콜 버전 불일치 — 거절 (받은 값: 없음, 서버: 1)`이 찍힌다.
끝나면 `Stop-Process -Id $p.Id -Force; Get-Process Tichu.Server -ErrorAction SilentlyContinue | Stop-Process -Force`.

- [ ] **Step 3: 개발 APK 빌드**

Unity 에디터를 닫은 상태(`Get-Process Unity`가 비어 있음)에서 실행한다(~15분).
```powershell
Remove-Item "$env:TEMP\b.log" -ErrorAction SilentlyContinue
$p = Start-Process -FilePath "E:\6000.3.17f1\Editor\Unity.exe" -PassThru -ArgumentList @("-batchmode","-quit","-projectPath","C:\Users\user\Desktop\Project\Tichu","-executeMethod","DevBuild.BuildDevApk","-logFile","$env:TEMP\b.log")
```
`Build/TichuMaster-dev-arm64.apk`의 수정 시각이 바뀌고 로그에 빌드 성공 문구가 나올 때까지 폴링한다. 실패하면 `b.log`의 `error`를 보고한다. 끝나면 프로세스가 남아 있는지 확인한다(ADB 때문에 남으면 `taskkill /PID $p.Id /F`).

- [ ] **Step 4: 테스트용 목표 점수 200**

```powershell
ssh -i $key $h "sudo sed -i 's/--target [0-9]*/--target 200/' /etc/tichu/server.env && sudo systemctl restart tichu-server && tail -1 /etc/tichu/server.env"
# Expected: TICHU_GAME_ARGS=--seats 0,2 --difficulty Hard --target 200
```

- [ ] **Step 5: APK 설치**

사용자에게 S23을 USB(디버깅 허용)로 연결해 달라고 한다.
```powershell
$adb = "E:\6000.3.17f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
& $adb devices                                                    # Expected: 기기 1대 "device"
& $adb install -r "Build\TichuMaster-dev-arm64.apk"               # Expected: Success
```
`INSTALL_FAILED_UPDATE_INCOMPATIBLE`(서명 불일치)이면 **멈추고** 사용자에게 기존 앱 제거 여부를 묻는다(제거하면 볼륨 설정이 초기화된다). 승인을 받으면 `& $adb uninstall com.kas328.tichumaster` 후 다시 설치한다.

- [ ] **Step 6: 완료 기준 1 · 8 — 실기 (사용자)**

사용자에게 체크리스트를 주고 결과를 받는다. 그동안 서버 로그를 본다: `ssh -i $key $h "sudo journalctl -u tichu-server -f"`(백그라운드 실행, 확인 후 종료).
1. **S23 Wi-Fi 끄기(모바일 데이터)** → 친구방 → `wss://play.<도메인>` → 접속 → "기다리는 중 1/2".
2. 두 번째 인간: Unity 에디터 Play → 친구방 → 같은 주소(에디터는 PC 인터넷으로 클라우드에 접속). 또는 다른 폰.
3. 목표 200 매치를 끝까지 둔다 → 양쪽 결과 화면. **(DoD 1)** 서버 로그에 `좌석 0 접속`, `좌석 2 접속`, 매치 시작, 매치 종료가 찍힌다.
4. 그 판 동안 밀린 세 가지를 본다. **(DoD 8)**
   - 큰 티츄·교환을 내 차례가 아니어도 바로 묻는다(동시 결정 `d755eef`).
   - 다른 좌석 차례에 작은 티츄를 눌러도 즉시 적용·표시된다(`2d30413`·`1afc015`).
   - 매치를 마친 뒤 메뉴 → 다시 친구방/싱글로 들어가도 이전 화면이 덮지 않는다(캔버스 `2fe1ecc`).
5. 결과를 항목별 OK / 이상함(무엇이)으로 받는다.

- [ ] **Step 7: 목표 점수 원복**

```powershell
ssh -i $key $h "sudo sed -i 's/--target [0-9]*/--target 1000/' /etc/tichu/server.env && sudo systemctl restart tichu-server && tail -1 /etc/tichu/server.env"
# Expected: … --target 1000
```

- [ ] **Step 8: 전체 자동 테스트 재확인 (완료 기준 9)**

```powershell
dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --nologo      # Expected: 전부 PASS
dotnet test core/tests/Tichu.Server.Tests/Tichu.Server.Tests.csproj --nologo    # Expected: 전부 PASS
pwsh tools/sync-core-to-unity.ps1 -Check                                       # Expected: 드리프트 없음
```
Unity `-assemblyNames Tichu.Presentation.Tests` → Expected: `failed=0`, `total>0`.

---

### Task 8: 마무리 — 결과 보고 · 메모리 · 브랜치 정리

**Files:**
- Create: `docs/reports/release/티츄_M2b_배포결과.html`
- Modify: `docs/catalog.json` (항목 2개 추가: 설계 HTML, 배포결과 HTML)

- [ ] **Step 1: 결과 보고 HTML**

`docs/reports/release/티츄_M2b_배포결과.html`(자립형, 한국어, 인라인 CSS, 라이트/다크): 완료 기준 9개 표(통과/실패와 근거 수치 — smoke 출력, NRestarts, 재부팅 시간, 실기 결과), 실제 주소·비용, 롤백 훈련 결과, 발견 사항과 이월(M3로 넘길 것). 브라우저로 연다.

- [ ] **Step 2: 카탈로그 항목 추가**

`docs/catalog.json`의 기존 항목 형식을 그대로 따라 `티츄_M2b_클라우드배포_설계.html`(design)과 `티츄_M2b_배포결과.html`(release) 두 항목을 추가한다. 파일명은 바꾸지 않는다. 허브 Artifact 재발행 여부는 사용자에게 묻는다.

- [ ] **Step 3: 커밋**

```bash
git add "docs/reports/release/티츄_M2b_배포결과.html" docs/catalog.json
git commit -m "docs(mp): M2b 배포 결과 보고 + 카탈로그 항목

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 4: 브랜치 마무리**

superpowers:finishing-a-development-branch 스킬로 머지와 푸시 여부를 사용자에게 묻는다(기존 관례: `--no-ff` 머지, 한국어 머지 커밋, 승인 후 origin 푸시, feat 브랜치 삭제).
