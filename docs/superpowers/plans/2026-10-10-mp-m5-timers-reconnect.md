# 멀티플레이 M5 — 턴 타이머 · AI 인계 · 재접속 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 랜덤 매칭 판이 한 사람 때문에 깨지지 않게 한다 — 30초 턴 타이머, 끊김·잠수 시 Hard 대리 AI 인계, 기기 식별자로 같은 자리 자동 복귀, 사람 0명이면 2분 뒤 방 종료.

**Architecture:** 원격 좌석(`RemoteAgent`)이 좌석 전용 대리 AI(`PacedPimcAgent`)를 품고, 사람이 없거나(끊김) 자리를 비웠거나(연속 2회 시간 초과) 시간이 넘으면 결정을 넘긴다. 엔진·`GameDriver`·순수 AI 좌석은 그대로다. 방(`Room`)은 이탈을 폴트가 아니라 "연결 끊김"으로 다루고, 같은 기기 식별자의 재접속을 그 좌석에 다시 붙인다(로비 + 마지막 상태 스냅샷). 매치메이커가 `기기 식별자 → 방` 목록으로 재접속을 라우팅한다. 클라이언트는 질문 번호(`AskId`)를 답에 실어 늦은 답을 막고, 끊기면 자동으로 재접속한다.

**Tech Stack:** .NET 10(서버, ASP.NET Core WebSocket) · .NET 9(core 테스트) · NUnit 4(dotnet) / NUnit 3.5(Unity) · Newtonsoft.Json · Unity 6000.3.17f1(R3 · R3.Unity `Observable.EveryUpdate` · UniTask).

**Spec:** `docs/superpowers/specs/2026-10-09-mp-m5-timers-reconnect-design.md`

## Global Constraints

- 턴 타이머 = 결정 한 번에 **30초**(큰 티츄·교환·차례·용 양도 동일). 서버 옵션 `--turn-seconds`(기본 30).
- 시간 초과 시 **그 한 수만** 대리 AI. **연속 2회** 초과 또는 연결 끊김 → "자리 비움", 대리 AI 가 **즉시** 둔다.
- 복귀 트리거 = 그 좌석의 아무 유효 액션 · `Resume` · 재접속. 복귀는 **다음 질문부터**(이미 대리 AI 가 둔 수는 되돌리지 않음).
- 대리 AI = 좌석 전용 `PacedPimcAgent`, 방 난이도(Hard), 매치 내내 재사용.
- 방에 연결된 사람 0명 → 판 멈춤(사람 좌석 차례에서 대기) → **120초**(`--empty-room-seconds`, 기본 120) 지나면 방 종료. 그 전에 돌아오면 재개.
- M3 규칙 "한 명 나가면 그 방 종료"는 **삭제**. 방은 매치 종료 또는 사람 0명 120초로만 닫힌다.
- 프로토콜 `ProtocolVersion.Current = 3`. 접속 URI `?v=3&id=<기기 식별자>`, 식별자 = `Guid.ToString("N")`(32자 16진). 없거나 형식 오류면 업데이트 안내(`UpdateRequiredReason`)로 거절.
- `Awaiting.AskId`(long, 좌석별 증가), `Awaiting.SecondsLeft`(int?, 자리 비움·멈춤이면 null). `ClientAction.AskId`(long, 0 = 번호 없음). `ClientActionKind.Resume`. `PlayerView.Away`(bool[4]).
- 늦은 답 거부: 답의 `AskId` 가 0 이 아니고 지금 질문과 다르면 `"stale answer"`. 큰 티츄·교환은 이번 라운드에 이미 결정됐으면 `"already decided this round"`. 차례 밖 폭탄·작은 티츄는 `AskId` 0(검사 없음).
- 재접속 시 `Lobby`(내 좌석, 사람 수, 사람 수) + `TeamA`/`TeamB` = 매치 누적 점수(절대 좌석 기준), 이어서 그 좌석의 마지막 `StateUpdate` 스냅샷(`Applied` 없음). 재접속 순간 원격 좌석의 `ActionSeq` 기준을 0 으로.
- 같은 식별자의 옛 연결은 `"다른 곳에서 접속"` 사유로 닫는다(방에서도, 대기열에서도).
- 클라이언트 재접속: 1·2·4·8·8…초 간격, 총 **120초** 예산. 서버의 정상 종료(`MatchEnded`·서버 `SessionFaulted`)는 재접속 대상이 아니다.
- **계획이 정한 세부(스펙 공백 메우기):** 대리 AI 는 **큰 티츄·작은 티츄를 대신 부르지 않는다**(시간 초과 큰 티츄 = 패스, 작은 티츄는 사람이 예약한 것만). 교환·차례·용 양도는 대리 AI 가 둔다.
- `Tichu.Net`(core/src/Tichu.Net) 변경은 Unity 미러 `Assets/_Project/Net/` 에 그대로 복사, 바뀐/새 core 테스트도 `Assets/_Project/Tests/EditMode/` 에 복사(새 파일은 Unity 가 `.meta` 를 만든다 — 함께 커밋). 서버(`Tichu.Server`)는 미러 안 함.
- Unity 는 NUnit 3.5 — 미러 테스트에 NUnit 4 전용 API 금지. dotnet NUnit 4 는 `CollectionAssert`·`[Timeout]` 불가.
- ⚠️ Unity 에서 `Tichu.Core.Tests` 전체 실행 금지(10만 판 시뮬). 클래스 필터로만.
- ⚠️ 서버 프로세스가 떠 있으면 빌드 시 DLL 잠김 — 빌드 전 종료.
- 싱글플레이(로컬 루프백) 경로는 대리 AI 없음·무한 대기 그대로여야 한다(동작 불변).

## Review Focus

1. **잡아 둔 차례 프롬프트의 질문 번호** — 차례 중 작은 티츄 Yield 로 서버가 같은 질문을 새 `AskId` 로 다시 묻는데, 뷰모델은 깜빡임 방지로 옛 프롬프트 인스턴스를 유지한다. 그 인스턴스의 `AskId` 를 갱신하지 않으면 사람의 답이 `"stale answer"` 로 거부된다. → Task 7 `A_held_turn_prompt_takes_the_new_ask_id`.
2. **재접속 직후 첫 액션** — 새로 접속한 앱의 `ActionSeq` 는 1 부터. 서버가 기준을 초기화하지 않으면 모든 액션이 "duplicate" 로 거부된다. → Task 2 `ResetActionSeq_lets_a_new_client_start_from_one`, Task 4 `Rejoin_restores_the_seat_with_score_and_snapshot`(재접속 뒤 판이 이어짐).
3. **차례를 기다리는 중의 끊김** — 30초를 기다리지 않고 즉시 대리 AI 가 둬야 한다. → Task 2 `Disconnect_while_waiting_hands_the_turn_to_the_stand_in_at_once`.
4. **혼자 하던 사람이 잠깐 끊김** — 판이 AI 끼리 끝나 버리면 안 되고(멈춤), 돌아오면 같은 자리에서 이어진다. → Task 3 `Session_pauses_with_no_connected_seat_and_resumes_on_return`, Task 6 `A_dropped_solo_player_rejoins_the_same_seat_and_finishes`.
5. **같은 기기 이중 접속** — 옛 연결은 닫히고 새 연결이 좌석을 갖는다. 옛 연결의 늦은 끊김이 좌석을 "끊김"으로 되돌리면 안 된다. → Task 4 `Duplicate_rejoin_closes_the_old_link_and_ignores_its_late_leave`.

---

## 파일 구조

| 파일 | 책임 | 상태 |
|---|---|---|
| `core/src/Tichu.Net/ProtocolVersion.cs` | v3, `DeviceIdKey`, `Attach(uri, id)`, `IsValidDeviceId` | 수정(+미러) |
| `core/src/Tichu.Net/Awaiting.cs` | `AskId`, `SecondsLeft` | 수정(+미러) |
| `core/src/Tichu.Net/Protocol/ClientAction.cs` | `AskId`, `Resume` 종류·팩토리 | 수정(+미러) |
| `core/src/Tichu.Net/PlayerView.cs` | `Away[4]` | 수정(+미러) |
| `core/src/Tichu.Net/SeatRotation.cs` | `Away` 회전, `Awaiting` 새 필드·`ClientAction.AskId` 복사 | 수정(+미러) |
| `core/src/Tichu.Net/WebSocketClientTransport.cs` | `ConnectAsync(uri, deviceId, …)`, `ConnectionLost` | 수정(+미러) |
| `core/src/Tichu.Net/RemoteAgent.cs` | 연결·자리 비움·타이머·대리 AI·질문 번호·늦은 답 | 수정(+미러) |
| `core/src/Tichu.Net/MatchSession.cs` | 대리 AI 공급, 좌석 연결 상태, 순번 초기화, 점수 노출, 뷰 `Away` | 수정(+미러) |
| `core/src/Tichu.Server/Matchmaking/PlayerLink.cs` | `IPlayerLink.DeviceId`, `IRoom.DeviceIds/Rejoin/Tick` | 수정 |
| `core/src/Tichu.Server/Matchmaking/Room.cs` | 끊김=AI 대행, 재접속, 스냅샷 캐시, 빈 방 종료 | 수정 |
| `core/src/Tichu.Server/Matchmaking/Matchmaker.cs` | `id → 방` 라우팅, 대기열 중복 교체, 방 틱 | 수정 |
| `core/src/Tichu.Server/Connection.cs` · `ServerApp.cs` · `ServerOptions.cs` · `Program.cs` | 식별자 관문·전달, `--turn-seconds`·`--empty-room-seconds` | 수정 |
| `tools/smoke-server.ps1` | `&id=` 첨부 | 수정 |
| `Assets/_Project/Presentation/Net/DeviceIdentity.cs` | 기기 식별자 생성·보관(순수) | 신규 |
| `Assets/_Project/Presentation/Net/ReconnectPolicy.cs` | 재접속 간격·예산(순수) | 신규 |
| `Assets/_Project/Presentation/Net/LobbyWaiter.cs` | 로비의 누적 점수 | 수정 |
| `Assets/_Project/Presentation/ViewModel/TableViewModel.cs` | `AskId` 첨부, 남은 초, 자리 비움, `Resume`, 점수 세팅, 재접속 초기화 | 수정 |
| `Assets/_Project/Presentation/Views/RuntimeTableView.cs` | 타이머 문구, 자리 비움 배너, "AI 대행" | 수정 |
| `Assets/_Project/Presentation/RoundBootstrap.cs` | 기기 식별자 접속, 자동 재접속 루프 | 수정 |

### 테스트 실행 명령(모든 태스크 공통)

- core: `dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~<클래스>" --nologo` (core 전체 dotnet 실행은 허용)
- server: `dotnet test core/tests/Tichu.Server.Tests/Tichu.Server.Tests.csproj --nologo` (필터 `--filter "FullyQualifiedName~<클래스>"`)
- Unity(에디터 열림, MCP raw HTTP): 헬퍼 `mcp.py`·`unity_tests.py`(SDD 작업 공간에 복사해 둔다. 원본: `%TEMP%\claude\C--Users-user-Desktop-Project-Tichu\94c820c5-fea3-4a03-8906-29369accd906\scratchpad\`). `PYTHONIOENCODING=utf-8` 로 실행.
  1. `python mcp.py refresh_unity '{"mode":"force","scope":"all","compile":"request","wait_for_ready":true}'` → `sleep 20`
  2. `python mcp.py read_console '{"action":"get","types":["error"],"count":10}'` → `error CS` 없음
  3. `python unity_tests.py names <정규화 전체이름…>` / `python unity_tests.py assembly Tichu.Presentation.Tests` → 0 = 통과(1 실패, 2 필터 미스, 3 이상)
- Unity(에디터 닫힘): `E:\6000.3.17f1\Editor\Unity.exe -batchmode -runTests -projectPath C:\Users\user\Desktop\Project\Tichu -testPlatform EditMode -testFilter <접두어;접두어> -testResults %TEMP%\r.xml -logFile %TEMP%\r.log` (결과 XML 의 `test-run` total/passed/failed).
- 미러 확인: `diff -rq --strip-trailing-cr core/src/Tichu.Net Assets/_Project/Net -x "*.meta" -x bin -x obj -x "*.csproj" -x "*.asmdef"` → 출력 없음.

---

### Task 1: 프로토콜 v3 — 식별자 · 질문 번호 · 자리 비움 · Resume

**Files:**
- Modify: `core/src/Tichu.Net/ProtocolVersion.cs`, `Awaiting.cs`, `Protocol/ClientAction.cs`, `PlayerView.cs`, `SeatRotation.cs`, `WebSocketClientTransport.cs`
- Test: `core/tests/Tichu.Core.Tests/ProtocolVersionTests.cs`, `ProtocolJsonTests.cs`, `SeatRotationTests.cs`
- Mirror: 위 소스 6개 → `Assets/_Project/Net/…`, 테스트 3개 → `Assets/_Project/Tests/EditMode/…`
- Modify: `core/tests/Tichu.Server.Tests/ConnectionTests.cs`(버전 케이스 `?v=2` 추가 — 아래 Step 6)

**Interfaces:**
- Produces: `ProtocolVersion.Current == 3`; `ProtocolVersion.DeviceIdKey == "id"`; `static Uri ProtocolVersion.Attach(Uri uri, string deviceId)`; `static bool ProtocolVersion.IsValidDeviceId(string? value)`; `Awaiting.AskId:long`, `Awaiting.SecondsLeft:int?`; `ClientAction.AskId:long`; `ClientActionKind.Resume`; `static ClientAction ClientAction.Resume(int seat, long actionSeq)`; `PlayerView.Away:bool[4]`; `WebSocketClientTransport.ConnectAsync(Uri uri, string deviceId, TimeSpan timeout, CancellationToken ct)`; 기존 `ConnectAsync(Uri, TimeSpan, CancellationToken)` 는 매번 새 무작위 식별자로 위임(테스트·구 호출부 호환); `bool WebSocketClientTransport.ConnectionLost`(서버의 정상 종료가 아닌 끊김을 보고했으면 true).
- 서버는 이 태스크에서 아직 `id` 를 검사하지 않는다(Task 5). `v=3` 만 맞으면 받는다.

- [ ] **Step 1: 실패하는 테스트** — `ProtocolVersionTests.cs`: 기존 `Matchmaking_protocol_is_version_2` 를 다음으로 교체하고 테스트 3개 추가:

```csharp
        [Test]
        public void Reconnect_protocol_is_version_3()
        {
            // M5 가 질문 번호·자리 비움·Resume·기기 식별자를 더했다 — v2 앱은 업데이트 안내를 받아야 한다.
            Assert.That(ProtocolVersion.Current, Is.EqualTo(3));
            Assert.That(ProtocolVersion.Accepts("2"), Is.False);
        }

        [Test]
        public void Attach_with_device_id_adds_both_queries()
        {
            var id = "0123456789abcdef0123456789abcdef";
            var u = ProtocolVersion.Attach(new Uri("ws://192.168.0.5:7777/ws"), id);
            Assert.That(u.ToString(), Is.EqualTo("ws://192.168.0.5:7777/ws?v=3&id=" + id));
        }

        [TestCase("0123456789abcdef0123456789abcdef", true)]
        [TestCase("0123456789ABCDEF0123456789ABCDEF", true)]
        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("0123456789abcdef", false)]
        [TestCase("01234567-89ab-cdef-0123-456789abcdef", false)]   // "N" 형식만
        [TestCase("zz23456789abcdef0123456789abcdef", false)]
        public void Device_id_must_be_a_32_hex_guid(string? id, bool ok)
        {
            Assert.That(ProtocolVersion.IsValidDeviceId(id), Is.EqualTo(ok));
        }
```

`ProtocolJsonTests.cs` — 서버 이벤트 왕복 테스트 끝(Queued 단언 뒤)에:

```csharp
            var view = new PlayerView { Viewer = 1 };
            view.Away[2] = true;
            var ask = ServerEvent.StateUpdate(view, new Awaiting { Kind = StepKind.Play, Seat = 1, AskId = 7, SecondsLeft = 30 });
            var ask2 = RoundTrip(ask);
            Assert.That(ask2.View!.Away, Is.EqualTo(new[] { false, false, true, false }));
            Assert.That(ask2.View.Awaiting!.AskId, Is.EqualTo(7));
            Assert.That(ask2.View.Awaiting.SecondsLeft, Is.EqualTo(30));
```

클라이언트 액션 왕복 테스트(같은 파일의 `ClientAction` 직렬화 테스트) 끝에:

```csharp
            var resume = ProtocolJson.DeserializeClientAction(ProtocolJson.Serialize(ClientAction.Resume(2, 9)));
            Assert.That(resume.Kind, Is.EqualTo(ClientActionKind.Resume));
            Assert.That(resume.ActionSeq, Is.EqualTo(9));
            var answered = ClientAction.Pass(0, 3);
            answered.AskId = 41;
            Assert.That(ProtocolJson.DeserializeClientAction(ProtocolJson.Serialize(answered)).AskId, Is.EqualTo(41));
```

(파일에 `using Tichu.GameFlow;` 가 없으면 추가 — `StepKind` 네임스페이스. 클라이언트 액션 왕복 테스트가 따로 없으면 위 블록을 새 `[Test] public void Resume_and_ask_id_round_trip()` 로 만든다.)

`SeatRotationTests.cs` 에 추가:

```csharp
        [Test]
        public void Away_flags_and_ask_fields_follow_rotation()
        {
            var v = new PlayerView { Viewer = 1 };
            v.Away[2] = true;   // 절대 좌석 2 = 좌석 1 에게는 오른쪽(로컬 1)
            v.Awaiting = new Awaiting { Kind = StepKind.Play, Seat = 1, AskId = 5, SecondsLeft = 12 };
            var local = SeatRotation.ToLocal(ServerEvent.StateUpdate(v, v.Awaiting), 1).View!;
            Assert.That(local.Away, Is.EqualTo(new[] { false, true, false, false }));
            Assert.That(local.Awaiting!.AskId, Is.EqualTo(5));
            Assert.That(local.Awaiting.SecondsLeft, Is.EqualTo(12));
        }

        [Test]
        public void Ask_id_survives_rotation_to_the_server()
        {
            var a = ClientAction.Pass(0, 1);
            a.AskId = 99;
            Assert.That(SeatRotation.ToServer(a, 3).AskId, Is.EqualTo(99));
        }
```

- [ ] **Step 2: 실패 확인** — `dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~ProtocolVersionTests|FullyQualifiedName~ProtocolJsonTests|FullyQualifiedName~SeatRotationTests" --nologo` → 컴파일 실패(`Attach(uri,id)`·`AskId`·`Away`·`Resume` 없음).

- [ ] **Step 3: 구현**

`ProtocolVersion.cs`: `Current = 3`, 클래스에 추가:

```csharp
        /// <summary>접속 URI 의 기기 식별자 쿼리 키(M5). 같은 기기의 재접속을 그 좌석에 다시 붙이는 데 쓴다.</summary>
        public const string DeviceIdKey = "id";

        /// <summary>v=Current 와 id=deviceId 를 붙인다.</summary>
        public static Uri Attach(Uri uri, string deviceId) =>
            new Uri(Attach(uri).ToString() + "&" + DeviceIdKey + "=" + Uri.EscapeDataString(deviceId));

        /// <summary>서버: 기기 식별자 형식 — Guid "N"(32자 16진). 그 밖은 전부 false.</summary>
        public static bool IsValidDeviceId(string? value) =>
            value != null && value.Length == 32 && Guid.TryParseExact(value, "N", out _);
```

`Awaiting.cs` — `DeadlineUtc` 아래에:

```csharp
        /// <summary>그 좌석에 묻는 질문의 일련번호(좌석별 증가). 답은 이 번호를 싣는다 — 늦은 답 거부(M5 §3.1).</summary>
        public long AskId { get; set; }

        /// <summary>이 질문의 남은 초(턴 타이머). 시계 차이를 피하려 절대 시각 대신 쓴다. 타이머가 없으면 null.</summary>
        public int? SecondsLeft { get; set; }
```

`Redacted()` 는 그대로(다른 좌석엔 번호·남은 초가 필요 없다).

`ClientAction.cs` — 열거형 끝에 `GiveDragon,` 뒤로 `Resume` 추가(마지막 항목이 되도록 쉼표 정리), 속성 `RecipientSeat` 아래에:

```csharp
        /// <summary>답하는 질문의 번호(Awaiting.AskId). 0 = 번호 없음(차례 밖 폭탄·작은 티츄·미리 낸 큰 티츄/교환).</summary>
        public long AskId { get; set; }
```

팩토리 끝에:

```csharp
        /// <summary>자리 비움에서 돌아가기(M5). 서버는 좌석을 사람 있음으로 바꾸고 아무것도 적용하지 않는다.</summary>
        public static ClientAction Resume(int seat, long actionSeq) =>
            new ClientAction { Kind = ClientActionKind.Resume, Seat = seat, ActionSeq = actionSeq };
```

`PlayerView.cs` — `IsOut` 근처에:

```csharp
        /// <summary>좌석별 자리 비움(AI 대행) 여부(M5).</summary>
        public bool[] Away { get; set; } = new bool[4];
```

`SeatRotation.cs` — `RotateView` 객체 초기화에 `IsOut = Permute(v.IsOut, my),` 뒤로 `Away = Permute(v.Away, my),`, `Awaiting` 초기화에 `CanPass = v.Awaiting.CanPass,` 뒤로 `AskId = v.Awaiting.AskId,` `SecondsLeft = v.Awaiting.SecondsLeft,`. `ToServer` 초기화에 `ActionSeq = a.ActionSeq,` 뒤로 `AskId = a.AskId,`.

`WebSocketClientTransport.cs`:
- 필드 `private int _lost;` 추가, 속성 `/// <summary>서버의 정상 종료(MatchEnded·서버 SessionFaulted)가 아닌 끊김을 보고했다 — 재접속 대상.</summary> public bool ConnectionLost => Volatile.Read(ref _lost) == 1;`
- `ReportLostIfUnexpected` 에서 `_inbox.Enqueue(...)` 바로 앞에 `Interlocked.Exchange(ref _lost, 1);`
- `ConnectAsync` 를 다음 두 개로:

```csharp
        /// <summary>접속한다. URI 에 프로토콜 버전(v)과 기기 식별자(id)를 붙인다. 시간 초과·거절·주소 오류는 예외로 올라온다.</summary>
        public static async Task<WebSocketClientTransport> ConnectAsync(Uri uri, string deviceId, TimeSpan timeout, CancellationToken ct)
        {
            var ws = new ClientWebSocket();
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                linked.CancelAfter(timeout);
                try { await ws.ConnectAsync(ProtocolVersion.Attach(uri, deviceId), linked.Token).ConfigureAwait(false); }
                catch { ws.Dispose(); throw; }
            }
            return new WebSocketClientTransport(ws);
        }

        /// <summary>식별자를 신경 쓰지 않는 호출부용 — 매번 새 기기로 접속한다(재접속 불가).</summary>
        public static Task<WebSocketClientTransport> ConnectAsync(Uri uri, TimeSpan timeout, CancellationToken ct) =>
            ConnectAsync(uri, Guid.NewGuid().ToString("N"), timeout, ct);
```

- [ ] **Step 4: 통과 확인** — Step 2 명령 → PASS. core 전체 `dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --nologo` → 실패 0.

- [ ] **Step 5: 미러 복사·Unity 확인** — 소스 6개·테스트 3개를 미러 경로에 `cp`, 미러 diff 명령 → 출력 없음. Unity 컴파일 후 `python unity_tests.py names Tichu.Core.Tests.ProtocolJsonTests Tichu.Core.Tests.ProtocolVersionTests Tichu.Core.Tests.SeatRotationTests` → 0. `python unity_tests.py assembly Tichu.Presentation.Tests` → 0(호출부 호환 확인).

- [ ] **Step 6: 서버 테스트 정리** — `core/tests/Tichu.Server.Tests/ConnectionTests.cs` 의 `Missing_wrong_or_garbage_version…` 케이스에 `[TestCase("?v=2")]` 추가(구 v2 앱). `dotnet test core/tests/Tichu.Server.Tests/Tichu.Server.Tests.csproj --nologo` → 전부 PASS(서버는 아직 id 를 보지 않으므로 기존 테스트 그대로 통과해야 한다).

- [ ] **Step 7: 커밋**

```bash
git add core/src/Tichu.Net core/tests/Tichu.Core.Tests core/tests/Tichu.Server.Tests/ConnectionTests.cs Assets/_Project/Net Assets/_Project/Tests/EditMode
git commit -m "feat(net): 프로토콜 v3 — 기기 식별자·질문 번호·남은 초·자리 비움·Resume"
```

---

### Task 2: 원격 좌석 — 연결·자리 비움·턴 타이머·대리 AI·늦은 답

**Files:**
- Modify: `core/src/Tichu.Net/RemoteAgent.cs`
- Test: `core/tests/Tichu.Core.Tests/RemoteAgentTests.cs`
- Mirror: 둘 다

**Interfaces:**
- Consumes(Task 1): `ClientAction.AskId`, `ClientActionKind.Resume`, `ClientAction.Resume`, `Awaiting.AskId/SecondsLeft`.
- Produces: 생성자에 선택 인자 4개를 **끝에** 추가(기존 호출부 그대로 컴파일): `IAgent? standIn = null, Func<bool>? anyConnected = null, Action<string>? log = null, int maxTimeouts = 2`. 공개 멤버: `bool IsConnected`, `bool IsAway`(= 끊김 또는 연속 시간 초과), `void SetConnected(bool connected)`, `void WakeIfPaused()`, `void ResetActionSeq()`.
- 동작 계약: 대리 AI 가 없으면(`standIn == null`) 기존과 똑같다 — 시간 초과는 `TimeoutException`, 연결 상태는 항상 연결됨. 로컬 싱글플레이·기존 테스트 불변.

- [ ] **Step 1: 실패하는 테스트** — `RemoteAgentTests.cs`. `Harness.Create` 에 인자 추가(기존 호출 호환):

```csharp
            public RemoteAgent Create(int seat, TimeSpan? timeout = null,
                Action? onTichuReserved = null, Func<bool>? anyTichuPending = null,
                IAgent? standIn = null, Func<bool>? anyConnected = null, int maxTimeouts = 2)
            {
                RemoteAgent agent = null!;
                agent = new RemoteAgent(
                    seat,
                    (s, ev) => { lock (Sent) Sent.Add(ev); },
                    (s, aw) => { lock (Awaits) Awaits.Add(aw); },
                    () => Interlocked.Increment(ref _arrival),
                    timeout ?? TimeSpan.FromSeconds(5),
                    Cts.Token,
                    onTichuReserved,
                    anyTichuPending ?? (Func<bool>)(() => agent.HasReservedTichu),
                    standIn,
                    anyConnected,
                    null,
                    maxTimeouts);
                return agent;
            }

            public Awaiting LastAwait
            {
                get { lock (Awaits) return Awaits[Awaits.Count - 1]; }
            }

            public bool Rejected(string reason)
            {
                lock (Sent) return Sent.Exists(e => e.Kind == ServerEventKind.ActionRejected && e.Reason == reason);
            }
```

파일 안에 대리 AI 스텁:

```csharp
        /// <summary>대리 AI 대역 — 불린 횟수만 센다. 결과의 합법성은 RemoteAgent 의 관심사가 아니다(드라이버가 검증).</summary>
        private sealed class StubAgent : IAgent
        {
            public int Turns, Exchanges, Grands, Dragons;
            public bool CallGrandTichu(in DecisionContext ctx) { Grands++; return true; }
            public ExchangeChoice ChooseExchange(in DecisionContext ctx)
            {
                Exchanges++;
                var h = ctx.State.Seats[ctx.Seat].Hand;
                return new ExchangeChoice(h[0], h[1], h[2]);
            }
            public bool CallTichu(in DecisionContext ctx) => true;
            public TurnDecision DecideTurn(in DecisionContext ctx) { Turns++; return TurnDecision.Pass; }
            public Tichu.Core.Combinations.Combination? DecideBomb(in DecisionContext ctx) => null;
            public int ChooseDragonRecipient(in DecisionContext ctx) { Dragons++; return (ctx.Seat + 1) % 4; }
        }

        private static ClientAction PlayFive(long seq, long askId = 0)
        {
            var a = ClientAction.Play(0, seq, new List<Card> { Card.Normal(5, Suit.Jade) }, rank: 0, wish: null);
            a.AskId = askId;
            return a;
        }

        private static void WaitUntil(Func<bool> cond, int ms = 3000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!cond())
            {
                if (sw.ElapsedMilliseconds > ms) Assert.Fail("조건이 성립하지 않았다");
                Thread.Sleep(5);
            }
        }
```

테스트(파일 끝에 추가):

```csharp
        [Test]
        public void Timeout_lets_the_stand_in_play_that_turn_only()
        {
            var h = new Harness();
            var stub = new StubAgent();
            var agent = h.Create(0, TimeSpan.FromMilliseconds(80), standIn: stub);

            var d = agent.DecideTurn(LeadContext(out _));

            Assert.That(d.IsPass, Is.True, "대리 AI 의 결정이 돌아와야 한다");
            Assert.That(stub.Turns, Is.EqualTo(1));
            Assert.That(agent.IsAway, Is.False, "한 번 초과는 자리 비움이 아니다");
        }

        [Test]
        public void Two_timeouts_make_the_seat_away_and_the_next_question_skips_the_wait()
        {
            var h = new Harness();
            var stub = new StubAgent();
            var agent = h.Create(0, TimeSpan.FromMilliseconds(300), standIn: stub);
            var ctx = LeadContext(out _);

            agent.DecideTurn(ctx);
            agent.DecideTurn(ctx);
            Assert.That(agent.IsAway, Is.True);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            agent.DecideTurn(ctx);
            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(250), "자리 비움이면 기다리지 않는다");
            Assert.That(stub.Turns, Is.EqualTo(3));
            Assert.That(h.LastAwait.SecondsLeft, Is.Not.Null, "마지막으로 사람에게 물은 질문엔 타이머가 있었다");
        }

        [Test]
        public void Any_action_brings_an_away_seat_back()
        {
            var h = new Harness();
            var stub = new StubAgent();
            var agent = h.Create(0, TimeSpan.FromMilliseconds(80), standIn: stub);
            var ctx = LeadContext(out _);
            agent.DecideTurn(ctx);
            agent.DecideTurn(ctx);
            Assert.That(agent.IsAway, Is.True);

            agent.Submit(ClientAction.Resume(0, 1));
            Assert.That(agent.IsAway, Is.False);
            agent.Submit(PlayFive(2));                    // 차례 밖 → 차례가 오면 사람의 답

            var d = agent.DecideTurn(ctx);
            Assert.That(d.IsPass, Is.False);
            Assert.That(stub.Turns, Is.EqualTo(2), "복귀 뒤엔 대리 AI 를 부르지 않는다");
        }

        [Test]
        public void Answer_carrying_the_current_ask_id_is_accepted()
        {
            var h = new Harness();
            var agent = h.Create(0, TimeSpan.FromSeconds(5), standIn: new StubAgent());
            var ctx = LeadContext(out _);
            TurnDecision? got = null;
            var t = new Thread(() => got = agent.DecideTurn(ctx));
            t.Start();
            WaitUntil(() => h.AwaitCount == 1);

            var aw = h.LastAwait;
            Assert.That(aw.AskId, Is.GreaterThan(0));
            Assert.That(aw.SecondsLeft, Is.EqualTo(5));
            agent.Submit(PlayFive(1, aw.AskId));
            t.Join(3000);

            Assert.That(got!.Value.IsPass, Is.False);
            Assert.That(h.RejectCount, Is.EqualTo(0));
        }

        [Test]
        public void Late_answer_to_a_timed_out_question_is_rejected()
        {
            var h = new Harness();
            var agent = h.Create(0, TimeSpan.FromMilliseconds(80), standIn: new StubAgent());
            agent.DecideTurn(LeadContext(out _));          // 사람이 못 답해 대리 AI 가 둠
            long oldAsk = h.LastAwait.AskId;

            agent.Submit(PlayFive(1, oldAsk));             // 늦게 도착한 답

            Assert.That(h.Rejected("stale answer"), Is.True);
            Assert.That(agent.PeekReservedBomb(), Is.Null, "늦은 답이 차례 밖 큐에 남으면 다음 차례에 쓰인다");
        }

        [Test]
        public void Disconnect_while_waiting_hands_the_turn_to_the_stand_in_at_once()
        {
            var h = new Harness();
            var stub = new StubAgent();
            var agent = h.Create(0, TimeSpan.FromSeconds(10), standIn: stub, anyConnected: () => true);
            var ctx = LeadContext(out _);
            TurnDecision? got = null;
            var t = new Thread(() => got = agent.DecideTurn(ctx));
            t.Start();
            WaitUntil(() => h.AwaitCount == 1);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            agent.SetConnected(false);
            Assert.That(t.Join(2000), Is.True);

            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(1500));
            Assert.That(stub.Turns, Is.EqualTo(1));
            Assert.That(agent.IsAway, Is.True);
        }

        [Test]
        public void With_no_one_connected_the_seat_waits_until_it_returns()
        {
            var h = new Harness();
            var stub = new StubAgent();
            bool anyone = false;
            // 복귀 뒤 사람에게 다시 묻는 질문이 시간 초과되지 않도록 넉넉히(멈춤 대기 자체는 무기한).
            var agent = h.Create(0, TimeSpan.FromSeconds(2), standIn: stub, anyConnected: () => anyone);
            agent.SetConnected(false);
            var ctx = LeadContext(out _);
            TurnDecision? got = null;
            var t = new Thread(() => got = agent.DecideTurn(ctx));
            t.Start();
            WaitUntil(() => h.AwaitCount == 1);

            Assert.That(h.LastAwait.SecondsLeft, Is.Null, "멈춤 중엔 타이머가 없다");
            Assert.That(t.Join(400), Is.False, "아무도 없으면 대리 AI 로 넘기지 않고 기다린다");

            anyone = true;
            agent.SetConnected(true);                      // 재접속 → 사람에게 다시 묻는다
            WaitUntil(() => h.AwaitCount == 2);
            agent.Submit(PlayFive(1, h.LastAwait.AskId));
            Assert.That(t.Join(2000), Is.True);

            Assert.That(got!.Value.IsPass, Is.False, "돌아온 사람이 둔다");
            Assert.That(stub.Turns, Is.EqualTo(0));
        }

        [Test]
        public void Grand_tichu_on_timeout_is_declined_and_a_late_answer_is_rejected()
        {
            var h = new Harness();
            var stub = new StubAgent();
            var agent = h.Create(0, TimeSpan.FromMilliseconds(80), standIn: stub);
            var s = GameEngine.NewRound(777UL);

            bool called = agent.CallGrandTichu(GameFlowHelpers.Context(s, 0));

            Assert.That(called, Is.False, "대리 AI 는 큰 티츄를 대신 부르지 않는다");
            Assert.That(stub.Grands, Is.EqualTo(0));
            agent.Submit(ClientAction.CallGrandTichu(0, 1));
            Assert.That(h.Rejected("already decided this round"), Is.True);
        }

        [Test]
        public void ResetActionSeq_lets_a_new_client_start_from_one()
        {
            var h = new Harness();
            var agent = h.Create(0);
            agent.Submit(ClientAction.Pass(0, 5));

            agent.ResetActionSeq();
            agent.Submit(ClientAction.Pass(0, 1));

            Assert.That(h.Rejected("duplicate or out-of-order actionSeq"), Is.False);
        }

        [Test]
        public void Without_a_stand_in_a_disconnected_seat_still_waits_for_its_human()
        {
            // 로컬 싱글플레이·기존 경로: 대리 AI 가 없으면 연결 상태가 바뀌어도 동작이 그대로다.
            var h = new Harness();
            var agent = h.Create(0, TimeSpan.FromMilliseconds(80));
            Assert.Throws<TimeoutException>(() => agent.DecideTurn(LeadContext(out _)));
        }
```

- [ ] **Step 2: 실패 확인** — `dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~RemoteAgentTests" --nologo` → 컴파일 실패(생성자 인자·`SetConnected` 등 없음).

- [ ] **Step 3: 구현** — `RemoteAgent.cs`.

(a) 클래스 주석 끝에 한 문단 추가:

```csharp
    /// M5: 대리 AI 를 받으면 사람이 없을 때 결정을 넘긴다. 연결이 끊기거나(SetConnected(false))
    /// 연속 maxTimeouts 번 시간 초과면 "자리 비움" — 묻자마자 대리 AI 가 둔다. 그 좌석의 아무
    /// 액션·Resume·재접속이 복귀다(다음 질문부터). 방에 연결된 원격 좌석이 하나도 없으면(anyConnected
    /// == false) 대리 AI 로 넘기지 않고 무기한 기다린다 — 판을 멈추는 쪽은 방이고 종료도 방이 한다.
    /// 질문마다 AskId 를 붙이고, 지난 질문을 향한 늦은 답은 거부한다. 대리 AI 는 큰 티츄·작은 티츄를
    /// 대신 부르지 않는다. 대리 AI 가 없으면 M4 이전과 동작이 같다.
```

(b) 필드 추가(`_anyTichuPending` 선언 아래):

```csharp
        private readonly IAgent? _standIn;
        private readonly Func<bool>? _anyConnected;
        private readonly Action<string>? _log;
        private readonly int _maxTimeouts;
        private bool _connected = true;   // _gate — 방이 SetConnected 로 바꾼다
        private bool _afk;                // _gate — 연속 시간 초과로 자리 비움
        private int _timeouts;            // _gate — 연속 시간 초과 횟수
        private long _askCounter;         // _gate
        private long _currentAsk;         // _gate — 지금 사람에게 묻는 질문(0 = 없음)
        private bool _paused;             // _gate — 아무도 연결돼 있지 않아 무기한 대기 중
        private bool _grandDecided;       // _gate — 이번 라운드 결정 끝
        private bool _exchangeDecided;    // _gate
```

(c) 생성자 시그니처 끝에 `, IAgent? standIn = null, Func<bool>? anyConnected = null, Action<string>? log = null, int maxTimeouts = 2` 추가, 본문 끝에:

```csharp
            _standIn = standIn;
            _anyConnected = anyConnected;
            _log = log;
            _maxTimeouts = maxTimeouts;
```

(d) `Submit` 의 `else` 블록(시퀀스 통과 뒤)을 다음으로 교체:

```csharp
                else
                {
                    _lastSeq = a.ActionSeq;

                    // 그 좌석이 무언가 보냈다 = 자리에 있다. 자리 비움이었으면 대기를 깨워 다시 보게 한다
                    // (멈춤 대기 중이면 사람에게 다시 묻는다).
                    bool wasAfk = _afk;
                    _afk = false;
                    _timeouts = 0;
                    if (wasAfk) WakeLocked();

                    if (a.Kind == ClientActionKind.Resume)
                    {
                        // 복귀 표시만 — 적용할 것이 없다.
                    }
                    else if (_awaiting.HasValue && Matches(_awaiting.Value, a.Kind))
                    {
                        if (a.AskId != 0 && a.AskId != _currentAsk) rejectReason = "stale answer";
                        else if (DecidedLocked(a.Kind)) rejectReason = "already decided this round";
                        else
                        {
                            // 큐로 받는다. 거부 뒤 재제출처럼 한 결정에 여러 액션이 연달아 올 수 있고,
                            // 단일 슬롯이면 먼저 온 것이 덮어써져 거부 통보가 사라진다.
                            _matched.Enqueue(a);
                            _signal.Release();
                            return;
                        }
                    }
                    else if (a.Kind == ClientActionKind.CallTichu)
                    {
                        // (기존 CallTichu 분기 본문 그대로 — 라운드당 1회 예약)
                    }
                    else if (a.AskId != 0)
                    {
                        // 지금 묻지 않는 질문을 향한 번호 붙은 답 = 시간 초과로 대리 AI 가 이미 둔 질문의 늦은 답.
                        // 차례 밖 큐에 넣으면 다음 차례에 엉뚱하게 쓰인다.
                        rejectReason = "stale answer";
                    }
                    else if (DecidedLocked(a.Kind))
                    {
                        rejectReason = "already decided this round";
                    }
                    else if (a.Kind == ClientActionKind.Play)
                    {
                        // 차례밖 폭탄 후보. 자기 차례가 되면 Await 가 여기서 꺼내 쓴다.
                        _offTurnPlays.Enqueue(new OffTurnPlay(a, _nextArrival()));
                    }
                    else
                    {
                        _early.Add(a);
                    }
                }
```

(기존 CallTichu 분기 본문은 위치만 옮기고 내용은 바꾸지 않는다.)

(e) 새 공개 멤버(`WakeTurnForTichu` 아래):

```csharp
        /// <summary>방이 연결해 둔 사람이 있는가(끊기면 false).</summary>
        public bool IsConnected { get { lock (_gate) return _connected; } }

        /// <summary>자리 비움 — 끊겼거나 연속 시간 초과. 뷰의 Away 로 다른 좌석에 보인다.</summary>
        public bool IsAway { get { lock (_gate) return !_connected || _afk; } }

        /// <summary>방이 연결/끊김을 알린다. 대기 중이면 깨워 다시 판단하게 한다(끊김 → 즉시 대리 AI).</summary>
        public void SetConnected(bool connected)
        {
            lock (_gate)
            {
                _connected = connected;
                if (connected) { _afk = false; _timeouts = 0; }
                WakeLocked();
            }
        }

        /// <summary>아무도 없어 멈춰 기다리는 중이면 깨운다 — 다른 좌석의 사람이 돌아왔다.</summary>
        public void WakeIfPaused()
        {
            lock (_gate) { if (_paused) WakeLocked(); }
        }

        /// <summary>재접속: 새 앱은 ActionSeq 를 1 부터 센다.</summary>
        public void ResetActionSeq()
        {
            lock (_gate) _lastSeq = 0;
        }

        /// <summary>대기 중이면 Resume 표식으로 깨운다 — Await 가 상태를 다시 보고 다시 묻는다. _gate 안에서.</summary>
        private void WakeLocked()
        {
            if (!_awaiting.HasValue) return;
            _matched.Enqueue(ClientAction.Resume(_seat, 0L));
            _signal.Release();
        }

        private bool DecidedLocked(ClientActionKind k) =>
            (_grandDecided && (k == ClientActionKind.CallGrandTichu || k == ClientActionKind.DeclineGrandTichu))
            || (_exchangeDecided && k == ClientActionKind.Exchange);
```

(f) `ResetForNewRound`: `_matched` 배수 루프의 조건을 `if (m.Kind != ClientActionKind.CallTichu && m.Kind != ClientActionKind.Resume) discarded.Add(m);` 로, 그리고 `_tichuCalledThisRound = false;` 아래에 `_grandDecided = false; _exchangeDecided = false; _paused = false;`.

(g) `IAgent` 구현 교체:

```csharp
        /// <inheritdoc/>
        public bool CallGrandTichu(in DecisionContext ctx)
        {
            var a = Await(StepKind.GrandTichu, ctx.State, ctx.Seat);
            lock (_gate) _grandDecided = true;
            // 사람이 없으면 패스 — 대리 AI 가 큰 티츄를 대신 부르지 않는다(계획 Global Constraints).
            return a != null && a.Kind == ClientActionKind.CallGrandTichu;
        }

        /// <inheritdoc/>
        public ExchangeChoice ChooseExchange(in DecisionContext ctx)
        {
            var state = ctx.State;
            int seat = ctx.Seat;
            while (true)
            {
                var a = Await(StepKind.Exchange, state, seat);
                if (a == null)
                {
                    lock (_gate) _exchangeDecided = true;
                    return _standIn!.ChooseExchange(ctx);
                }
                if (!IsLegalExchange(state, seat, a)) { Reject(a, "exchange cards must be three distinct cards from hand"); continue; }
                lock (_gate) _exchangeDecided = true;
                return new ExchangeChoice(a.ToLeft, a.ToPartner, a.ToRight);
            }
        }
```

`DecideTurn` 의 `var a = Await(StepKind.Play, state, seat);` 바로 아래에 `if (a == null) return _standIn!.DecideTurn(ctx);`. `ChooseDragonRecipient` 의 `var a = Await(...)` 아래에 `if (a == null) return _standIn!.ChooseDragonRecipient(ctx);`. `CallTichu`(작은 티츄)는 그대로(`TakeReservedTichu()`).

(h) `Await` 를 다음으로 교체(반환형 `ClientAction?` — null = 대리 AI 가 둘 차례), `BuildAwaiting` 에 번호·남은 초 인자 추가:

```csharp
        /// <summary>
        /// 해당 종류의 액션이 올 때까지 기다린다. null 이면 사람이 없다 — 호출자가 대리 AI 에게 넘긴다.
        /// 사람 있음: 질문 번호·남은 초를 붙여 묻고 _timeout 까지 기다린다(초과 → null, 연속 _maxTimeouts 번이면 자리 비움).
        /// 자리 비움: 묻지 않고 바로 null. 단 방에 연결된 사람이 아무도 없으면 멈춰 무기한 기다린다.
        /// Resume 표식(연결·복귀 변화)으로 깨면 상태를 다시 보고 처음부터 다시 묻는다.
        /// </summary>
        private ClientAction? Await(StepKind kind, GameState state, int seat)
        {
            while (true)
            {
                // 다른 좌석의 잠금을 잡는 콜백이라 _gate 밖에서 부른다(잠금 중첩 금지).
                bool anyConnected = _standIn == null || (_anyConnected?.Invoke() ?? true);
                bool human, paused;
                long ask;
                lock (_gate)
                {
                    human = _standIn == null || (_connected && !_afk);
                    paused = !human && !anyConnected;
                    ask = ++_askCounter;
                    _currentAsk = human ? ask : 0;
                }
                if (!human && !paused) return null;

                int? secondsLeft = human && _timeout != Timeout.InfiniteTimeSpan
                    ? (int)Math.Ceiling(_timeout.TotalSeconds) : (int?)null;
                // 버퍼에 답이 이미 있든 없든 **항상 먼저 알린다** — "지금 누구에게 무엇을 묻는지"의 유일한 신호다.
                _onAwait(_seat, BuildAwaiting(kind, state, seat, ask, secondsLeft));

                lock (_gate)
                {
                    // 이전 질문에서 남은 매칭 큐 항목은 이 질문의 답이 아니다(기존 주석 참고). 깨움 표식
                    // (CallTichu·Resume)은 예약과 별개라 버린다.
                    while (_matched.Count > 0)
                    {
                        var leftover = _matched.Dequeue();
                        _signal.Wait(0);
                        if (leftover.Kind != ClientActionKind.CallTichu && leftover.Kind != ClientActionKind.Resume)
                            _early.Add(leftover);
                    }

                    if (human)
                    {
                        for (int i = 0; i < _early.Count; i++)
                        {
                            if (!Matches(kind, _early[i].Kind)) continue;
                            var buffered = _early[i];
                            _early.RemoveAt(i);
                            _timeouts = 0;
                            return buffered;
                        }

                        if (kind == StepKind.Play && _offTurnPlays.Count > 0)
                        {
                            _timeouts = 0;
                            return _offTurnPlays.Dequeue().Action;
                        }
                    }

                    _awaiting = kind;
                    _paused = paused;
                }

                bool got = false;
                try
                {
                    // _awaiting 을 세운 **뒤에** 예약을 확인한다(기존 주석 참고).
                    if (human && kind == StepKind.Play && _anyTichuPending != null && _anyTichuPending())
                        WakeTurnForTichu();

                    got = _signal.Wait(human ? _timeout : Timeout.InfiniteTimeSpan, _ct);
                }
                finally
                {
                    lock (_gate) { _awaiting = null; _paused = false; }
                }

                if (!got)
                {
                    if (_standIn == null) throw new TimeoutException($"seat {_seat} did not act within {_timeout}");
                    bool nowAfk;
                    lock (_gate)
                    {
                        _timeouts++;
                        nowAfk = _timeouts >= _maxTimeouts;
                        if (nowAfk) _afk = true;
                        _currentAsk = 0;
                    }
                    _log?.Invoke(nowAfk
                        ? $"좌석 {_seat} 시간 초과 {_maxTimeouts}회 — 자리 비움, 대리 AI"
                        : $"좌석 {_seat} 시간 초과 — 대리 AI 가 한 수");
                    return null;
                }

                ClientAction a;
                lock (_gate) a = _matched.Dequeue();
                if (a.Kind == ClientActionKind.Resume) continue;   // 연결·복귀 변화 — 다시 판단
                lock (_gate) _timeouts = 0;
                return a;
            }
        }

        private Awaiting BuildAwaiting(StepKind kind, GameState state, int seat, long askId, int? secondsLeft)
        {
            // (기존 좌석 일치 검사 그대로)
            if (seat != _seat)
                throw new InvalidOperationException(
                    $"seat {_seat} 에이전트가 seat {seat} 으로 질의됐다 — 배선 오류");

            var aw = new Awaiting { Kind = kind, Seat = _seat, AskId = askId, SecondsLeft = secondsLeft };
            if (kind == StepKind.Play)
            {
                aw.LegalMoves = LegalMoveGenerator.LegalMoves(state, seat);
                aw.CanPass = LegalMoveGenerator.CanPass(state, seat);
            }
            return aw;
        }
```

- [ ] **Step 4: 통과 확인** — Step 2 명령 → 새 테스트 + 기존 `RemoteAgentTests` 전부 PASS. 그다음 `--filter "FullyQualifiedName~LoopbackMatchTests|FullyQualifiedName~MatchSessionOracleTests|FullyQualifiedName~LoopbackTransportTests|FullyQualifiedName~LoopbackClientTests"` → PASS(경로 불변 확인). core 전체 1회 → 실패 0.

- [ ] **Step 5: 음성 대조**(편집 → 지정 테스트만 FAIL → 원복):
  1. `Submit` 의 `if (a.AskId != 0 && a.AskId != _currentAsk) rejectReason = "stale answer";` 줄과 `else if (a.AskId != 0) rejectReason = "stale answer";` 분기 제거 → `Late_answer_to_a_timed_out_question_is_rejected` 실패.
  2. `SetConnected` 의 `WakeLocked();` 제거 → `Disconnect_while_waiting…` 실패.
  3. `Await` 의 `paused` 계산을 `paused = false` 로 → `With_no_one_connected…` 실패.
  4. `ResetActionSeq` 본문 비우기 → `ResetActionSeq_lets…` 실패.
  원복 후 전부 PASS.

- [ ] **Step 6: 미러·Unity** — 두 파일 미러 복사, diff 확인. Unity: `python unity_tests.py names Tichu.Core.Tests.RemoteAgentTests Tichu.Core.Tests.LoopbackMatchTests Tichu.Core.Tests.MatchSessionOracleTests` → 0.

- [ ] **Step 7: 커밋**

```bash
git add core/src/Tichu.Net/RemoteAgent.cs core/tests/Tichu.Core.Tests/RemoteAgentTests.cs Assets/_Project/Net/RemoteAgent.cs Assets/_Project/Tests/EditMode/RemoteAgentTests.cs
git commit -m "feat(net): 원격 좌석 — 턴 타이머·자리 비움·대리 AI 인계·늦은 답 거부"
```

---

### Task 3: 매치 세션 — 대리 AI 공급 · 좌석 연결 · 점수 · 뷰 Away

**Files:**
- Modify: `core/src/Tichu.Net/MatchSession.cs`
- Create: `core/tests/Tichu.Core.Tests/MatchSessionPresenceTests.cs`
- Mirror: 소스 + 새 테스트(→ `Assets/_Project/Tests/EditMode/MatchSessionPresenceTests.cs`, `.meta` 는 Unity 가 생성 — 함께 커밋)

**Interfaces:**
- Consumes(Task 2): `RemoteAgent` 새 생성자 인자, `SetConnected`, `WakeIfPaused`, `ResetActionSeq`, `IsConnected`, `IsAway`.
- Produces: 생성자 끝에 선택 인자 `Func<int, IAgent>? standInFor = null, Action<string>? log = null`. 공개: `void SetSeatConnected(int seat, bool connected)`, `void ResetSeatSequence(int seat)`, `bool IsSeatAway(int seat)`, `(int TeamA, int TeamB) MatchScore`. 모든 방송 뷰의 `Away[i]` = 원격 좌석 i 의 `IsAway`.

- [ ] **Step 1: 실패하는 테스트** — `MatchSessionPresenceTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Tichu.GameFlow.Agents;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    /// <summary>M5: 끊긴 좌석은 대리 AI 가 두고, 아무도 없으면 멈추며, 돌아오면 이어진다.</summary>
    public class MatchSessionPresenceTests
    {
        private static MatchSession Session(LoopbackTransport t, IAgent?[] seats, int target = 200) =>
            new MatchSession(seats, t, masterSeed: 42UL, targetScore: target,
                turnTimeout: TimeSpan.FromSeconds(30),
                standInFor: s => new ScriptedAgent());

        private static void PumpUntil(Func<bool> done, int ms, params ScriptedClient[] clients)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(ms);
            while (!done())
            {
                if (DateTime.UtcNow > deadline) Assert.Fail("시간 안에 끝나지 않았다");
                foreach (var c in clients) c.Pump();
                Thread.Sleep(1);
            }
        }

        [Test]
        public void Disconnected_seat_is_played_by_its_stand_in_and_the_match_finishes()
        {
            var t = new LoopbackTransport();
            var session = Session(t, new IAgent?[] { null, new ScriptedAgent(), null, new ScriptedAgent() });
            var c0 = new ScriptedClient(0, t);
            session.SetSeatConnected(2, false);               // 좌석 2 의 사람은 끝내 오지 않는다
            session.Start();

            PumpUntil(() => c0.MatchEnded, 30000, c0);

            Assert.That(c0.FaultReason, Is.Null);
            Assert.That(session.Join(TimeSpan.FromSeconds(5)), Is.True);
        }

        [Test]
        public void Away_flags_reach_every_view()
        {
            var t = new LoopbackTransport();
            var session = Session(t, new IAgent?[] { null, new ScriptedAgent(), null, new ScriptedAgent() });
            var c0 = new ScriptedClient(0, t);
            session.SetSeatConnected(2, false);
            session.Start();
            PumpUntil(() => c0.MatchEnded, 30000, c0);

            Assert.That(session.IsSeatAway(2), Is.True);
            Assert.That(session.IsSeatAway(0), Is.False);
            Assert.That(c0.LastAway, Is.Not.Null);
            Assert.That(c0.LastAway![2], Is.True, "다른 좌석의 화면에 AI 대행이 보여야 한다");
        }

        [Test]
        public void Match_score_is_readable_and_matches_the_end()
        {
            var t = new LoopbackTransport();
            var session = Session(t, new IAgent?[] { null, new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent() });
            var c0 = new ScriptedClient(0, t);
            session.Start();
            PumpUntil(() => c0.MatchEnded, 30000, c0);

            var (a, b) = session.MatchScore;
            Assert.That(a + b, Is.Not.EqualTo(0));
            Assert.That(Math.Max(a, b), Is.GreaterThanOrEqualTo(200));
        }

        [Test]
        public void Session_pauses_with_no_connected_seat_and_resumes_on_return()
        {
            var t = new LoopbackTransport();
            var session = Session(t, new IAgent?[] { null, new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent() });
            var c0 = new ScriptedClient(0, t);
            session.SetSeatConnected(0, false);               // 유일한 사람이 없다 → 멈춤
            session.Start();

            Thread.Sleep(500);
            var drained = t.Inbox(0);   // 소비하지 않고 본다
            Assert.That(session.Join(TimeSpan.Zero), Is.False, "아무도 없으면 판이 AI 끼리 끝나면 안 된다");
            Assert.That(drained.Count, Is.LessThan(10), "멈춘 동안 진행이 거의 없어야 한다(첫 질문 직전까지)");

            session.SetSeatConnected(0, true);
            PumpUntil(() => c0.MatchEnded, 30000, c0);
            Assert.That(c0.FaultReason, Is.Null);
        }
    }
}
```

`ScriptedClient.cs`(core 테스트, Unity 미러에도)에 마지막 Away 기록 추가 — `Pump()` 의 `var view = ev.View!;` 다음 줄에 `LastAway = view.Away;`, 속성 `/// <summary>마지막 StateUpdate 의 좌석별 자리 비움.</summary> public bool[]? LastAway { get; private set; }`.

(`LoopbackTransport.Inbox(int seat)` 는 소비하지 않는 조회다 — `ScriptedClient` 가 나중에 같은 이벤트를 받는다.)

- [ ] **Step 2: 실패 확인** — `dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~MatchSessionPresenceTests" --nologo` → 컴파일 실패.

- [ ] **Step 3: 구현** — `MatchSession.cs`:
- 생성자 시그니처 끝에 `, Func<int, IAgent>? standInFor = null, Action<string>? log = null`. 원격 좌석 생성을:

```csharp
                var remote = new RemoteAgent(
                    seat,
                    (s, ev) => _transport.SendTo(s, ev),
                    OnAwait,
                    NextArrival,
                    timeout,
                    _cts.Token,
                    WakeTurnsForTichu,
                    AnyTichuPending,
                    standInFor?.Invoke(seat),
                    AnyRemoteConnected,
                    log);
```

- 공개 멤버(`Submit` 위):

```csharp
        /// <summary>방이 좌석의 연결/끊김을 알린다. 돌아오면 멈춰 있던 좌석들을 깨운다.</summary>
        public void SetSeatConnected(int seat, bool connected)
        {
            var r = _remote[seat];
            if (r == null) return;
            r.SetConnected(connected);
            if (!connected) return;
            for (int s = 0; s < SeatCount; s++) _remote[s]?.WakeIfPaused();
        }

        /// <summary>재접속: 새 앱의 ActionSeq 를 1 부터 받는다.</summary>
        public void ResetSeatSequence(int seat) => _remote[seat]?.ResetActionSeq();

        public bool IsSeatAway(int seat) => _remote[seat]?.IsAway ?? false;

        /// <summary>매치 누적 점수(재접속 로비용). 세션 스레드가 라운드마다 갱신한다.</summary>
        public (int TeamA, int TeamB) MatchScore => (Volatile.Read(ref _teamA), Volatile.Read(ref _teamB));

        private bool AnyRemoteConnected()
        {
            for (int s = 0; s < SeatCount; s++)
                if (_remote[s] != null && _remote[s]!.IsConnected) return true;
            return false;
        }

        /// <summary>좌석 시야 + 자리 비움 표시.</summary>
        private PlayerView Project(GameState s, int seat)
        {
            var v = ViewProjector.ProjectFor(s, seat);
            for (int i = 0; i < SeatCount; i++) v.Away[i] = _remote[i]?.IsAway ?? false;
            return v;
        }
```

- `OnAwait`·`Broadcast` 의 `ViewProjector.ProjectFor(…, i)` / `(s, seat)` 를 `Project(…)` 로.
- `Run` 의 `_teamA += …; _teamB += …;` 를 `Interlocked.Add(ref _teamA, outcome.Result.TeamATotal); Interlocked.Add(ref _teamB, outcome.Result.TeamBTotal);` 로(다른 스레드가 읽는다). 이후 그 줄들의 `_teamA`/`_teamB` 읽기는 세션 스레드이므로 그대로.

- [ ] **Step 4: 통과 확인** — Step 2 명령 → 4개 PASS. `LoopbackMatchTests|MatchSessionOracleTests|RemoteAgentTests` 필터 → PASS. core 전체 1회 → 실패 0.

- [ ] **Step 5: 음성 대조** — `SetSeatConnected` 의 `WakeIfPaused` 루프 제거 → 이번 테스트들은 통과할 수 있으므로, 대신 `Project` 의 `v.Away[i] = …` 줄 제거 → `Away_flags_reach_every_view` 실패 확인, 원복.

- [ ] **Step 6: 미러·Unity** — `MatchSession.cs`·`ScriptedClient.cs`·새 테스트 미러 복사, diff 확인. Unity `refresh scope all` → `python unity_tests.py names Tichu.Core.Tests.MatchSessionPresenceTests Tichu.Core.Tests.LoopbackMatchTests Tichu.Core.Tests.RemoteAgentTests` → 0, `total` 이 새 테스트 4개를 포함하는지 확인.

- [ ] **Step 7: 커밋**

```bash
git add core/src/Tichu.Net/MatchSession.cs core/tests/Tichu.Core.Tests Assets/_Project/Net/MatchSession.cs Assets/_Project/Tests/EditMode
git commit -m "feat(net): 매치 세션 — 대리 AI 공급·좌석 연결 상태·누적 점수·뷰 자리 비움"
```

---

### Task 4: 방 — 끊김은 AI 대행, 재접속, 스냅샷, 빈 방 종료

**Files:**
- Modify: `core/src/Tichu.Server/Matchmaking/PlayerLink.cs`, `Room.cs`, `core/src/Tichu.Server/ServerOptions.cs`, `core/src/Tichu.Server/Connection.cs`(DeviceId 만), `core/src/Tichu.Server/ServerApp.cs`(Room 생성자 인자만), `core/src/Tichu.Server/Matchmaking/Matchmaker.cs`(`RoomClosed(IRoom)` 시그니처만)
- Test: `core/tests/Tichu.Server.Tests/RoomTests.cs`, `FakeLink.cs`, `MatchmakerTests.cs`(시그니처 따라가기), `ServerOptionsTests.cs`

**Interfaces:**
- Consumes(Task 3): `MatchSession(… standInFor, log)`, `SetSeatConnected`, `ResetSeatSequence`, `MatchScore`.
- Produces:
  - `IPlayerLink.DeviceId : string`(Connection 은 생성자 인자 `string deviceId = ""`, FakeLink 는 기본 새 GUID).
  - `IRoom` 에 `IReadOnlyList<string> DeviceIds { get; }`, `void Rejoin(IPlayerLink link)`, `void Tick(DateTime now)`.
  - `Room(int index, IReadOnlyList<IPlayerLink> humans, int[] seats, ServerOptions opt, Func<DateTime> now, Action<string> log, Action<Room> onClosed)`.
  - `Matchmaker.RoomClosed(IRoom room)`(이 태스크에선 시그니처만 바꾸고 기존 동작 유지 — Task 5 가 확장).
  - `ServerOptions.TurnSeconds`(기본 30, `--turn-seconds`, 최소 1), `ServerOptions.EmptyRoomSeconds`(기본 120, `--empty-room-seconds`, 최소 1).
  - 로그: `방 {i} 좌석 {s} 연결 끊김 — AI 대행` (+ 사람 0명이면 ` · 사람 0명, 멈춤`), `방 {i} 좌석 {s} 재접속 (연결 #{id})`, `방 {i} 사람 0명 {n}초 — 방 종료`, `방 {i} 종료`, 세션 로그 접두 `방 {i} `(시간 초과·자리 비움).

- [ ] **Step 1: 테스트 헬퍼·옵션 테스트** — `FakeLink.cs`: `public string DeviceId { get; init; } = Guid.NewGuid().ToString("N");` 추가(`using System;`). `FakeRoom` 에 새 멤버:

```csharp
        public List<IPlayerLink> Rejoined { get; } = new List<IPlayerLink>();
        public List<DateTime> Ticks { get; } = new List<DateTime>();
        public IReadOnlyList<string> DeviceIds => Humans.Select(h => h.DeviceId).ToList();
        public void Rejoin(IPlayerLink link) { lock (Rejoined) { Rejoined.Add(link); link.Owner = this; } }
        public void Tick(DateTime now) { lock (Ticks) Ticks.Add(now); }
```

`MatchmakerTests.cs`: `mm.RoomClosed()` 호출을 `mm.RoomClosed(_rooms[0])` 로(해당 테스트가 닫는 방). `ServerOptionsTests.cs`: `Defaults_match_the_spec` 에 `Assert.That(o.TurnSeconds, Is.EqualTo(30)); Assert.That(o.EmptyRoomSeconds, Is.EqualTo(120));`, `All_flags_parse` 인자에 `"--turn-seconds", "7", "--empty-room-seconds", "9"` + 단언, `Bad_arguments_throw` 에 `[TestCase("--turn-seconds", "0")]`, `[TestCase("--empty-room-seconds", "0")]`.

- [ ] **Step 2: 방 테스트 재작성** — `RoomTests.cs`. `Make` 헬퍼를 새 생성자로:

```csharp
        private DateTime _clock = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

        private (Room room, Func<int> closedCount, List<string> log) Make(ServerOptions opt, int[] seats, params FakeLink[] humans)
        {
            int closed = 0;
            var log = new List<string>();
            var room = new Room(_rooms.Count, humans, seats, opt, () => _clock,
                m => { lock (log) log.Add(m); }, _ => Interlocked.Increment(ref closed));
            _rooms.Add(room);
            _links.AddRange(humans);
            return (room, () => Volatile.Read(ref closed), log);
        }
```

`Fast()` 는 `TurnSeconds = 30, EmptyRoomSeconds = 120` 기본 그대로. **삭제**: `Leaving_mid_match_faults_only_this_room`, `Leave_before_start_faults_the_room_on_start`(M3 규칙 폐지). `TearDown` 은 그대로 두되, 각 방을 확실히 끝내도록 루프 뒤에 `foreach (var r in _rooms) r.Tick(DateTime.MaxValue.AddDays(-1));` 를 추가하고 `Fast()` 에서 `EmptyRoomSeconds` 를 1 로 바꾸지 않는다(Tick 이 큰 시각을 넘긴다). **추가**:

```csharp
        [Test]
        public async Task A_leaving_human_is_played_by_the_stand_in_and_the_room_continues()
        {
            var a = new FakeLink(); var b = new FakeLink();
            var (room, closed, log) = Make(Fast(target: 200), new[] { 0, 2 }, a, b);
            room.Start();
            var client = new ScriptedClient(0, a);
            await Until(() => { client.Pump(); return a.Received.Any(e => e.Kind == ServerEventKind.StateUpdate); });

            room.Left(b);
            await Until(() => { client.Pump(); return client.MatchEnded; }, 60);

            Assert.That(client.FaultReason, Is.Null, "한 명이 나가도 판은 깨지지 않는다");
            lock (log) Assert.That(log.Exists(m => m == "방 0 좌석 2 연결 끊김 — AI 대행"), Is.True);
            await Until(() => closed() == 1);
        }

        [Test]
        public async Task A_human_gone_before_start_is_played_by_the_stand_in()
        {
            var a = new FakeLink(); var b = new FakeLink();
            var (room, closed, _) = Make(Fast(target: 200), new[] { 0, 1 }, a, b);
            room.Left(b);
            room.Start();
            var client = new ScriptedClient(0, a);
            await Until(() => { client.Pump(); return client.MatchEnded; }, 60);
            Assert.That(client.FaultReason, Is.Null);
        }

        [Test]
        public async Task Rejoin_restores_the_seat_with_score_and_snapshot()
        {
            var a = new FakeLink();
            var (room, _, log) = Make(Fast(target: 1000, aiDelay: 20), new[] { 1 }, a);
            room.Start();
            await Until(() => a.Received.Any(e => e.Kind == ServerEventKind.StateUpdate));

            room.Left(a);                                    // 유일한 사람 → 멈춤
            var a2 = new FakeLink { DeviceId = a.DeviceId };
            room.Rejoin(a2);

            var first = a2.Received[0];
            Assert.That(first.Kind, Is.EqualTo(ServerEventKind.Lobby));
            Assert.That(first.YourSeat, Is.EqualTo(1));
            Assert.That(a2.Received[1].Kind, Is.EqualTo(ServerEventKind.StateUpdate), "로비 다음은 스냅샷");
            Assert.That(a2.Received[1].Applied, Is.Null, "스냅샷은 효과음·로그를 다시 일으키지 않는다");
            Assert.That(a2.Owner, Is.SameAs(room));
            lock (log) Assert.That(log.Exists(m => m.StartsWith("방 0 좌석 1 재접속")), Is.True);

            // 새 앱은 ActionSeq 1 부터 — 판이 이어져야 한다.
            var client = new ScriptedClient(1, a2);
            int before = a2.Received.Count;
            await Until(() => { client.Pump(); return a2.Received.Count > before + 20; }, 30);
            Assert.That(a2.Received.Any(e => e.Kind == ServerEventKind.ActionRejected && e.Reason!.Contains("actionSeq")), Is.False);
        }

        [Test]
        public async Task Duplicate_rejoin_closes_the_old_link_and_ignores_its_late_leave()
        {
            var a = new FakeLink(); var other = new FakeLink();
            var (room, _, log) = Make(Fast(target: 1000, aiDelay: 20), new[] { 0, 2 }, a, other);
            room.Start();
            await Until(() => a.Received.Any(e => e.Kind == ServerEventKind.StateUpdate));

            var a2 = new FakeLink { DeviceId = a.DeviceId };
            room.Rejoin(a2);
            room.Left(a);                                    // 옛 연결이 늦게 끊긴다

            Assert.That(a.ClosedReason, Is.EqualTo("다른 곳에서 접속"));
            lock (log) Assert.That(log.Exists(m => m.Contains("좌석 0 연결 끊김")), Is.False, "옛 연결의 끊김은 좌석에 닿지 않는다");
        }

        [Test]
        public async Task Empty_room_closes_after_the_grace_period()
        {
            var a = new FakeLink();
            var (room, closed, log) = Make(Fast(target: 1000, aiDelay: 20), new[] { 0 }, a);
            room.Start();
            await Until(() => a.Received.Any(e => e.Kind == ServerEventKind.StateUpdate));
            room.Left(a);

            room.Tick(_clock.AddSeconds(119));
            await Task.Delay(200);
            Assert.That(closed(), Is.EqualTo(0));

            room.Tick(_clock.AddSeconds(120));
            await Until(() => closed() == 1);
            lock (log) Assert.That(log.Exists(m => m == "방 0 사람 0명 120초 — 방 종료"), Is.True);
        }

        [Test]
        public async Task Returning_within_the_grace_period_keeps_the_room()
        {
            var a = new FakeLink();
            var (room, closed, _) = Make(Fast(target: 1000, aiDelay: 20), new[] { 0 }, a);
            room.Start();
            await Until(() => a.Received.Any(e => e.Kind == ServerEventKind.StateUpdate));
            room.Left(a);
            room.Rejoin(new FakeLink { DeviceId = a.DeviceId });

            room.Tick(_clock.AddSeconds(500));
            await Task.Delay(300);
            Assert.That(closed(), Is.EqualTo(0));
        }
```

(`Fast(int target = 1, int aiDelay = 0)` 시그니처는 기존 그대로.)

- [ ] **Step 3: 실패 확인** — `dotnet test core/tests/Tichu.Server.Tests/Tichu.Server.Tests.csproj --filter "FullyQualifiedName~RoomTests|FullyQualifiedName~ServerOptionsTests|FullyQualifiedName~MatchmakerTests" --nologo` → 컴파일 실패.

- [ ] **Step 4: 구현**

`PlayerLink.cs`: `IPlayerLink` 에 `/// <summary>기기 식별자(접속 쿼리 id). 재접속 라우팅 키. 없으면 "".</summary> string DeviceId { get; }`. `IRoom` 을:

```csharp
    public interface IRoom : ILinkOwner
    {
        /// <summary>AI 생성·Lobby 송신·세션 시작. 매치메이커 잠금 밖에서 한 번 불린다.</summary>
        void Start();

        /// <summary>이 방 사람 좌석들의 기기 식별자(재접속 라우팅용). 생성 후 불변.</summary>
        IReadOnlyList<string> DeviceIds { get; }

        /// <summary>같은 기기의 새 연결을 그 좌석에 다시 붙인다. 매치메이커 잠금 밖에서 불린다.</summary>
        void Rejoin(IPlayerLink link);

        /// <summary>서버 1초 틱 — 빈 방 유예 시간을 잰다.</summary>
        void Tick(DateTime now);
    }
```

(`using System; using System.Collections.Generic;` 추가.)

`Connection.cs`: 생성자에 `string deviceId = ""` 인자 추가, `public string DeviceId { get; }` 속성 대입.

`ServerOptions.cs`: 속성

```csharp
        /// <summary>결정 한 번의 시간 제한(초). 넘으면 그 한 수는 대리 AI(M5).</summary>
        public int TurnSeconds { get; set; } = 30;
        /// <summary>방에 연결된 사람이 0명인 채 이 초가 지나면 방을 닫는다(M5).</summary>
        public int EmptyRoomSeconds { get; set; } = 120;
```

switch 에 `case "--turn-seconds": o.TurnSeconds = ParseMin(key, value, 1); break;`, `case "--empty-room-seconds": o.EmptyRoomSeconds = ParseMin(key, value, 1); break;`. 클래스 주석의 CLI 목록에 두 옵션 추가.

`Room.cs` 전체를 다음으로 교체:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tichu.GameFlow.Agents;
using Tichu.Net;

namespace Tichu.Server
{
    /// <summary>
    /// 매치 하나(M3 §2.1, M5). 빈자리는 PIMC, 사람 좌석은 원격 좌석 — 사람이 끊기면 그 좌석의 대리 AI 가
    /// 둔다(판은 깨지지 않는다). 같은 기기가 다시 오면(Rejoin) 그 좌석에 다시 붙여 로비(+누적 점수)와
    /// 마지막 상태 스냅샷을 보낸다. 방에 연결된 사람이 0명인 채 EmptyRoomSeconds 가 지나면 닫는다.
    /// 링크 표·스냅샷 캐시·송신은 한 잠금(_gate) 안에서 — 재접속 스냅샷과 세션 방송의 순서가 뒤바뀌지 않는다.
    /// 잠금 순서: 방(_gate) → 원격 좌석. 세션 메서드는 가능하면 _gate 밖에서 부른다.
    /// </summary>
    public sealed class Room : IRoom, ITichuTransport
    {
        private const int SeatCount = 4;

        private readonly int _index;
        private readonly ServerOptions _opt;
        private readonly Func<DateTime> _now;
        private readonly Action<string> _log;
        private readonly Action<Room> _onClosed;
        private readonly int _humans;
        private readonly string?[] _deviceBySeat = new string?[SeatCount];       // 생성 후 불변
        private readonly IPlayerLink?[] _bySeat = new IPlayerLink?[SeatCount];   // _gate
        private readonly ServerEvent?[] _lastState = new ServerEvent?[SeatCount]; // _gate
        private readonly object _gate = new object();
        private Action<ClientAction>? _handler;   // _gate
        private MatchSession? _session;           // _gate, Start 이후
        private DateTime? _emptySince;            // _gate
        private int _ending;
        private int _closing;

        /// <summary>가볍다 — 매치메이커 잠금 안에서 불린다. 좌석 배정과 소속 전환만 한다.</summary>
        public Room(int index, IReadOnlyList<IPlayerLink> humans, int[] seats, ServerOptions opt,
            Func<DateTime> now, Action<string> log, Action<Room> onClosed)
        {
            if (humans.Count != seats.Length) throw new ArgumentException("humans and seats differ in length");
            _index = index;
            _opt = opt;
            _now = now;
            _log = log;
            _onClosed = onClosed;
            _humans = humans.Count;
            for (int i = 0; i < humans.Count; i++)
            {
                _bySeat[seats[i]] = humans[i];
                _deviceBySeat[seats[i]] = humans[i].DeviceId;
                humans[i].Owner = this;
            }
        }

        public IReadOnlyList<string> DeviceIds
        {
            get
            {
                var ids = new List<string>();
                foreach (var d in _deviceBySeat) if (!string.IsNullOrEmpty(d)) ids.Add(d!);
                return ids;
            }
        }

        public void Start()
        {
            ulong seed = _opt.Seed is ulong s ? unchecked(s + (ulong)_index) : unchecked((ulong)DateTime.UtcNow.Ticks + (ulong)_index);
            var config = PolicyConfig.For(_opt.Difficulty);
            int budgetMs = PimcBudget.For(_opt.Difficulty);

            var agents = new IAgent?[SeatCount];
            for (int seat = 0; seat < SeatCount; seat++)
                if (_deviceBySeat[seat] == null)
                    agents[seat] = new PacedPimcAgent(seed, seat, config, budgetMs, _opt.AiDelayMs);

            var session = new MatchSession(
                agents, this, seed,
                targetScore: _opt.Target,
                turnTimeout: TimeSpan.FromSeconds(_opt.TurnSeconds),
                interRoundPause: TimeSpan.FromMilliseconds(_opt.InterRoundPauseMs),
                standInFor: st => new PacedPimcAgent(unchecked(seed + 0x9E3779B97F4A7C15UL + (ulong)st), st, config, budgetMs, _opt.AiDelayMs),
                log: m => _log($"방 {_index} {m}"));

            var gone = new List<int>();
            lock (_gate)
            {
                _session = session;
                // Lobby 가 첫 StateUpdate 보다 먼저 큐에 들어가야 한다(클라이언트 LobbyWaiter) — 세션 시작 전에 보낸다.
                for (int seat = 0; seat < SeatCount; seat++)
                {
                    if (_deviceBySeat[seat] == null) continue;
                    if (_bySeat[seat] == null) gone.Add(seat);
                    else _bySeat[seat]!.Send(ServerEvent.Lobby(seat, _humans, _humans));
                }
                if (gone.Count == _humans) _emptySince = _now();
            }
            foreach (int seat in gone) session.SetSeatConnected(seat, false);   // 시작 전에 끊긴 좌석
            session.Start();
            _log($"방 {_index} 시작 — 사람 {_humans} · AI {SeatCount - _humans} · seed {seed} · {_opt.Difficulty} · 목표 {_opt.Target}");

            Task.Factory.StartNew(() =>
            {
                session.Join(Timeout.InfiniteTimeSpan);
                End();
            }, TaskCreationOptions.LongRunning);
        }

        public void SendTo(int seat, ServerEvent ev)
        {
            if (ev.Kind == ServerEventKind.ActionRejected || ev.Kind == ServerEventKind.SessionFaulted)
                _log($"방 {_index} 좌석 {seat} {ev.Kind}: {ev.Reason}");
            if (seat < 0 || seat >= SeatCount) return;
            lock (_gate)
            {
                if (ev.Kind == ServerEventKind.StateUpdate) _lastState[seat] = ev;   // 재접속 스냅샷
                _bySeat[seat]?.Send(ev);                                              // 끊긴·AI 좌석은 버린다
            }
        }

        public void OnClientAction(Action<ClientAction> handler)
        {
            lock (_gate) _handler = handler;
        }

        public void Receive(IPlayerLink from, ClientAction a)
        {
            Action<ClientAction>? h;
            lock (_gate)
            {
                int seat = SeatOfLocked(from);
                if (seat < 0) return;                        // 교체된 옛 연결
                a.Seat = seat;                               // 사칭 방지
                h = _handler;
            }
            h?.Invoke(a);
        }

        public void Left(IPlayerLink who)
        {
            if (Volatile.Read(ref _ending) == 1) return;     // 종료 중 우리가 닫은 연결
            int seat;
            bool empty;
            MatchSession? session;
            lock (_gate)
            {
                seat = SeatOfLocked(who);
                if (seat < 0) return;                        // 이미 새 연결로 교체됨
                _bySeat[seat] = null;
                empty = ConnectedLocked() == 0;
                if (empty) _emptySince = _now();
                session = _session;
            }
            _log($"방 {_index} 좌석 {seat} 연결 끊김 — AI 대행{(empty ? " · 사람 0명, 멈춤" : "")}");
            session?.SetSeatConnected(seat, false);          // null = 시작 전 — Start 가 처리한다
        }

        public void Rejoin(IPlayerLink link)
        {
            int seat;
            IPlayerLink? old;
            MatchSession? session;
            lock (_gate)
            {
                seat = Array.IndexOf(_deviceBySeat, link.DeviceId);
                if (seat < 0 || Volatile.Read(ref _ending) == 1)
                {
                    link.Close("match over");
                    return;
                }
                old = _bySeat[seat];
                _bySeat[seat] = link;
                link.Owner = this;
                _emptySince = null;
                session = _session;

                var lobby = ServerEvent.Lobby(seat, _humans, _humans);
                if (session != null) { var sc = session.MatchScore; lobby.TeamA = sc.TeamA; lobby.TeamB = sc.TeamB; }
                link.Send(lobby);
                var last = _lastState[seat];
                if (last?.View != null) link.Send(ServerEvent.StateUpdate(last.View, last.View.Awaiting));
            }
            old?.Close("다른 곳에서 접속");
            _log($"방 {_index} 좌석 {seat} 재접속 (연결 #{link.Id})");
            if (session == null) return;
            session.ResetSeatSequence(seat);                  // 새 앱은 ActionSeq 1 부터 — 수신 루프 시작 전에
            session.SetSeatConnected(seat, true);             // 멈춰 있었으면 다시 묻는다
        }

        public void Tick(DateTime now)
        {
            MatchSession? session;
            lock (_gate)
            {
                if (_emptySince == null || now - _emptySince.Value < TimeSpan.FromSeconds(_opt.EmptyRoomSeconds)) return;
                _emptySince = null;
                session = _session;
            }
            if (Interlocked.Exchange(ref _closing, 1) == 1) return;
            _log($"방 {_index} 사람 0명 {_opt.EmptyRoomSeconds}초 — 방 종료");
            session?.Stop();                                  // 세션 스레드가 끝나면 End 가 정리한다
        }

        private int SeatOfLocked(IPlayerLink link)
        {
            for (int s = 0; s < SeatCount; s++)
                if (_bySeat[s] == link) return s;
            return -1;
        }

        private int ConnectedLocked()
        {
            int n = 0;
            for (int s = 0; s < SeatCount; s++) if (_bySeat[s] != null) n++;
            return n;
        }

        private void End()
        {
            if (Interlocked.Exchange(ref _ending, 1) == 1) return;
            _log($"방 {_index} 종료");
            IPlayerLink?[] links;
            lock (_gate) links = (IPlayerLink?[])_bySeat.Clone();
            foreach (var l in links) l?.Close("match over");
            _onClosed(this);
        }
    }
}
```

`Matchmaker.cs`: `public void RoomClosed()` → `public void RoomClosed(IRoom room)`(본문 그대로). `ServerApp.cs` 의 방 생성:

```csharp
                (index, humans) => new Room(index, humans, SeatPicker.Pick(humans.Count, rng), opt,
                    () => DateTime.UtcNow, log, r => matchmaker!.RoomClosed(r)),
```

- [ ] **Step 5: 통과 확인** — Step 3 명령 → PASS. 서버 전체 → 실패 0(E2E 의 M3 `Leaving_mid_match_faults_only_that_room` 은 동작이 바뀌어 실패한다 — **이 태스크에서 그 테스트를 삭제**하고 Task 6 이 대체 테스트를 쓴다. 다른 E2E 는 그대로 통과해야 한다).

- [ ] **Step 6: 음성 대조** — (1) `Rejoin` 의 `session.ResetSeatSequence(seat);` 제거 → `Rejoin_restores…` 실패(actionSeq 거부). (2) `Left` 의 `if (seat < 0) return;` 를 지우고 `SeatOfLocked` 가 -1 일 때도 진행하게 하면 예외가 나므로, 대신 `Rejoin` 에서 `old?.Close(...)` 를 지우고 `_bySeat[seat] = link` 앞에 아무 것도 하지 않는 버전으로 바꿔 `Duplicate_rejoin…` 실패 확인. (3) `Tick` 의 시간 비교를 `>= 0` 으로 바꾸면 `Empty_room_closes…` 의 119초 단언 실패. 각각 원복.

- [ ] **Step 7: 커밋**

```bash
git add core/src/Tichu.Server core/tests/Tichu.Server.Tests
git commit -m "feat(server): 방 — 끊기면 AI 대행, 같은 기기 재접속, 상태 스냅샷, 빈 방 2분 종료"
```

---

### Task 5: 매치메이커 라우팅 · 식별자 관문 · 서버 옵션 배선

**Files:**
- Modify: `core/src/Tichu.Server/Matchmaking/Matchmaker.cs`, `core/src/Tichu.Server/ServerApp.cs`, `core/src/Tichu.Server/Program.cs`, `tools/smoke-server.ps1`, `deploy/server/server.env.default`
- Test: `core/tests/Tichu.Server.Tests/MatchmakerTests.cs`, `ConnectionTests.cs`

**Interfaces:**
- Consumes(Task 4): `IRoom.DeviceIds/Rejoin/Tick`, `IPlayerLink.DeviceId`, `Connection(ws, bucket, log, deviceId)`.
- Produces: `Matchmaker.Enqueue` 가 진행 중인 방의 같은 식별자면 `room.Rejoin(link)` 로 보낸다; 대기열의 같은 식별자는 자리(순서·입장 시각)를 유지한 채 새 연결로 교체하고 옛 연결을 `"다른 곳에서 접속"` 으로 닫는다; `Tick()` 이 활성 방마다 `Tick(now)`; `RoomClosed(room)` 이 그 방의 식별자를 지운다. 로그 `연결 #{id} 진행 중인 방으로 복귀`, `연결 #{old} 같은 기기의 새 접속으로 교체`. 서버 관문: `v` 가 맞고 `id` 가 `IsValidDeviceId` 여야 함(아니면 업데이트 안내 + 로그 `기기 식별자 없음 — 거절`).

- [ ] **Step 1: 실패하는 테스트** — `MatchmakerTests.cs` 에 추가:

```csharp
        [Test]
        public void Reconnecting_device_is_routed_to_its_room_not_the_queue()
        {
            var mm = Make();
            var p = Links(4);
            foreach (var l in p) mm.Enqueue(l);
            var back = new FakeLink { DeviceId = p[1].DeviceId };

            mm.Enqueue(back);

            Assert.That(_rooms[0].Rejoined, Is.EqualTo(new[] { back }));
            Assert.That(mm.QueuedCount, Is.EqualTo(0));
            Assert.That(back.QueuedEvents, Is.Empty, "대기열 상태를 받으면 안 된다");
        }

        [Test]
        public void Same_device_in_the_queue_replaces_the_old_entry_and_keeps_its_place()
        {
            var mm = Make();
            var a = new FakeLink(); var b = new FakeLink();
            mm.Enqueue(a);
            Advance(10); mm.Enqueue(b);
            Advance(5);
            var a2 = new FakeLink { DeviceId = a.DeviceId };

            mm.Enqueue(a2);

            Assert.That(a.ClosedReason, Is.EqualTo("다른 곳에서 접속"));
            Assert.That(mm.QueuedCount, Is.EqualTo(2));
            Assert.That(a2.QueuedEvents.Last().FillInSeconds, Is.EqualTo(15), "a 의 입장 시각을 이어받는다");
            Advance(15); mm.Tick();
            Assert.That(_rooms.Single().Humans, Is.EqualTo(new IPlayerLink[] { a2, b }));
        }

        [Test]
        public void A_closed_room_forgets_its_devices()
        {
            var mm = Make();
            var p = Links(4);
            foreach (var l in p) mm.Enqueue(l);
            mm.RoomClosed(_rooms[0]);

            mm.Enqueue(new FakeLink { DeviceId = p[0].DeviceId });

            Assert.That(_rooms[0].Rejoined, Is.Empty);
            Assert.That(mm.QueuedCount, Is.EqualTo(1));
        }

        [Test]
        public void Tick_passes_the_clock_to_active_rooms()
        {
            var mm = Make();
            foreach (var l in Links(4)) mm.Enqueue(l);
            Advance(3);
            mm.Tick();
            Assert.That(_rooms[0].Ticks.Last(), Is.EqualTo(_now));
        }
```

`ConnectionTests.cs`: `Connect` 헬퍼를 `ConnectRaw(ProtocolVersion.Attach(uri, Guid.NewGuid().ToString("N")))` 로. 추가:

```csharp
        [TestCase("?v=3")]
        [TestCase("?v=3&id=short")]
        public async Task Missing_or_bad_device_id_gets_the_update_notice(string query)
        {
            await using var server = await TestServer.StartAsync(QueueOnly());
            using var ws = await ConnectRaw(new Uri(server.WsUri + query));
            var ev = await Receive(ws);
            Assert.That(ev!.Kind, Is.EqualTo(ServerEventKind.SessionFaulted));
            Assert.That(ev.Reason, Is.EqualTo(ProtocolVersion.UpdateRequiredReason));
            await server.WaitForLogAsync("기기 식별자 없음");
        }
```

- [ ] **Step 2: 실패 확인** — `dotnet test core/tests/Tichu.Server.Tests/Tichu.Server.Tests.csproj --filter "FullyQualifiedName~MatchmakerTests|FullyQualifiedName~ConnectionTests" --nologo` → 새 테스트 실패.

- [ ] **Step 3: 구현** — `Matchmaker.cs`:
- 필드 `private readonly Dictionary<string, IRoom> _byDevice = new Dictionary<string, IRoom>();`, `private readonly HashSet<IRoom> _rooms = new HashSet<IRoom>();`.
- `Enqueue` 교체:

```csharp
        public void Enqueue(IPlayerLink link)
        {
            IRoom? rejoin = null;
            IPlayerLink? replaced = null;
            int count = 0;
            lock (_gate)
            {
                if (link.DeviceId.Length > 0 && _byDevice.TryGetValue(link.DeviceId, out var room))
                {
                    rejoin = room;
                }
                else
                {
                    link.Owner = this;
                    int i = link.DeviceId.Length > 0 ? _queue.FindIndex(e => e.Link.DeviceId == link.DeviceId) : -1;
                    if (i >= 0)
                    {
                        // 같은 기기가 다시 줄을 섰다 — 자리와 입장 시각을 그대로 이어받는다.
                        replaced = _queue[i].Link;
                        _queue[i] = new Entry { Link = link, Since = _queue[i].Since };
                    }
                    else
                    {
                        _queue.Add(new Entry { Link = link, Since = _now() });
                    }
                    count = _queue.Count;
                }
            }
            if (rejoin != null)
            {
                _log($"연결 #{link.Id} 진행 중인 방으로 복귀");
                rejoin.Rejoin(link);
                return;
            }
            if (replaced != null)
            {
                _log($"연결 #{replaced.Id} 같은 기기의 새 접속으로 교체");
                replaced.Close("다른 곳에서 접속");
            }
            _log($"연결 #{link.Id} 대기열 입장 ({count}명)");
            Pump();
        }
```

- `Pump` 의 방 생성 직후(`rooms.Add(...)` 다음 줄)에:

```csharp
                    var opened = rooms[rooms.Count - 1];
                    _rooms.Add(opened);
                    foreach (var h in humans) if (h.DeviceId.Length > 0) _byDevice[h.DeviceId] = opened;
```

- `RoomClosed`:

```csharp
        public void RoomClosed(IRoom room)
        {
            lock (_gate)
            {
                _activeRooms--;
                _rooms.Remove(room);
                foreach (var id in room.DeviceIds)
                    if (_byDevice.TryGetValue(id, out var r) && r == room) _byDevice.Remove(id);
            }
            Pump();
        }
```

- `Tick`:

```csharp
        /// <summary>1초마다 서버 타이머가 부른다 — 충원 시각이 된 묶음을 열고, 방들에 시계를 넘긴다(빈 방 유예).</summary>
        public void Tick()
        {
            Pump();
            List<IRoom> rooms;
            DateTime now;
            lock (_gate) { rooms = new List<IRoom>(_rooms); now = _now(); }
            foreach (var r in rooms) r.Tick(now);
        }
```

`ServerApp.cs` `/ws` 핸들러 — 버전 검사 뒤에:

```csharp
                string? deviceId = ctx.Request.Query[ProtocolVersion.DeviceIdKey];
                if (!ProtocolVersion.IsValidDeviceId(deviceId))
                {
                    log("기기 식별자 없음 — 거절");
                    await Connection.RejectAsync(ws, ProtocolVersion.UpdateRequiredReason);
                    return;
                }
                var conn = new Connection(ws, new TokenBucket(opt.RateCapacity, opt.RatePerSecond, NowSeconds), log, deviceId!);
```

(기존 `var conn = …` 줄 교체.) `Program.cs` 사용법 문자열에 `[--turn-seconds 30] [--empty-room-seconds 120]`, 배너에 `· 턴 {opt.TurnSeconds}초`. `tools/smoke-server.ps1`: `$uri` 계산을 `?v=$Version&id=$([guid]::NewGuid().ToString('N'))` 로(NoVersion 이면 그대로). `deploy/server/server.env.default`: `TICHU_GAME_ARGS=--difficulty Hard --target 1000 --fill-after 30 --max-rooms 2 --turn-seconds 30 --empty-room-seconds 120`.

- [ ] **Step 4: 통과 확인** — Step 2 명령 → PASS. 서버 전체 → 실패 0. 로컬 스모크: `dotnet run --project core/src/Tichu.Server -- --port 7799 --fill-after 600` 띄우고 `pwsh tools/smoke-server.ps1 -Url ws://127.0.0.1:7799/ws` → OK, `-NoVersion -ExpectReject` → OK, 서버 종료.

- [ ] **Step 5: 음성 대조** — `Enqueue` 의 `_byDevice.TryGetValue` 분기를 지우면 `Reconnecting_device_is_routed…` 실패; `RoomClosed` 의 식별자 제거 루프를 지우면 `A_closed_room_forgets…` 실패. 원복.

- [ ] **Step 6: 커밋**

```bash
git add core/src/Tichu.Server core/tests/Tichu.Server.Tests tools/smoke-server.ps1 deploy/server/server.env.default
git commit -m "feat(server): 기기 식별자 관문과 재접속 라우팅 — 진행 중인 방으로 복귀, 대기열 중복 교체"
```

---

### Task 6: E2E — 끊김·복귀·잠수·빈 방·이중 접속

**Files:**
- Modify: `core/tests/Tichu.Server.Tests/EndToEndTests.cs`

**Interfaces:**
- Consumes: `WebSocketClientTransport.ConnectAsync(uri, deviceId, timeout, ct)`, 기존 `AwaitLobby`/`Joined`/`PlayToEnd`/`Count` 헬퍼, Task 4·5 로그 문구.

- [ ] **Step 1: 테스트 작성** — 헬퍼 추가:

```csharp
        private static Task<WebSocketClientTransport> ConnectAs(TestServer s, string id) =>
            WebSocketClientTransport.ConnectAsync(s.WsUri, id, TimeSpan.FromSeconds(5), CancellationToken.None);

        private static string NewId() => Guid.NewGuid().ToString("N");
```

테스트:

```csharp
        [Test]
        public async Task A_dropped_solo_player_rejoins_the_same_seat_and_finishes()
        {
            var opt = Fast(target: 200);
            opt.AiDelayMs = 20;
            await using var server = await TestServer.StartAsync(opt);
            string id = NewId();
            var t1 = await ConnectAs(server, id);
            var j1 = await AwaitLobby(t1);
            var c1 = new ScriptedClient(j1.Seat, j1);
            var until = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < until) { c1.Pump(); await Task.Delay(5); }
            t1.Dispose();                                     // 판 도중 끊김(혼자 → 멈춤)
            await server.WaitForLogAsync("사람 0명, 멈춤");

            using var t2 = await ConnectAs(server, id);
            var j2 = await AwaitLobby(t2);

            Assert.That(j2.Seat, Is.EqualTo(j1.Seat), "같은 자리로 돌아온다");
            await server.WaitForLogAsync($"좌석 {j2.Seat} 재접속");
            await PlayToEnd(new ScriptedClient(j2.Seat, j2));
        }

        [Test]
        public async Task Remaining_player_finishes_when_the_other_never_returns()
        {
            var opt = Fast(target: 200);
            opt.AiDelayMs = 20;
            await using var server = await TestServer.StartAsync(opt);
            using var t1 = await Connect(server);
            var t2 = await Connect(server);
            var j1 = await AwaitLobby(t1);
            await AwaitLobby(t2);
            t2.Dispose();

            await PlayToEnd(new ScriptedClient(j1.Seat, j1));
            await server.WaitForLogAsync("연결 끊김 — AI 대행");
        }

        [Test]
        public async Task Idle_player_times_out_and_becomes_away()
        {
            var opt = Fast(target: 200);
            opt.TurnSeconds = 1;
            await using var server = await TestServer.StartAsync(opt);
            using var tActive = await Connect(server);
            using var tIdle = await Connect(server);          // 아무것도 하지 않는다
            var j = await AwaitLobby(tActive);
            await AwaitLobby(tIdle);

            var active = new ScriptedClient(j.Seat, j);
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline && !active.MatchEnded)
            {
                active.Pump();
                tIdle.Drain();
                await Task.Delay(2);
            }
            Assert.That(active.MatchEnded, Is.True);
            await server.WaitForLogAsync("자리 비움, 대리 AI");
        }

        [Test]
        public async Task Empty_room_closes_after_the_grace_period()
        {
            var opt = Fast(target: 1000);
            opt.AiDelayMs = 20;
            opt.EmptyRoomSeconds = 1;
            await using var server = await TestServer.StartAsync(opt);
            var t = await Connect(server);
            await AwaitLobby(t);
            t.Dispose();

            await server.WaitForLogAsync("사람 0명 1초 — 방 종료");
            await server.WaitForLogAsync("방 0 종료");
        }

        [Test]
        public async Task Second_connection_from_the_same_device_takes_the_seat()
        {
            var opt = Fast(target: 1000);
            opt.AiDelayMs = 20;
            await using var server = await TestServer.StartAsync(opt);
            string id = NewId();
            using var first = await ConnectAs(server, id);
            var j1 = await AwaitLobby(first);

            using var second = await ConnectAs(server, id);
            var j2 = await AwaitLobby(second);

            Assert.That(j2.Seat, Is.EqualTo(j1.Seat));
            var lost = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < lost && !first.ConnectionLost) { first.Drain(); await Task.Delay(10); }
            Assert.That(first.ConnectionLost, Is.True, "옛 연결은 닫힌다");
        }

        [Test]
        public async Task Old_v2_app_gets_the_update_notice()
        {
            await using var server = await TestServer.StartAsync(Fast());
            using var ws = new System.Net.WebSockets.ClientWebSocket();
            await ws.ConnectAsync(new Uri(server.WsUri + "?v=2"), CancellationToken.None);
            var buf = new byte[4096];
            var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
            var ev = ProtocolJson.DeserializeServerEvent(System.Text.Encoding.UTF8.GetString(buf, 0, r.Count));
            Assert.That(ev.Reason, Is.EqualTo(ProtocolVersion.UpdateRequiredReason));
        }
```

- [ ] **Step 2: 실행** — `dotnet test core/tests/Tichu.Server.Tests/Tichu.Server.Tests.csproj --filter "FullyQualifiedName~EndToEndTests" --nologo` → 전부 PASS. 3회 반복해 플래키 없음 확인. 서버 전체 1회.

- [ ] **Step 3: 음성 대조** — `Room.Rejoin` 의 `session.ResetSeatSequence(seat);` 를 잠시 지우면 `A_dropped_solo_player…` 가 실패(재접속 뒤 액션 전부 거부 → 시간 초과)하는지 확인 후 원복.

- [ ] **Step 4: 커밋**

```bash
git add core/tests/Tichu.Server.Tests/EndToEndTests.cs
git commit -m "test(server): M5 E2E — 끊김 후 같은 자리 복귀·남은 사람 완주·잠수 자리 비움·빈 방 종료·이중 접속"
```

---

### Task 7: 클라이언트 로직 — 기기 식별자 · 재접속 정책 · 뷰모델

**Files:**
- Create: `Assets/_Project/Presentation/Net/DeviceIdentity.cs`, `Assets/_Project/Presentation/Net/ReconnectPolicy.cs`
- Modify: `Assets/_Project/Presentation/Net/LobbyWaiter.cs`, `Assets/_Project/Presentation/ViewModel/TableViewModel.cs`
- Create: `Assets/_Project/Presentation/Tests/DeviceIdentityTests.cs`, `ReconnectPolicyTests.cs`
- Test: `Assets/_Project/Presentation/Tests/LobbyWaiterTests.cs`, `TableViewModelTests.cs`

**Interfaces:**
- Consumes(Task 1): `ProtocolVersion.IsValidDeviceId`, `Awaiting.AskId/SecondsLeft`, `PlayerView.Away`, `ClientAction.AskId`, `ClientAction.Resume`.
- Produces:
  - `static string DeviceIdentity.GetOrCreate(Func<string> load, Action<string> save)`.
  - `ReconnectPolicy(Func<double> nowSeconds, double budgetSeconds = 120)` · `bool TryNextDelay(out double seconds)`(1,2,4,8,8…; 예산 초과면 false).
  - `LobbyWaiter.MatchTeamA/MatchTeamB`(절대 좌석 기준) · `void LobbyWaiter.LocalScore(out int mine, out int theirs)`(내 팀 기준 — 좌석 0 기준 뷰모델의 A/B).
  - `TableViewModel(int mySeat, Func<double> nowSeconds = null)`; `ReactiveProperty<bool> MyAway`; `ReadOnlyReactiveProperty<bool> SeatAway(int seat)`; `int? TurnSecondsLeft`; `bool SubmitResume()`; `void SetMatchScore(int mine, int theirs)`; `void ResetForReconnect()`. 답 액션(큰 티츄·교환·패스·차례 플레이·용 양도)에 지금 프롬프트의 `AskId` 첨부, 차례 밖 폭탄·작은 티츄는 0.

- [ ] **Step 1: 실패하는 테스트**

`DeviceIdentityTests.cs`:

```csharp
using NUnit.Framework;
using Tichu.Net;
using Tichu.Presentation.Net;

namespace Tichu.Presentation.Tests
{
    public class DeviceIdentityTests
    {
        [Test]
        public void A_new_id_is_created_once_and_then_reused()
        {
            string stored = "";
            string first = DeviceIdentity.GetOrCreate(() => stored, v => stored = v);
            string second = DeviceIdentity.GetOrCreate(() => stored, v => stored = v);

            Assert.That(ProtocolVersion.IsValidDeviceId(first), Is.True);
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void A_corrupt_stored_value_is_replaced()
        {
            string stored = "garbage";
            string id = DeviceIdentity.GetOrCreate(() => stored, v => stored = v);
            Assert.That(ProtocolVersion.IsValidDeviceId(id), Is.True);
            Assert.That(stored, Is.EqualTo(id));
        }
    }
}
```

`ReconnectPolicyTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Presentation.Net;

namespace Tichu.Presentation.Tests
{
    public class ReconnectPolicyTests
    {
        [Test]
        public void Delays_double_up_to_eight_seconds()
        {
            double now = 0;
            var p = new ReconnectPolicy(() => now, 120);
            var got = new List<double>();
            for (int i = 0; i < 6; i++) { Assert.That(p.TryNextDelay(out double d), Is.True); got.Add(d); now += d; }
            Assert.That(got, Is.EqualTo(new[] { 1.0, 2.0, 4.0, 8.0, 8.0, 8.0 }));
        }

        [Test]
        public void Gives_up_when_the_budget_runs_out()
        {
            double now = 0;
            var p = new ReconnectPolicy(() => now, 10);
            Assert.That(p.TryNextDelay(out double d1), Is.True); now += d1;   // 1
            Assert.That(p.TryNextDelay(out double d2), Is.True); now += d2;   // 3
            Assert.That(p.TryNextDelay(out double d3), Is.True); now += d3;   // 7
            Assert.That(p.TryNextDelay(out _), Is.False, "7 + 8 > 10");
        }
    }
}
```

`LobbyWaiterTests.cs` 에 추가:

```csharp
        [Test]
        public void Rejoin_lobby_carries_the_match_score_for_my_team()
        {
            var w = new LobbyWaiter(() => 0);
            var lobby = ServerEvent.Lobby(3, 2, 2);
            lobby.TeamA = 300; lobby.TeamB = 450;
            w.Feed(new[] { lobby });

            w.LocalScore(out int mine, out int theirs);
            Assert.That(w.MatchTeamA, Is.EqualTo(300));
            Assert.That((mine, theirs), Is.EqualTo((450, 300)), "좌석 3 은 B 팀 — 내 팀이 앞");
        }
```

`TableViewModelTests.cs` 에 추가(파일의 `MyTurnView`·`AskPlay` 헬퍼 재사용; `AskPlay` 에 `long askId = 1, int? secs = 30` 선택 인자를 더해 `AskId = askId, SecondsLeft = secs` 를 설정하도록 수정):

```csharp
        [Test]
        public void Answers_carry_the_prompt_ask_id_but_off_turn_calls_do_not()
        {
            var s = GameEngine.NewRound(777UL);
            var legal = s.Seats[0].Hand[0];
            var vm = new TableViewModel(0);
            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));
            vm.Apply(MyTurnView(s, 0, AskPlay(legal, askId: 17)));

            vm.SubmitPass();

            Assert.That(sent.Last().AskId, Is.EqualTo(17));
        }

        [Test]
        public void A_held_turn_prompt_takes_the_new_ask_id()
        {
            // 작은 티츄 Yield 재질의: 깜빡임 방지로 인스턴스는 유지하지만 번호는 새 질문의 것이어야 한다.
            var s = GameEngine.NewRound(777UL);
            var legal = s.Seats[0].Hand[0];
            var vm = new TableViewModel(0);
            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));
            vm.Apply(MyTurnView(s, 0, AskPlay(legal, askId: 5)));
            var held = vm.Prompt.CurrentValue;
            vm.Apply(MyTurnView(s, 0, null));
            vm.Apply(MyTurnView(s, 0, AskPlay(legal, askId: 6)));

            Assert.That(vm.Prompt.CurrentValue, Is.SameAs(held));
            vm.SubmitPass();
            Assert.That(sent.Last().AskId, Is.EqualTo(6));
        }

        [Test]
        public void Turn_countdown_runs_from_the_time_the_question_arrived()
        {
            var s = GameEngine.NewRound(777UL);
            double now = 100;
            var vm = new TableViewModel(0, () => now);
            vm.Apply(MyTurnView(s, 0, AskPlay(s.Seats[0].Hand[0], askId: 1, secs: 30)));

            Assert.That(vm.TurnSecondsLeft, Is.EqualTo(30));
            now = 112.4;
            Assert.That(vm.TurnSecondsLeft, Is.EqualTo(18));
            vm.SubmitPass();
            Assert.That(vm.TurnSecondsLeft, Is.Null, "답하면 타이머가 사라진다");
        }

        [Test]
        public void Away_flags_and_resume()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));
            var v = MyTurnView(s, 1, null);
            v.Away[0] = true; v.Away[2] = true;
            vm.Apply(v);

            Assert.That(vm.MyAway.CurrentValue, Is.True);
            Assert.That(vm.SeatAway(2).CurrentValue, Is.True);
            Assert.That(vm.SubmitResume(), Is.True);
            Assert.That(sent.Last().Kind, Is.EqualTo(ClientActionKind.Resume));
        }

        [Test]
        public void Reconnect_resets_local_answers_and_sets_the_score()
        {
            var s = GameEngine.NewRound(777UL);           // 큰 티츄 단계
            var vm = new TableViewModel(0);
            vm.Apply(ViewOf(s, 0));
            vm.SubmitGrandTichu(false);
            vm.Apply(ViewOf(s, 0));
            Assert.That(vm.Prompt.CurrentValue, Is.Null);

            vm.ResetForReconnect();
            vm.SetMatchScore(450, 300);
            vm.Apply(ViewOf(s, 0));

            Assert.That(vm.Prompt.CurrentValue, Is.Not.Null, "재접속하면 서버 상태로 다시 묻는다");
            Assert.That(vm.CumulativeA.CurrentValue, Is.EqualTo(450));
            Assert.That(vm.CumulativeB.CurrentValue, Is.EqualTo(300));
        }
```

- [ ] **Step 2: 실패 확인** — Unity 컴파일 에러(새 타입·멤버 없음) 또는 `python unity_tests.py names Tichu.Presentation.Tests.TableViewModelTests` → 1.

- [ ] **Step 3: 구현**

`DeviceIdentity.cs`:

```csharp
using System;
using Tichu.Net;

namespace Tichu.Presentation.Net
{
    /// <summary>
    /// 이 기기의 식별자(M5 재접속 키). 처음 한 번 무작위 GUID 를 만들어 저장하고 이후 그대로 쓴다.
    /// 개인정보가 아니라 앱이 만든 무작위 값이다. 저장소는 호출자가 준다(유니티는 PlayerPrefs).
    /// </summary>
    public static class DeviceIdentity
    {
        public static string GetOrCreate(Func<string> load, Action<string> save)
        {
            string stored = load();
            if (ProtocolVersion.IsValidDeviceId(stored)) return stored;
            string id = Guid.NewGuid().ToString("N");
            save(id);
            return id;
        }
    }
}
```

`ReconnectPolicy.cs`:

```csharp
using System;

namespace Tichu.Presentation.Net
{
    /// <summary>
    /// 연결이 끊겼을 때 다시 접속할 간격: 1·2·4·8·8…초, 첫 시도부터 budget 초 안에서만(M5 §5).
    /// 서버는 사람 0명인 방을 120초 기다리므로 기본 예산도 120초다.
    /// </summary>
    public sealed class ReconnectPolicy
    {
        private readonly Func<double> _now;
        private readonly double _budget;
        private readonly double _start;
        private int _attempt;

        public ReconnectPolicy(Func<double> nowSeconds, double budgetSeconds = 120)
        {
            _now = nowSeconds;
            _budget = budgetSeconds;
            _start = nowSeconds();
        }

        public bool TryNextDelay(out double seconds)
        {
            seconds = Math.Min(8.0, Math.Pow(2, _attempt));
            _attempt++;
            return _now() - _start + seconds <= _budget;
        }
    }
}
```

`LobbyWaiter.cs`: 속성 `public int MatchTeamA { get; private set; }`, `public int MatchTeamB { get; private set; }`; `Feed` 의 `Lobby` 분기에 `MatchTeamA = ev.TeamA; MatchTeamB = ev.TeamB;`; 메서드

```csharp
        /// <summary>내 팀 기준 누적 점수(뷰모델은 좌석 0 기준 — 홀수 좌석이면 B 가 내 팀).</summary>
        public void LocalScore(out int mine, out int theirs)
        {
            bool odd = (Seat & 1) == 1;
            mine = odd ? MatchTeamB : MatchTeamA;
            theirs = odd ? MatchTeamA : MatchTeamB;
        }
```

`TableViewModel.cs`:
- 필드/생성자:

```csharp
        private readonly Func<double> _now;
        private double? _turnDeadline;      // 내게 온 서버 질문의 마감(로컬 시계)
        private long _deadlineAsk;          // 그 마감을 잡은 질문 번호
        private readonly ReactiveProperty<bool>[] _away = new ReactiveProperty<bool>[4];

        /// <summary>내 좌석이 자리 비움(AI 대행) — 배너·[돌아가기].</summary>
        public ReactiveProperty<bool> MyAway { get; } = new ReactiveProperty<bool>(false);

        public ReadOnlyReactiveProperty<bool> SeatAway(int seat) => _away[seat];

        /// <summary>내 차례 남은 초. 묻는 질문이 없으면 null.</summary>
        public int? TurnSecondsLeft =>
            _turnDeadline.HasValue ? Math.Max(0, (int)Math.Ceiling(_turnDeadline.Value - _now())) : (int?)null;
```

생성자를 `public TableViewModel(int mySeat, Func<double> nowSeconds = null)` 로, 본문에 `_now = nowSeconds ?? (() => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency);` 와 루프 안 `_away[i] = new ReactiveProperty<bool>(false);`.

- `Apply` 끝(`Prompt.Value = PromptFor(v);` 다음)에:

```csharp
            for (int i = 0; i < 4; i++) _away[i].Value = v.Away != null && v.Away[i];
            MyAway.Value = _away[_mySeat].Value;
            TrackDeadline(v.Awaiting);
            if (Prompt.CurrentValue == null) _turnDeadline = null;
```

```csharp
        /// <summary>서버가 내게 새 질문을 보냈으면(번호가 다르면) 마감을 다시 잡는다 — 받은 시각 + 남은 초.</summary>
        private void TrackDeadline(Awaiting aw)
        {
            if (aw == null || aw.Seat != _mySeat || !aw.SecondsLeft.HasValue || aw.AskId == _deadlineAsk) return;
            _deadlineAsk = aw.AskId;
            _turnDeadline = _now() + aw.SecondsLeft.Value;
        }
```

- `TurnPrompt` 의 재질의 유지 분기 `if (held && stillMyTurn && v.Awaiting.Kind == StepKind.Play) return current;` 를:

```csharp
            if (held && stillMyTurn && v.Awaiting.Kind == StepKind.Play)
            {
                // 인스턴스는 유지(다시 그리지 않음), 질문 번호·합법수·남은 초는 새 질문의 것으로.
                current.AskId = v.Awaiting.AskId;
                current.SecondsLeft = v.Awaiting.SecondsLeft;
                current.LegalMoves = v.Awaiting.LegalMoves;
                current.CanPass = v.Awaiting.CanPass;
                return current;
            }
```

- 답 액션에 번호: `Answered()` 앞에서 보내는 다섯 곳(큰 티츄·교환·패스·차례 `SubmitPlay`·용 양도)의 `Send(X)` 를 `Send(WithAsk(X))` 로. 차례 밖 폭탄(`myTurn == false`)·작은 티츄는 그대로.

```csharp
        private ClientAction WithAsk(ClientAction a)
        {
            a.AskId = Prompt.CurrentValue?.AskId ?? 0;
            return a;
        }
```

(`SubmitPlay` 는 `myTurn ? WithAsk(action) : action`.)
- `Answered()` 에 `_turnDeadline = null;` 추가.
- 공개 메서드:

```csharp
        /// <summary>자리 비움에서 돌아가기.</summary>
        public bool SubmitResume()
        {
            Send(ClientAction.Resume(_mySeat, NextSeq()));
            return true;
        }

        /// <summary>재접속 로비의 누적 점수(내 팀 기준)로 갈아끼운다.</summary>
        public void SetMatchScore(int mine, int theirs)
        {
            CumulativeA.Value = mine;
            CumulativeB.Value = theirs;
        }

        /// <summary>재접속 직후 — 로컬에만 있던 답함·예약 표시를 지운다. 서버 스냅샷이 다시 묻게 한다.</summary>
        public void ResetForReconnect()
        {
            _grandAnswered = false;
            _exchangeAnswered = false;
            _earlyPrompt = null;
            _turnPromptHeld = false;
            _tichuSent = false;
            _bombSent = false;
            _turnDeadline = null;
            _deadlineAsk = 0;
            Prompt.Value = null;
        }
```

- [ ] **Step 4: 통과 확인** — Unity `refresh scope all`(새 파일 4개) → 에러 0 → `python unity_tests.py assembly Tichu.Presentation.Tests` → 0, total 이 이전보다 `2+2+1+5 = 10` 늘었는지 확인.

- [ ] **Step 5: 음성 대조** — `TurnPrompt` 의 `current.AskId = v.Awaiting.AskId;` 제거 → `A_held_turn_prompt_takes_the_new_ask_id` 실패 확인, 원복.

- [ ] **Step 6: 커밋**(새 파일의 `.meta` 포함)

```bash
git add Assets/_Project/Presentation
git commit -m "feat(presentation): 기기 식별자·재접속 정책·질문 번호 첨부·턴 타이머·자리 비움 상태"
```

---

### Task 8: 클라이언트 화면·접속 — 타이머 · 배너 · AI 대행 · 자동 재접속

**Files:**
- Modify: `Assets/_Project/Presentation/Views/RuntimeTableView.cs`, `Assets/_Project/Presentation/RoundBootstrap.cs`

**Interfaces:**
- Consumes(Task 7): `vm.TurnSecondsLeft`, `vm.MyAway`, `vm.SeatAway(i)`, `vm.SubmitResume()`, `vm.SetMatchScore`, `vm.ResetForReconnect()`, `DeviceIdentity`, `ReconnectPolicy`, `LobbyWaiter.LocalScore`; (Task 1) `WebSocketClientTransport.ConnectAsync(uri, deviceId, …)`, `ConnectionLost`.

- [ ] **Step 1: 뷰** — `RuntimeTableView.cs`
- 필드: `private Text _timerText; private GameObject _awayBanner;`
- 화면 구성(`_promptLabel` 를 만드는 곳 바로 뒤):

```csharp
            _timerText = NewText("TurnTimer", panel.transform, "", 24);
            _timerText.alignment = TextAnchor.MiddleCenter;
            _timerText.color = Warn;
```

(`panel` 은 `_promptLabel` 의 부모와 같은 지역 변수. 레이아웃 그룹 안이면 자동 배치된다.)
- 자리 비움 배너(같은 구성 메서드 끝, `rt` = 루트 RectTransform):

```csharp
            _awayBanner = new GameObject("AwayBanner", typeof(RectTransform), typeof(Image));
            _awayBanner.transform.SetParent(rt, false);
            var brt = (RectTransform)_awayBanner.transform;
            brt.anchorMin = new Vector2(0.5f, 0f); brt.anchorMax = new Vector2(0.5f, 0f); brt.pivot = new Vector2(0.5f, 0f);
            brt.sizeDelta = new Vector2(900, 90); brt.anchoredPosition = new Vector2(0, 20);
            _awayBanner.GetComponent<Image>().color = new Color(0.10f, 0.12f, 0.16f, 0.92f);
            var bl = _awayBanner.AddComponent<HorizontalLayoutGroup>();
            bl.childAlignment = TextAnchor.MiddleCenter; bl.spacing = 24; bl.childForceExpandWidth = false;
            var msg = NewText("AwayMsg", _awayBanner.transform, "자리 비움 — AI가 대신 두는 중", 26);
            msg.gameObject.AddComponent<LayoutElement>().preferredWidth = 520;
            var back = new GameObject("Btn_돌아가기", typeof(RectTransform), typeof(Image), typeof(Button));
            back.transform.SetParent(_awayBanner.transform, false);
            back.GetComponent<Image>().color = BtnGo;
            back.AddComponent<LayoutElement>().preferredWidth = 220;
            back.GetComponent<Button>().onClick.AddListener(() => _vm.SubmitResume());
            AddCardLabel(back.transform, "돌아가기", Ink, 24);
            _awayBanner.SetActive(false);
```

- `Bind` 의 구독 끝에:

```csharp
            _vm.MyAway.Subscribe(a => { if (_awayBanner != null) _awayBanner.SetActive(a); }).AddTo(_subs);
            for (int i = 1; i < 4; i++)
            {
                int seat = i;
                _vm.SeatAway(seat).Subscribe(a => _seatTexts[seat].text = a ? $"{SeatNames[seat]} (AI 대행)" : SeatNames[seat]).AddTo(_subs);
            }
            Observable.EveryUpdate().Subscribe(_ =>
            {
                var left = _vm.TurnSecondsLeft;
                _timerText.text = left.HasValue ? $"남은 시간 {left}초" : "";
            }).AddTo(_subs);
```

(`Warn`·`BtnGo`·`Ink`·`NewText`·`AddCardLabel` 은 파일에 이미 있는 이름. 없으면 같은 역할의 기존 이름을 쓰고 보고서에 적는다.)

- [ ] **Step 2: 접속·재접속** — `RoundBootstrap.cs`:
- 상수 `private const string DeviceIdKey = "tichu.deviceId";`, `using Tichu.Presentation.Net;`(이미 있으면 생략).
- `RunNetworkMatchAsync` 를 다음으로 교체:

```csharp
        /// <summary>
        /// 서버에 접속해 대기실에서 좌석을 받은 뒤 같은 펌프로 매치를 돈다. 연결이 끊기면(서버의 정상 종료가
        /// 아니면) 같은 기기 식별자로 다시 접속해 같은 자리로 돌아간다(M5) — 서버가 로비+스냅샷을 보낸다.
        /// </summary>
        private async UniTaskVoid RunNetworkMatchAsync(TableViewModel vm, Canvas canvas)
        {
            var ct = this.GetCancellationTokenOnDestroy();
            string deviceId = DeviceIdentity.GetOrCreate(
                () => PlayerPrefs.GetString(DeviceIdKey, ""),
                v => { PlayerPrefs.SetString(DeviceIdKey, v); PlayerPrefs.Save(); });
            var overlay = CreateLobbyOverlay(canvas, out var status, out var cancel);
            cancel.onClick.AddListener(() => InvokeFault(null));   // 사용자 취소 → 알림 없이 메뉴로
            ReconnectPolicy retry = null;                           // null = 첫 접속

            try
            {
                while (true)
                {
                    WebSocketClientTransport transport;
                    try
                    {
                        transport = await WebSocketClientTransport.ConnectAsync(_args.ServerAddress, deviceId, TimeSpan.FromSeconds(5), ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                    catch (Exception e)
                    {
                        if (ct.IsCancellationRequested || _faulted) return;
                        if (retry != null && retry.TryNextDelay(out double wait))
                        {
                            await UniTask.Delay(TimeSpan.FromSeconds(wait), cancellationToken: ct);
                            continue;
                        }
                        Debug.LogWarning($"[RoundBootstrap] 접속 실패 {_args.ServerAddress}: {e.Message}");
                        InvokeFault(retry == null ? "서버에 연결할 수 없습니다" : "서버와 연결이 끊어졌습니다");
                        return;
                    }

                    using (transport)
                    {
                        var lobby = new LobbyWaiter();
                        while (!lobby.Started && lobby.FaultReason == null)
                        {
                            lobby.Feed(transport.Drain());
                            if (_faulted) return;   // 사용자 취소 — 파괴될 UI 를 건드리지 않는다
                            if (retry == null) status.text = lobby.StatusText;
                            await UniTask.Yield(PlayerLoopTiming.Update, ct);
                        }
                        if (lobby.FaultReason != null)
                        {
                            if (transport.ConnectionLost && retry != null && retry.TryNextDelay(out double w2))
                            {
                                await UniTask.Delay(TimeSpan.FromSeconds(w2), cancellationToken: ct);
                                continue;
                            }
                            InvokeFault(lobby.FaultReason);
                            return;
                        }

                        if (overlay != null) { Destroy(overlay); overlay = null; }
                        if (retry != null) vm.ResetForReconnect();
                        lobby.LocalScore(out int mine, out int theirs);
                        vm.SetMatchScore(mine, theirs);
                        retry = null;

                        MatchSummary? summary = null;
                        using var client = new TichuClient(transport, vm, lobby.Seat, s => summary = s, lobby.Backlog);
                        while (!summary.HasValue && !client.Faulted && !ct.IsCancellationRequested)
                        {
                            client.Pump();
                            await UniTask.Yield(PlayerLoopTiming.Update, ct);
                        }
                        if (ct.IsCancellationRequested) return;
                        if (summary.HasValue) { _onComplete?.Invoke(summary.Value); return; }

                        if (transport.ConnectionLost)
                        {
                            Debug.LogWarning("[RoundBootstrap] 연결이 끊겼다 — 재접속을 시도한다");
                            retry = new ReconnectPolicy(() => Time.realtimeSinceStartupAsDouble);
                            overlay = CreateLobbyOverlay(canvas, out status, out cancel);
                            cancel.onClick.AddListener(() => InvokeFault(null));
                            status.text = "재접속 중…";
                            if (retry.TryNextDelay(out double w3))
                                await UniTask.Delay(TimeSpan.FromSeconds(w3), cancellationToken: ct);
                            continue;
                        }
                        Debug.LogError($"[RoundBootstrap] 세션 폴트로 매치를 중단한다: {client.LastRejection}");
                        InvokeFault(client.LastRejection);
                        return;
                    }
                }
            }
            catch (OperationCanceledException) { /* 씬 종료: 정상 */ }
            catch (Exception e) { Debug.LogException(e); }
        }
```

(기존 `PumpUntilEndAsync` 는 로컬 경로가 계속 쓰므로 그대로 둔다.)

- [ ] **Step 3: 컴파일·회귀** — Unity `refresh scope all` → 에러 0 → `python unity_tests.py assembly Tichu.Presentation.Tests` → 0.

- [ ] **Step 4: 에디터 수동 확인** — 서버 `dotnet run --project core/src/Tichu.Server -- --fill-after 5 --turn-seconds 10`. 에디터 Play → 게임 시작 → 온라인 대전 → `127.0.0.1` → AI 와 시작 → 내 차례에 "남은 시간 N초" 가 줄어드는지, 2번 가만히 두면 "자리 비움" 배너 → [돌아가기] 로 사라지는지. 서버를 Ctrl+C 로 끄면 "재접속 중…" 오버레이가 뜨는지(2분 뒤 메뉴로). 결과를 보고서에 적는다. 서버 종료.

- [ ] **Step 5: 커밋**

```bash
git add Assets/_Project/Presentation/Views/RuntimeTableView.cs Assets/_Project/Presentation/RoundBootstrap.cs
git commit -m "feat(presentation): 턴 타이머 표시·자리 비움 배너·AI 대행 표시·자동 재접속"
```

---

### Task 9: 회귀 · 실기 · APK · 보고

**Files:**
- Create: `docs/reports/design/티츄_M5_타이머_재접속_결과.html`
- Modify(필요 시): `docs/ops/server-runbook.md`(새 옵션 `--turn-seconds`·`--empty-room-seconds`, 로그 문구 "연결 끊김 — AI 대행"·"재접속"·"사람 0명 … 방 종료", 진행 중 판 확인법은 그대로 `방 N 시작`/`방 N 종료`)

- [ ] **Step 1: 전체 회귀** — dotnet core 전체 · server 전체 → 실패 0. Unity: `python unity_tests.py assembly Tichu.Presentation.Tests` → 0; `python unity_tests.py names Tichu.Core.Tests.ProtocolJsonTests Tichu.Core.Tests.ProtocolVersionTests Tichu.Core.Tests.SeatRotationTests Tichu.Core.Tests.RemoteAgentTests Tichu.Core.Tests.MatchSessionPresenceTests Tichu.Core.Tests.LoopbackMatchTests Tichu.Core.Tests.MatchSessionOracleTests` → 0. 미러 diff → 출력 없음.
- [ ] **Step 2: 운영 안내** — `docs/ops/server-runbook.md` 의 옵션·로그 설명을 위 내용으로 갱신(같은 커밋).
- [ ] **Step 3: 새 개발 APK** — 에디터 안 MCP `execute_menu_item {"menu_path":"Tichu/Build Dev APK"}`(호출은 시간 초과로 돌아와도 빌드는 진행 — `Build/TichuMaster-dev-arm64.apk` 갱신 시각과 콘솔 `[DevBuild] 성공` 으로 확인). 빌드 뒤 `ProjectSettings/ProjectSettings.asset` 에 생긴 `preloadedAssets` 변경은 되돌린다(`git checkout -- ProjectSettings/ProjectSettings.asset`). `AndroidBundleVersionCode` 는 2 유지.
- [ ] **Step 4: 실기(사람, 에디터 + S23, 같은 Wi-Fi LAN 서버 `--fill-after 30`)** — 체크리스트:
  1. 판 도중 폰 비행기 모드 → 에디터 화면에 그 좌석 "(AI 대행)" → 해제 → 폰이 "재접속 중…" 뒤 같은 자리로 복귀.
  2. 폰 앱 완전 종료 → 재실행 → 온라인 대전 → 진행 중인 판으로 바로 복귀(점수 유지).
  3. 2번 연속 시간 초과 → 자리 비움 배너 → [돌아가기].
  4. 둘 다 나가면 서버 로그에 2분 뒤 `사람 0명 120초 — 방 종료`.
  5. 구 APK(v2) 는 업데이트 안내.
- [ ] **Step 5: 보고서 HTML** — 바뀐 것(좌석 상태 그림), 테스트 수, 실기 결과, 계획이 정한 세부(대리 AI 는 티츄를 부르지 않음 등), 남은 일. 커밋 `docs(mp): M5 결과 보고`.
- [ ] **Step 6: 머지 승인 요청** — main 머지·푸시는 사용자 승인 후.
