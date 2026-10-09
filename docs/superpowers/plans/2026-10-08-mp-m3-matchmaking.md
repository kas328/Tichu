# 멀티플레이 M3 — 다중 방 · 랜덤 매칭 대기열 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 서버 한 프로세스가 랜덤 매칭 대기열로 사람을 모아 여러 방(매치)을 동시에 돌리고, 30초 뒤 빈자리를 Hard AI로 채우며, 방 상한에 걸리면 대기 순번을 안내한다.

**Architecture:** 지금의 "방 하나짜리 전송"(`WebSocketServerTransport` + `MatchHost` + `SeatTable`)을 세 단위로 나눈다 — `Connection`(소켓 하나: 송수신·검사), `Matchmaker`(대기열·충원·상한, 순수 로직), `Room`(매치 하나: 좌석·`MatchSession`·AI, `ITichuTransport` 구현). 연결은 `IPlayerLink.Owner` 로 처음엔 대기열, 묶이면 방에 속한다. 프로토콜은 v2 로 올리고 대기 상태 이벤트 `Queued` 하나를 더한다.

**Tech Stack:** .NET 10(서버, ASP.NET Core Kestrel WebSocket) · .NET 9(core 테스트) · NUnit 4(dotnet) / NUnit 3.5(Unity) · Newtonsoft.Json · Unity 6000.3.17f1(클라이언트, R3).

**Spec:** `docs/superpowers/specs/2026-10-08-mp-m3-matchmaking-design.md`

## Global Constraints

- 입장 방식 = 랜덤 매칭만. 초대 코드 없음. 새 클라이언트 액션 없음(대기 취소 = 연결 닫기).
- AI 충원 = 가장 오래 기다린 사람 기준 **30초**(`--fill-after`, 기본 30). 4명이 모이면 즉시.
- AI 난이도 = 서버 고정 **Hard**(`--difficulty`, 기본 Hard). 목표 점수 **1000**(`--target`, 기본 1000).
- 방 상한 `--max-rooms`, 잠정 기본 **2**. 꽉 차면 대기열에 남기고 `Busy`·`Position` 안내.
- `--seats` 는 **삭제**. 사람 좌석은 방 생성 때 무작위(`SeatPicker`).
- 프로토콜 `ProtocolVersion.Current = 2`. v1 앱은 기존 관문이 "앱을 업데이트해 주세요 (서버 프로토콜 v2)" 를 보내고 닫는다.
- `Queued` 필드: `Waiting`(대기열 인원, 나 포함) · `FillInSeconds`(int?, Busy 면 null) · `Busy`(bool) · `Position`(Busy 일 때 1부터, 아니면 0). **대기열 상태가 바뀔 때만** 보낸다(매초 아님).
- 방이 만들어지면 기존 `Lobby(내 좌석, 사람 수, 사람 수)` → 첫 `StateUpdate` 순서.
- 매치 중 사람 이탈 = **그 방만** `SessionFaulted("좌석 N 연결 끊김")` 후 종료(M5 전 정책).
- 대기 중 게임 액션 수신 = 대기열에서 빼고 연결을 닫는다(사유 `not in a match`).
- 레이트리밋(버스트 20, 초당 10) · 16KB 상한 · 텍스트 프레임만 · 깨진 JSON 종료 · 받은 액션 `Seat` 을 연결 좌석으로 덮어쓰기 — 유지.
- `Tichu.Net`(core/src/Tichu.Net) 변경은 **Unity 미러** `Assets/_Project/Net/` 에 그대로 복사하고, 바꾼 core 테스트도 `Assets/_Project/Tests/EditMode/` 에 복사한다. 서버 코드(`Tichu.Server`)는 미러하지 않는다.
- Unity 는 NUnit 3.5 — `Does.Not.Contain(object)`·`CollectionAssert` 등 NUnit 4 전용 API 를 미러 테스트에 쓰지 않는다. dotnet NUnit 4 는 `CollectionAssert`·`[Timeout]` 불가(명시적 데드라인 사용).
- ⚠️ Unity 에서 `Tichu.Core.Tests` **전체 실행 금지**(10만 판 시뮬이 에디터를 멈춘다). 클래스 필터로만.
- ⚠️ 서버가 떠 있으면 core 재빌드 시 DLL 이 잠긴다 — 빌드 전 서버 종료.

## Review Focus

1. **묶이는 순간 끊긴 연결** — 대기열에서 방으로 옮겨지는 바로 그때 끊기면, 그 방은 깨끗이 폴트로 끝나고 남은 사람은 `SessionFaulted` 를 받아야 한다(대기열·방 어디에도 갇히지 않음). → Task 3 `Leave_after_grouping_is_forwarded_to_the_room`, Task 4 `Leave_before_start_faults_the_room_on_start`.
2. **가장 오래 기다린 사람이 나감** — 카운트다운 기준이 다음 사람의 입장 시각으로 바뀌고, 남은 사람에게 새 `Queued` 가 가야 한다. → Task 3 `Leaving_the_queue_renotifies_and_resets_the_countdown_base`.
3. **상한에서 4명이 모임** — 즉시 시작하지 않고 Busy 순번만 받다가, 방이 비면 4명 방으로 시작한다. → Task 3 `Busy_queue_starts_when_a_room_frees`.
4. **구 v1 앱 접속** — 업데이트 안내 후 닫히고, 대기열 인원(`Waiting`)에 세어지지 않는다. → Task 5 `Rejected_connection_does_not_join_the_queue`.
5. **매치 종료 후 서버가 닫는 연결** — 정상 종료 뒤의 닫기를 이탈 폴트로 오인하지 않는다(결과 화면 대신 메뉴로 튕기는 회귀). → Task 4 `Normal_end_closes_links_without_fault`, Task 6 `No_fault_is_raised_when_server_closes_connections_after_match_end`.

---

## 파일 구조

| 파일 | 책임 | 상태 |
|---|---|---|
| `core/src/Tichu.Net/Protocol/ServerEvent.cs` | `Queued` 종류·필드·팩토리 추가 | 수정(+미러) |
| `core/src/Tichu.Net/ProtocolVersion.cs` | `Current = 2` | 수정(+미러) |
| `core/src/Tichu.Net/SeatRotation.cs` | `ToLocal` 이 Queued 필드도 복사 | 수정(+미러) |
| `core/src/Tichu.Server/SeatPicker.cs` | 사람 좌석 무작위 선택(순수) | 신규 |
| `core/src/Tichu.Server/ServerOptions.cs` | `--fill-after`·`--max-rooms` 추가, `--seats` 삭제 | 수정 |
| `core/src/Tichu.Server/Matchmaking/PlayerLink.cs` | `IPlayerLink`·`ILinkOwner`·`IRoom` 인터페이스 | 신규 |
| `core/src/Tichu.Server/Matchmaking/Matchmaker.cs` | 대기열·충원·상한·`Queued` 송신 | 신규 |
| `core/src/Tichu.Server/Matchmaking/Room.cs` | 매치 하나의 수명, `ITichuTransport` | 신규 |
| `core/src/Tichu.Server/Connection.cs` | 소켓 하나(송신 큐·수신 루프·검사·거절) | 신규(기존 내부 클래스 승격) |
| `core/src/Tichu.Server/ServerApp.cs` | 조립(매치메이커·틱 타이머·`/ws`) | 수정 |
| `core/src/Tichu.Server/Program.cs` | 사용법·배너 | 수정 |
| `core/src/Tichu.Server/WebSocketServerTransport.cs` · `MatchHost.cs` · `SeatTable.cs` | — | **삭제** |
| `core/tests/Tichu.Server.Tests/FakeLink.cs` | 테스트용 링크(+`IClientTransport`)·가짜 방 | 신규 |
| `core/tests/Tichu.Server.Tests/MatchmakerTests.cs` · `RoomTests.cs` · `SeatPickerTests.cs` · `ConnectionTests.cs` | 단위·소켓 테스트 | 신규 |
| `core/tests/Tichu.Server.Tests/EndToEndTests.cs` | 매칭 E2E | 재작성 |
| `core/tests/Tichu.Server.Tests/ServerTransportTests.cs` · `SeatTableTests.cs` | — | **삭제**(내용은 ConnectionTests·RoomTests 로) |
| `deploy/server/server.env.default` · `tools/smoke-server.ps1` | 새 인자 · `Queued` 기대 | 수정 |
| `Assets/_Project/Presentation/Net/LobbyWaiter.cs` | `Queued` 처리·카운트다운 문구 | 수정 |
| `Assets/_Project/Presentation/Shell/MenuShellPresenter.cs` · `Views/MenuShellView.cs` | "친구방" → "온라인 대전" | 수정 |

### 테스트 실행 명령(모든 태스크 공통)

- core: `dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~<클래스>" --nologo`
- server: `dotnet test core/tests/Tichu.Server.Tests/Tichu.Server.Tests.csproj --nologo` (필터는 `--filter "FullyQualifiedName~<클래스>"`)
- Unity(에디터 열림, MCP raw HTTP): 스크래치의 헬퍼 `mcp.py`·`unity_tests.py`(경로: 세션 스크래치패드, 없으면 `%TEMP%\claude\C--Users-user-Desktop-Project-Tichu\94c820c5-fea3-4a03-8906-29369accd906\scratchpad\` 에서 복사).
  1. `python mcp.py refresh_unity '{"mode":"force","scope":"all","compile":"request","wait_for_ready":true}'` (새 파일·삭제가 있으면 scope `all`)
  2. 약 15초 뒤 `python mcp.py read_console '{"action":"get","types":["error"],"count":10}'` → `CS` 에러 0
  3. `python unity_tests.py names <정규화 전체이름...>` 또는 `python unity_tests.py assembly Tichu.Presentation.Tests` → 종료코드 0 = 통과(1 실패, 2 필터 미스, 3 이상)
- Unity 대안(에디터가 닫혀 있을 때): `E:\6000.3.17f1\Editor\Unity.exe -batchmode -runTests -projectPath <프로젝트> -testPlatform EditMode -testFilter <접두어> -testResults %TEMP%\r.xml -logFile %TEMP%\r.log`

---

### Task 1: 프로토콜 v2 — `Queued` 이벤트

**Files:**
- Modify: `core/src/Tichu.Net/Protocol/ServerEvent.cs`
- Modify: `core/src/Tichu.Net/ProtocolVersion.cs:13`
- Modify: `core/src/Tichu.Net/SeatRotation.cs:28-44`
- Test: `core/tests/Tichu.Core.Tests/ProtocolJsonTests.cs`, `ProtocolVersionTests.cs`, `SeatRotationTests.cs`
- Mirror: 위 소스 3개 → `Assets/_Project/Net/...`, 테스트 3개 → `Assets/_Project/Tests/EditMode/...`

**Interfaces:**
- Produces: `ServerEventKind.Queued`; `ServerEvent.Waiting:int`, `FillInSeconds:int?`, `Busy:bool`, `Position:int`; `static ServerEvent ServerEvent.Queued(int waiting, int? fillInSeconds, bool busy, int position)`; `ProtocolVersion.Current == 2`.
- 주의: Unity `LobbyWaiter` 는 Task 7 전까지 `Queued` 를 "매치 시작"으로 오인한다(기본 분기). 이 브랜치의 서버가 `Queued` 를 보내기 시작하는 것은 Task 5 이므로, Task 7 이 끝나기 전에 실기 접속하지 않는다.

- [ ] **Step 1: 실패하는 테스트 작성** — `ProtocolJsonTests.cs` 의 서버 이벤트 왕복 테스트(Lobby 단언 바로 뒤, 같은 메서드 끝)에 추가:

```csharp
            var waiting = RoundTrip(ServerEvent.Queued(2, 18, false, 0));
            Assert.That(waiting.Kind, Is.EqualTo(ServerEventKind.Queued));
            Assert.That(waiting.Waiting, Is.EqualTo(2));
            Assert.That(waiting.FillInSeconds, Is.EqualTo(18));
            Assert.That(waiting.Busy, Is.False);
            Assert.That(waiting.Position, Is.EqualTo(0));

            var busy = RoundTrip(ServerEvent.Queued(5, null, true, 3));
            Assert.That(busy.FillInSeconds, Is.Null, "Busy 면 카운트다운이 없다");
            Assert.That(busy.Busy, Is.True);
            Assert.That(busy.Position, Is.EqualTo(3));
```

`ProtocolVersionTests.cs` 에 추가:

```csharp
        [Test]
        public void Matchmaking_protocol_is_version_2()
        {
            // M3 가 Queued 를 더했다 — v1 앱은 관문에서 업데이트 안내를 받아야 한다.
            Assert.That(ProtocolVersion.Current, Is.EqualTo(2));
            Assert.That(ProtocolVersion.Accepts("1"), Is.False);
        }
```

`SeatRotationTests.cs` 에 추가:

```csharp
        [Test]
        public void Queued_fields_survive_rotation()
        {
            var local = SeatRotation.ToLocal(ServerEvent.Queued(3, 12, true, 2), 1);
            Assert.That(local.Kind, Is.EqualTo(ServerEventKind.Queued));
            Assert.That(local.Waiting, Is.EqualTo(3));
            Assert.That(local.FillInSeconds, Is.EqualTo(12));
            Assert.That(local.Busy, Is.True);
            Assert.That(local.Position, Is.EqualTo(2));
        }
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~ProtocolJsonTests|FullyQualifiedName~ProtocolVersionTests|FullyQualifiedName~SeatRotationTests" --nologo`
Expected: 컴파일 실패 — `ServerEvent.Queued` 없음.

- [ ] **Step 3: 구현** — `ServerEvent.cs`: 열거형 끝에 `Queued` 를 더하고(`Lobby,` 뒤 `Queued`), 클래스에 필드·팩토리:

```csharp
        /// <summary>Queued: 지금 대기열에 있는 사람 수(나 포함).</summary>
        public int Waiting { get; set; }

        /// <summary>Queued: 내 묶음이 AI 충원으로 시작하기까지 남은 초. Busy 면 null.</summary>
        public int? FillInSeconds { get; set; }

        /// <summary>Queued: 방 상한에 걸려 기다리는 중인가.</summary>
        public bool Busy { get; set; }

        /// <summary>Queued: Busy 일 때 내 대기 순번(1부터). 아니면 0.</summary>
        public int Position { get; set; }
```

```csharp
        public static ServerEvent Queued(int waiting, int? fillInSeconds, bool busy, int position) =>
            new ServerEvent
            {
                Kind = ServerEventKind.Queued,
                Waiting = waiting, FillInSeconds = fillInSeconds, Busy = busy, Position = position
            };
```

`ProtocolVersion.cs`: `public const int Current = 2;`

`SeatRotation.ToLocal` 의 객체 초기화에 `Required = ev.Required,` 뒤로:

```csharp
                Waiting = ev.Waiting,
                FillInSeconds = ev.FillInSeconds,
                Busy = ev.Busy,
                Position = ev.Position,
```

- [ ] **Step 4: 통과 확인** — Step 2 명령. Expected: PASS(새 3개 포함, 기존 실패 0).

- [ ] **Step 5: Unity 미러 복사·확인**

```bash
cp core/src/Tichu.Net/Protocol/ServerEvent.cs Assets/_Project/Net/Protocol/ServerEvent.cs
cp core/src/Tichu.Net/ProtocolVersion.cs Assets/_Project/Net/ProtocolVersion.cs
cp core/src/Tichu.Net/SeatRotation.cs Assets/_Project/Net/SeatRotation.cs
cp core/tests/Tichu.Core.Tests/ProtocolJsonTests.cs Assets/_Project/Tests/EditMode/ProtocolJsonTests.cs
cp core/tests/Tichu.Core.Tests/ProtocolVersionTests.cs Assets/_Project/Tests/EditMode/ProtocolVersionTests.cs
cp core/tests/Tichu.Core.Tests/SeatRotationTests.cs Assets/_Project/Tests/EditMode/SeatRotationTests.cs
diff -rq core/src/Tichu.Net Assets/_Project/Net -x "*.meta" -x bin -x obj -x "*.csproj" -x "*.asmdef"
```
Expected: diff 출력 없음. Unity 컴파일 후 `python unity_tests.py names Tichu.Core.Tests.ProtocolJsonTests Tichu.Core.Tests.ProtocolVersionTests Tichu.Core.Tests.SeatRotationTests` → 0. (미러 테스트의 네임스페이스는 core 와 같다 — 파일 상단 `namespace` 로 확인.)

- [ ] **Step 6: 커밋**

```bash
git add core/src/Tichu.Net core/tests/Tichu.Core.Tests Assets/_Project/Net Assets/_Project/Tests/EditMode
git commit -m "feat(net): 프로토콜 v2 — 대기열 상태 이벤트 Queued"
```

---

### Task 2: 서버 옵션 `--fill-after`·`--max-rooms` + `SeatPicker`

**Files:**
- Create: `core/src/Tichu.Server/SeatPicker.cs`
- Modify: `core/src/Tichu.Server/ServerOptions.cs`
- Test: `core/tests/Tichu.Server.Tests/SeatPickerTests.cs`, `ServerOptionsTests.cs`

**Interfaces:**
- Produces: `ServerOptions.FillAfterSeconds:int`(기본 30), `ServerOptions.MaxRooms:int`(기본 2); `static int[] SeatPicker.Pick(int humans, Random rng)` — 0~3 중 중복 없는 `humans` 개, 순서는 사람 순서와 대응.
- `--seats`/`Seats` 는 이 태스크에서 **아직 지우지 않는다**(기존 전송이 쓰는 중). Task 5 에서 삭제.

- [ ] **Step 1: 실패하는 테스트** — `SeatPickerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Tichu.Server.Tests
{
    public class SeatPickerTests
    {
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void Seats_are_distinct_and_in_range(int humans)
        {
            for (int seed = 0; seed < 200; seed++)
            {
                var seats = SeatPicker.Pick(humans, new Random(seed));
                Assert.That(seats.Length, Is.EqualTo(humans));
                Assert.That(seats.Distinct().Count(), Is.EqualTo(humans));
                Assert.That(seats.All(s => s >= 0 && s <= 3), Is.True);
            }
        }

        [Test]
        public void A_solo_player_can_land_on_every_seat()
        {
            var seen = new HashSet<int>();
            for (int seed = 0; seed < 200; seed++) seen.Add(SeatPicker.Pick(1, new Random(seed))[0]);
            Assert.That(seen.Count, Is.EqualTo(4), "고정 좌석이면 무작위 배정이 아니다");
        }

        [Test]
        public void Two_players_are_sometimes_partners_and_sometimes_opponents()
        {
            bool partners = false, opponents = false;
            for (int seed = 0; seed < 200; seed++)
            {
                var s = SeatPicker.Pick(2, new Random(seed));
                if ((s[0] - s[1]) % 2 == 0) partners = true; else opponents = true;
            }
            Assert.That(partners && opponents, Is.True);
        }

        [TestCase(0)]
        [TestCase(5)]
        public void Out_of_range_counts_throw(int humans)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SeatPicker.Pick(humans, new Random(1)));
        }
    }
}
```

`ServerOptionsTests.cs`: `Defaults_match_the_spec` 끝에

```csharp
            Assert.That(o.FillAfterSeconds, Is.EqualTo(30));
            Assert.That(o.MaxRooms, Is.EqualTo(2));
```

`All_flags_parse` 의 인자 배열 끝에 `"--fill-after", "5", "--max-rooms", "3"` 를 더하고 단언

```csharp
            Assert.That(o.FillAfterSeconds, Is.EqualTo(5));
            Assert.That(o.MaxRooms, Is.EqualTo(3));
```

`Bad_arguments_throw` 에 케이스 추가:

```csharp
        [TestCase("--fill-after", "-1")]
        [TestCase("--fill-after", "x")]
        [TestCase("--max-rooms", "0")]
        [TestCase("--max-rooms", "x")]
```

- [ ] **Step 2: 실패 확인** — `dotnet test core/tests/Tichu.Server.Tests/Tichu.Server.Tests.csproj --filter "FullyQualifiedName~SeatPickerTests|FullyQualifiedName~ServerOptionsTests" --nologo` → 컴파일 실패(`SeatPicker`, `FillAfterSeconds` 없음).

- [ ] **Step 3: 구현** — `SeatPicker.cs`:

```csharp
using System;

namespace Tichu.Server
{
    /// <summary>
    /// 방이 만들어질 때 사람 좌석을 고른다. 0~3 을 섞어 앞에서 humans 개 — 사람 둘이 같은 팀일
    /// 수도 상대일 수도 있다(스펙 §4). 클라이언트는 "나 = 아래" 로 회전하므로 어느 좌석이든 같다.
    /// </summary>
    public static class SeatPicker
    {
        public static int[] Pick(int humans, Random rng)
        {
            if (humans < 1 || humans > 4) throw new ArgumentOutOfRangeException(nameof(humans));
            var seats = new[] { 0, 1, 2, 3 };
            for (int i = seats.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (seats[i], seats[j]) = (seats[j], seats[i]);
            }
            var picked = new int[humans];
            Array.Copy(seats, picked, humans);
            return picked;
        }
    }
}
```

`ServerOptions.cs`: 속성 추가(`Port` 다음):

```csharp
        /// <summary>대기열에서 가장 오래 기다린 사람이 이 초를 채우면 빈자리를 AI 로 채워 시작한다(M3 스펙 §1).</summary>
        public int FillAfterSeconds { get; set; } = 30;
        /// <summary>동시에 도는 방 상한. 꽉 차면 대기열에서 순번을 안내한다. 확정은 Lightsail 실측 후.</summary>
        public int MaxRooms { get; set; } = 2;
```

`Parse` 의 switch 에:

```csharp
                    case "--fill-after": o.FillAfterSeconds = ParseMin(key, value, 0); break;
                    case "--max-rooms":  o.MaxRooms = ParseMin(key, value, 1); break;
```

헬퍼:

```csharp
        private static int ParseMin(string key, string value, int min)
        {
            int v = ParseInt(key, value);
            return v >= min ? v : throw new ArgumentException($"{key} 는 {min} 이상: {value}");
        }
```

- [ ] **Step 4: 통과 확인** — Step 2 명령 → PASS. 이어서 서버 테스트 전체 `dotnet test core/tests/Tichu.Server.Tests/Tichu.Server.Tests.csproj --nologo` → 기존 포함 전부 PASS.

- [ ] **Step 5: 커밋**

```bash
git add core/src/Tichu.Server/SeatPicker.cs core/src/Tichu.Server/ServerOptions.cs core/tests/Tichu.Server.Tests/SeatPickerTests.cs core/tests/Tichu.Server.Tests/ServerOptionsTests.cs
git commit -m "feat(server): 충원 대기·방 상한 옵션과 무작위 좌석 선택"
```

---

### Task 3: `Matchmaker` — 대기열·충원·상한

**Files:**
- Create: `core/src/Tichu.Server/Matchmaking/PlayerLink.cs`
- Create: `core/src/Tichu.Server/Matchmaking/Matchmaker.cs`
- Create: `core/tests/Tichu.Server.Tests/FakeLink.cs`
- Test: `core/tests/Tichu.Server.Tests/MatchmakerTests.cs`

**Interfaces:**
- Produces(`namespace Tichu.Server`):

```csharp
public interface IPlayerLink
{
    int Id { get; }
    /// <summary>지금 이 연결의 메시지를 받는 곳(대기열 또는 방). 스레드 간 읽기 — 구현은 volatile.</summary>
    ILinkOwner? Owner { get; set; }
    /// <summary>큐에 넣기만 하고 막히지 않는다(잠금 안에서 불려도 된다).</summary>
    void Send(ServerEvent ev);
    /// <summary>남은 송신을 마친 뒤 정상 종료한다. 처음 호출만 유효.</summary>
    void Close(string reason);
}
public interface ILinkOwner
{
    void Receive(IPlayerLink from, ClientAction a);
    void Left(IPlayerLink who);
}
public interface IRoom : ILinkOwner
{
    /// <summary>AI 생성·Lobby 송신·세션 시작. 매치메이커 잠금 밖에서 한 번 불린다.</summary>
    void Start();
}
```

- `Matchmaker(int fillAfterSeconds, int maxRooms, Func<DateTime> utcNow, Func<int, IReadOnlyList<IPlayerLink>, IRoom> openRoom, Action<string> log)` — `openRoom(방 번호, 사람들)` 은 **매치메이커 잠금 안에서** 불리며, 반환 전에 각 사람의 `Owner` 를 방으로 바꿔야 한다(가볍게: 저장·좌석 배정만). `Start()` 는 잠금 밖.
- 공개: `void Enqueue(IPlayerLink)`, `void Tick()`, `void RoomClosed()`, `int QueuedCount`, `int ActiveRooms`, 그리고 `ILinkOwner` 구현.

- [ ] **Step 1: 테스트 헬퍼 작성** — `FakeLink.cs`(Task 4·6 에서도 쓴다):

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Tichu.Net;

namespace Tichu.Server.Tests
{
    /// <summary>메모리 안 연결. 서버 쪽에선 IPlayerLink, 테스트 클라이언트 쪽에선 IClientTransport 다 —
    /// ScriptedClient 를 그대로 붙일 수 있다(Submit = 소유자에게 Receive).</summary>
    internal sealed class FakeLink : IPlayerLink, IClientTransport
    {
        private static int _ids;
        private readonly object _gate = new object();
        private readonly List<ServerEvent> _all = new List<ServerEvent>();
        private readonly List<ServerEvent> _undrained = new List<ServerEvent>();
        private volatile ILinkOwner? _owner;
        private string? _closedReason;

        public int Id { get; } = Interlocked.Increment(ref _ids);
        public ILinkOwner? Owner { get => _owner; set => _owner = value; }

        public string? ClosedReason { get { lock (_gate) return _closedReason; } }

        public List<ServerEvent> Received { get { lock (_gate) return new List<ServerEvent>(_all); } }

        public List<ServerEvent> QueuedEvents => Received.Where(e => e.Kind == ServerEventKind.Queued).ToList();

        public void Send(ServerEvent ev)
        {
            lock (_gate) { _all.Add(ev); _undrained.Add(ev); }
        }

        public void Close(string reason)
        {
            lock (_gate) _closedReason ??= reason;
        }

        public IReadOnlyList<ServerEvent> Drain()
        {
            lock (_gate)
            {
                var r = new List<ServerEvent>(_undrained);
                _undrained.Clear();
                return r;
            }
        }

        public void Submit(ClientAction a) => Owner?.Receive(this, a);
    }

    /// <summary>매치메이커 단위 테스트용 방. 생성 때 Owner 를 자기로 바꾸고, 받은 것을 기록만 한다.</summary>
    internal sealed class FakeRoom : IRoom
    {
        public int Index { get; }
        public IReadOnlyList<IPlayerLink> Humans { get; }
        public bool Started { get; private set; }
        public List<IPlayerLink> LeftLinks { get; } = new List<IPlayerLink>();
        public List<ClientAction> ReceivedActions { get; } = new List<ClientAction>();

        public FakeRoom(int index, IReadOnlyList<IPlayerLink> humans)
        {
            Index = index;
            Humans = humans;
            foreach (var h in humans) h.Owner = this;
        }

        public void Start() => Started = true;
        public void Left(IPlayerLink who) { lock (LeftLinks) LeftLinks.Add(who); }
        public void Receive(IPlayerLink from, ClientAction a) { lock (ReceivedActions) ReceivedActions.Add(a); }
    }
}
```

- [ ] **Step 2: 실패하는 테스트** — `MatchmakerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Tichu.Net;

namespace Tichu.Server.Tests
{
    public class MatchmakerTests
    {
        private DateTime _now;
        private List<FakeRoom> _rooms = null!;
        private List<string> _log = null!;

        [SetUp]
        public void SetUp()
        {
            _now = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
            _rooms = new List<FakeRoom>();
            _log = new List<string>();
        }

        private Matchmaker Make(int fillAfter = 30, int maxRooms = 2) =>
            new Matchmaker(fillAfter, maxRooms, () => _now,
                (index, humans) => { var r = new FakeRoom(index, humans); _rooms.Add(r); return r; },
                m => _log.Add(m));

        private void Advance(double seconds) => _now = _now.AddSeconds(seconds);

        private static FakeLink[] Links(int n) => Enumerable.Range(0, n).Select(_ => new FakeLink()).ToArray();

        [Test]
        public void Four_players_open_a_room_at_once()
        {
            var mm = Make();
            var p = Links(4);
            foreach (var l in p) mm.Enqueue(l);

            Assert.That(_rooms.Count, Is.EqualTo(1));
            Assert.That(_rooms[0].Humans, Is.EqualTo(p));
            Assert.That(_rooms[0].Started, Is.True);
            Assert.That(p.All(l => l.Owner == _rooms[0]), Is.True, "묶인 연결은 방 소속이다");
            Assert.That(mm.QueuedCount, Is.EqualTo(0));
            Assert.That(mm.ActiveRooms, Is.EqualTo(1));
        }

        [Test]
        public void Fewer_than_four_wait_until_fill_after_then_open_with_those_present()
        {
            var mm = Make();
            var p = Links(3);
            foreach (var l in p) mm.Enqueue(l);

            Advance(29.9); mm.Tick();
            Assert.That(_rooms, Is.Empty, "30초 전엔 방이 없다");

            Advance(0.1); mm.Tick();
            Assert.That(_rooms.Count, Is.EqualTo(1));
            Assert.That(_rooms[0].Humans, Is.EqualTo(p));
        }

        [Test]
        public void Fill_timer_counts_from_the_oldest_waiter()
        {
            var mm = Make();
            var a = new FakeLink(); var b = new FakeLink();
            mm.Enqueue(a);
            Advance(20); mm.Enqueue(b);
            Advance(10); mm.Tick();

            Assert.That(_rooms.Count, Is.EqualTo(1));
            Assert.That(_rooms[0].Humans, Is.EqualTo(new[] { a, b }), "늦게 온 b 도 같은 방이다");
        }

        [Test]
        public void Fifth_player_keeps_its_own_arrival_time()
        {
            var mm = Make();
            foreach (var l in Links(4)) mm.Enqueue(l);       // 방 0, 즉시
            Advance(10);
            var fifth = new FakeLink();
            mm.Enqueue(fifth);

            Advance(29); mm.Tick();
            Assert.That(_rooms.Count, Is.EqualTo(1), "다섯째의 30초는 그 사람의 입장 시각부터");
            Advance(1); mm.Tick();
            Assert.That(_rooms.Count, Is.EqualTo(2));
            Assert.That(_rooms[1].Humans, Is.EqualTo(new[] { fifth }));
        }

        [Test]
        public void Queued_carries_the_countdown_of_the_oldest_waiter()
        {
            var mm = Make();
            var a = new FakeLink(); var b = new FakeLink();
            mm.Enqueue(a);
            Advance(10); mm.Enqueue(b);

            var q = b.QueuedEvents.Last();
            Assert.That(q.Waiting, Is.EqualTo(2));
            Assert.That(q.FillInSeconds, Is.EqualTo(20));
            Assert.That(q.Busy, Is.False);
            Assert.That(q.Position, Is.EqualTo(0));
            Assert.That(a.QueuedEvents.Last().Waiting, Is.EqualTo(2), "인원이 바뀌면 먼저 온 사람도 다시 받는다");
        }

        [Test]
        public void Queued_is_sent_only_when_it_changes()
        {
            var mm = Make();
            var a = new FakeLink();
            mm.Enqueue(a);
            int before = a.QueuedEvents.Count;

            for (int i = 0; i < 5; i++) { Advance(1); mm.Tick(); }

            Assert.That(before, Is.EqualTo(1));
            Assert.That(a.QueuedEvents.Count, Is.EqualTo(1), "매초 보내지 않는다 — 카운트다운은 클라이언트가 센다");
        }

        [Test]
        public void Leaving_the_queue_renotifies_and_resets_the_countdown_base()
        {
            var mm = Make();
            var a = new FakeLink(); var b = new FakeLink();
            mm.Enqueue(a);
            Advance(10); mm.Enqueue(b);
            Advance(5);
            mm.Left(a);

            var q = b.QueuedEvents.Last();
            Assert.That(q.Waiting, Is.EqualTo(1));
            Assert.That(q.FillInSeconds, Is.EqualTo(25), "이제 가장 오래 기다린 사람은 b(입장 5초 전)");
            Advance(24); mm.Tick();
            Assert.That(_rooms, Is.Empty);
            Advance(1); mm.Tick();
            Assert.That(_rooms.Single().Humans, Is.EqualTo(new[] { b }));
        }

        [Test]
        public void Busy_when_max_rooms_reached_sends_position_without_countdown()
        {
            var mm = Make(maxRooms: 1);
            foreach (var l in Links(4)) mm.Enqueue(l);       // 방 0 → 상한
            var x = new FakeLink(); var y = new FakeLink();
            mm.Enqueue(x); mm.Enqueue(y);

            Assert.That(x.QueuedEvents.Last().Busy, Is.True);
            Assert.That(x.QueuedEvents.Last().Position, Is.EqualTo(1));
            Assert.That(x.QueuedEvents.Last().FillInSeconds, Is.Null);
            Assert.That(y.QueuedEvents.Last().Position, Is.EqualTo(2));

            Advance(60); mm.Tick();
            Assert.That(_rooms.Count, Is.EqualTo(1), "상한에선 30초가 지나도 방을 열지 않는다");
        }

        [Test]
        public void Busy_queue_starts_when_a_room_frees()
        {
            var mm = Make(maxRooms: 1);
            foreach (var l in Links(4)) mm.Enqueue(l);       // 방 0
            var waiting = Links(4);
            foreach (var l in waiting) mm.Enqueue(l);        // 4명이 모였지만 상한
            Assert.That(_rooms.Count, Is.EqualTo(1));

            mm.RoomClosed();

            Assert.That(_rooms.Count, Is.EqualTo(2));
            Assert.That(_rooms[1].Humans, Is.EqualTo(waiting));
            Assert.That(mm.ActiveRooms, Is.EqualTo(1));
        }

        [Test]
        public void Game_action_while_queued_closes_and_removes_the_link()
        {
            var mm = Make();
            var a = new FakeLink();
            mm.Enqueue(a);

            a.Submit(ClientAction.Pass(0, 1));

            Assert.That(a.ClosedReason, Is.EqualTo("not in a match"));
            Assert.That(mm.QueuedCount, Is.EqualTo(0));
        }

        [Test]
        public void Leave_after_grouping_is_forwarded_to_the_room()
        {
            // 연결의 수신 루프가 Owner(=대기열) 를 읽은 직후 묶인 경우 — 매치메이커가 방으로 넘긴다.
            var mm = Make();
            var p = Links(4);
            foreach (var l in p) mm.Enqueue(l);

            mm.Left(p[2]);
            mm.Receive(p[1], ClientAction.Pass(0, 1));

            Assert.That(_rooms[0].LeftLinks, Is.EqualTo(new[] { p[2] }));
            Assert.That(_rooms[0].ReceivedActions.Count, Is.EqualTo(1));
            Assert.That(p[1].ClosedReason, Is.Null, "방에 속한 연결은 대기 중 액션 규칙으로 닫지 않는다");
        }
    }
}
```

- [ ] **Step 3: 실패 확인** — `dotnet test core/tests/Tichu.Server.Tests/Tichu.Server.Tests.csproj --filter "FullyQualifiedName~MatchmakerTests" --nologo` → 컴파일 실패(`Matchmaker`, `IPlayerLink` 없음).

- [ ] **Step 4: 구현** — `PlayerLink.cs` 에 위 **Interfaces** 의 세 인터페이스를 그대로(`using Tichu.Net;`, `namespace Tichu.Server`). `Matchmaker.cs`:

```csharp
using System;
using System.Collections.Generic;
using Tichu.Net;

namespace Tichu.Server
{
    /// <summary>
    /// 랜덤 매칭 대기열(M3 스펙 §2). 4명이 모이면 즉시, 아니면 가장 오래 기다린 사람이 fillAfter 를
    /// 채우는 순간 그때 있는 사람으로 방을 연다(빈자리는 방이 AI 로 채운다). 방이 maxRooms 개면 대기열에
    /// 남기고 Busy·순번을 알린다.
    ///
    /// 동시성: 대기열 판단·방 생성(openRoom)·Queued 송신은 한 잠금 안에서 한다 — 묶인 연결은 같은
    /// 잠금 안에서 대기열을 떠나 방 소속(Owner)이 되므로 양쪽에 동시에 있는 연결이 없다. Queued 를 잠금
    /// 안에서 큐에 넣으므로 같은 연결의 Lobby(방 Start, 잠금 밖)보다 반드시 앞선다. Start 는 잠금 밖.
    /// </summary>
    public sealed class Matchmaker : ILinkOwner
    {
        public const int SeatCount = 4;

        private readonly TimeSpan _fillAfter;
        private readonly int _maxRooms;
        private readonly Func<DateTime> _now;
        private readonly Func<int, IReadOnlyList<IPlayerLink>, IRoom> _openRoom;
        private readonly Action<string> _log;
        private readonly object _gate = new object();
        private readonly List<Entry> _queue = new List<Entry>();
        private int _activeRooms;
        private int _nextRoom;
        private bool _busyLogged;

        private sealed class Entry
        {
            public IPlayerLink Link = null!;
            public DateTime Since;
            public (int Waiting, bool Busy, int Position)? LastSent;
        }

        public Matchmaker(int fillAfterSeconds, int maxRooms, Func<DateTime> utcNow,
            Func<int, IReadOnlyList<IPlayerLink>, IRoom> openRoom, Action<string> log)
        {
            _fillAfter = TimeSpan.FromSeconds(fillAfterSeconds);
            _maxRooms = maxRooms;
            _now = utcNow;
            _openRoom = openRoom;
            _log = log;
        }

        public int QueuedCount { get { lock (_gate) return _queue.Count; } }
        public int ActiveRooms { get { lock (_gate) return _activeRooms; } }

        public void Enqueue(IPlayerLink link)
        {
            int count;
            lock (_gate)
            {
                link.Owner = this;
                _queue.Add(new Entry { Link = link, Since = _now() });
                count = _queue.Count;
            }
            _log($"연결 #{link.Id} 대기열 입장 ({count}명)");
            Pump();
        }

        /// <summary>1초마다 서버 타이머가 부른다 — 충원 시각이 된 묶음을 연다.</summary>
        public void Tick() => Pump();

        /// <summary>방 하나가 끝났다(정상·폴트). 자리가 났으니 대기열을 다시 본다.</summary>
        public void RoomClosed()
        {
            lock (_gate) _activeRooms--;
            Pump();
        }

        public void Left(IPlayerLink who)
        {
            if (Remove(who))
            {
                _log($"연결 #{who.Id} 대기열 이탈");
                Pump();
                return;
            }
            // 수신 루프가 Owner 를 읽은 뒤 방으로 묶였다 — 방이 처리한다.
            var owner = who.Owner;
            if (owner != null && owner != this) owner.Left(who);
        }

        public void Receive(IPlayerLink from, ClientAction a)
        {
            if (Remove(from))
            {
                // 정상 클라이언트는 Lobby 전에 게임 액션을 보내지 않는다.
                _log($"연결 #{from.Id} 대기 중 게임 액션 — 연결 종료");
                from.Close("not in a match");
                Pump();
                return;
            }
            var owner = from.Owner;
            if (owner != null && owner != this) owner.Receive(from, a);
        }

        private bool Remove(IPlayerLink link)
        {
            lock (_gate) return _queue.RemoveAll(e => e.Link == link) > 0;
        }

        private void Pump()
        {
            var rooms = new List<IRoom>();
            lock (_gate)
            {
                DateTime now = _now();
                while (_queue.Count > 0 && _activeRooms < _maxRooms)
                {
                    int take;
                    if (_queue.Count >= SeatCount) take = SeatCount;
                    else if (now - _queue[0].Since >= _fillAfter) take = _queue.Count;
                    else break;

                    var humans = new List<IPlayerLink>(take);
                    for (int i = 0; i < take; i++) humans.Add(_queue[i].Link);
                    _queue.RemoveRange(0, take);
                    int index = _nextRoom++;
                    _activeRooms++;
                    rooms.Add(_openRoom(index, humans));   // 반환 전에 humans 의 Owner 가 방으로 바뀐다
                    _log($"방 {index} 배정 — 사람 {take}명");
                }

                bool busy = _activeRooms >= _maxRooms;
                if (busy && _queue.Count > 0 && !_busyLogged) { _busyLogged = true; _log($"방 상한 {_maxRooms} 도달 — 대기 {_queue.Count}명"); }
                if (!busy) _busyLogged = false;
                for (int i = 0; i < _queue.Count; i++)
                {
                    var e = _queue[i];
                    var key = (_queue.Count, busy, busy ? i + 1 : 0);
                    if (e.LastSent == key) continue;
                    e.LastSent = key;
                    int? fill = busy ? (int?)null : SecondsLeft(now, _queue[0].Since);
                    e.Link.Send(ServerEvent.Queued(key.Item1, fill, busy, key.Item3));
                }
            }
            foreach (var room in rooms) room.Start();
        }

        private int SecondsLeft(DateTime now, DateTime oldest) =>
            Math.Max(0, (int)Math.Ceiling((_fillAfter - (now - oldest)).TotalSeconds));
    }
}
```

- [ ] **Step 5: 통과 확인** — Step 3 명령 → 11개 PASS.

- [ ] **Step 6: 음성 대조**(되돌려 실패를 확인한 뒤 원복). 각각 수정 → Step 3 명령 → 지정 테스트만 FAIL 확인 → `git checkout -- core/src/Tichu.Server/Matchmaking/Matchmaker.cs` 대신 편집으로 원복(아직 미커밋이므로 편집 원복):
  1. `while` 조건에서 `&& _activeRooms < _maxRooms` 제거 → `Busy_when_max_rooms_reached...`·`Busy_queue_starts...` 실패.
  2. `Remove(who)` 대신 항상 false → `Leaving_the_queue_renotifies...` 실패.
  3. `_queue[0].Since` 를 `_queue[_queue.Count - 1].Since` 로 → `Fill_timer_counts_from_the_oldest_waiter` 실패.
  4. `if (e.LastSent == key) continue;` 제거 → `Queued_is_sent_only_when_it_changes` 실패.
  원복 후 Step 3 재실행 → 전부 PASS.

- [ ] **Step 7: 커밋**

```bash
git add core/src/Tichu.Server/Matchmaking core/tests/Tichu.Server.Tests/FakeLink.cs core/tests/Tichu.Server.Tests/MatchmakerTests.cs
git commit -m "feat(server): 랜덤 매칭 대기열 — 4명 즉시·30초 충원·방 상한 순번"
```

---

### Task 4: `Room` — 매치 하나의 수명

**Files:**
- Create: `core/src/Tichu.Server/Matchmaking/Room.cs`
- Test: `core/tests/Tichu.Server.Tests/RoomTests.cs`

**Interfaces:**
- Consumes: `IPlayerLink`, `ILinkOwner`, `IRoom`(Task 3), `ServerOptions`(Task 2: `Difficulty`, `Target`, `AiDelayMs`, `InterRoundPauseMs`, `Seed`), `MatchSession`, `PacedPimcAgent(seed, seat, config, budgetMs, delayMs)`, `PolicyConfig.For`, `PimcBudget.For`.
- Produces: `Room(int index, IReadOnlyList<IPlayerLink> humans, int[] seats, ServerOptions opt, Action<string> log, Action onClosed)` : `IRoom, ITichuTransport`. 생성자는 각 사람의 `Owner` 를 방으로 바꾸고 좌석 배정만 한다(가벼움 — 매치메이커 잠금 안에서 불린다). `onClosed` 는 방이 끝날 때 정확히 한 번.
- 로그 문구(E2E 가 기다린다): `방 {i} 시작 — 사람 {h} · AI {4-h} · seed {s} · {난이도} · 목표 {t}` / `방 {i} 좌석 {s} 이탈 — 매치 중단` / `방 {i} 종료`.

- [ ] **Step 1: 실패하는 테스트** — `RoomTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Tichu.Core.Tests;
using Tichu.GameFlow.Agents;
using Tichu.Net;

namespace Tichu.Server.Tests
{
    public class RoomTests
    {
        private readonly List<Room> _rooms = new List<Room>();
        private readonly List<FakeLink> _links = new List<FakeLink>();

        private static ServerOptions Fast(int target = 1, int aiDelay = 0) => new ServerOptions
        {
            Difficulty = Difficulty.Easy, Target = target, AiDelayMs = aiDelay, InterRoundPauseMs = 0, Seed = 7
        };

        private (Room room, Func<int> closedCount, List<string> log) Make(ServerOptions opt, int[] seats, params FakeLink[] humans)
        {
            int closed = 0;
            var log = new List<string>();
            var room = new Room(_rooms.Count, humans, seats, opt, m => { lock (log) log.Add(m); }, () => Interlocked.Increment(ref closed));
            _rooms.Add(room);
            _links.AddRange(humans);
            return (room, () => Volatile.Read(ref closed), log);
        }

        [TearDown]
        public async Task TearDown()
        {
            // 남은 세션 스레드를 끝낸다: 아직 열린 사람 연결을 모두 이탈시킨다.
            foreach (var l in _links) l.Owner?.Left(l);
            await Task.Delay(50);
            _rooms.Clear();
            _links.Clear();
        }

        private static async Task Until(Func<bool> condition, double seconds = 10)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) Assert.Fail($"{seconds}초 안에 조건이 성립하지 않았다");
                await Task.Delay(5);
            }
        }

        [Test]
        public async Task Lobby_precedes_the_first_state_update_and_names_the_seat()
        {
            var a = new FakeLink(); var b = new FakeLink();
            var (room, _, _) = Make(Fast(target: 1000, aiDelay: 50), new[] { 3, 1 }, a, b);
            Assert.That(a.Owner, Is.SameAs(room), "생성자가 소속을 방으로 바꾼다");

            room.Start();
            await Until(() => a.Received.Any(e => e.Kind == ServerEventKind.StateUpdate));

            var first = a.Received[0];
            Assert.That(first.Kind, Is.EqualTo(ServerEventKind.Lobby));
            Assert.That(first.YourSeat, Is.EqualTo(3));
            Assert.That(first.Connected, Is.EqualTo(2));
            Assert.That(first.Required, Is.EqualTo(2));
            Assert.That(b.Received[0].YourSeat, Is.EqualTo(1));
        }

        [Test]
        public async Task A_human_and_three_ai_finish_and_the_room_closes_once()
        {
            var a = new FakeLink();
            var (room, closed, log) = Make(Fast(), new[] { 2 }, a);
            room.Start();
            var client = new ScriptedClient(2, a);

            await Until(() => { client.Pump(); return client.MatchEnded; }, 60);
            await Until(() => closed() == 1);

            Assert.That(client.FaultReason, Is.Null);
            Assert.That(a.ClosedReason, Is.EqualTo("match over"));
            lock (log) Assert.That(log.Exists(m => m.StartsWith("방 0 시작 — 사람 1 · AI 3 · seed 7 ")), Is.True);
            await Task.Delay(100);
            Assert.That(closed(), Is.EqualTo(1), "onClosed 는 한 번만");
        }

        [Test]
        public async Task Normal_end_closes_links_without_fault()
        {
            var a = new FakeLink();
            var (room, closed, log) = Make(Fast(), new[] { 0 }, a);
            room.Start();
            var client = new ScriptedClient(0, a);
            await Until(() => { client.Pump(); return client.MatchEnded; }, 60);
            await Until(() => closed() == 1);

            room.Left(a);                                    // 서버가 닫은 연결의 끊김 통보가 늦게 온다
            client.Pump();

            Assert.That(a.Received.Any(e => e.Kind == ServerEventKind.SessionFaulted), Is.False);
            lock (log) Assert.That(log.Exists(m => m.Contains("이탈")), Is.False);
        }

        [Test]
        public async Task Leaving_mid_match_faults_only_this_room()
        {
            var a1 = new FakeLink(); var a2 = new FakeLink(); var b1 = new FakeLink();
            var (roomA, closedA, logA) = Make(Fast(target: 1000, aiDelay: 50), new[] { 0, 2 }, a1, a2);
            var (roomB, closedB, _) = Make(Fast(target: 1000, aiDelay: 50), new[] { 1 }, b1);
            roomA.Start(); roomB.Start();
            await Until(() => a1.Received.Any(e => e.Kind == ServerEventKind.StateUpdate));

            roomA.Left(a2);
            await Until(() => closedA() == 1);

            var fault = a1.Received.Single(e => e.Kind == ServerEventKind.SessionFaulted);
            Assert.That(fault.Reason, Is.EqualTo("좌석 2 연결 끊김"));
            Assert.That(a1.ClosedReason, Is.EqualTo("match over"));
            lock (logA) Assert.That(logA.Exists(m => m == "방 0 좌석 2 이탈 — 매치 중단"), Is.True);

            await Task.Delay(300);
            Assert.That(closedB(), Is.EqualTo(0), "다른 방은 계속 돈다");
            Assert.That(b1.ClosedReason, Is.Null);
            Assert.That(b1.Received.Any(e => e.Kind == ServerEventKind.SessionFaulted), Is.False);
        }

        [Test]
        public async Task Leave_before_start_faults_the_room_on_start()
        {
            var a = new FakeLink(); var b = new FakeLink();
            var (room, closed, _) = Make(Fast(target: 1000, aiDelay: 50), new[] { 0, 1 }, a, b);

            room.Left(b);                                    // 묶인 직후, Start 전에 끊겼다
            room.Start();
            await Until(() => closed() == 1);

            Assert.That(a.Received.Exists(e => e.Kind == ServerEventKind.SessionFaulted && e.Reason == "좌석 1 연결 끊김"), Is.True);
            Assert.That(a.ClosedReason, Is.EqualTo("match over"));
        }

        [Test]
        public void Received_action_takes_the_seat_of_its_link()
        {
            var a = new FakeLink();
            var (room, _, _) = Make(Fast(), new[] { 2 }, a);
            ClientAction? got = null;
            room.OnClientAction(x => got = x);

            room.Receive(a, ClientAction.Pass(0, 1));        // 좌석 0 을 사칭

            Assert.That(got!.Seat, Is.EqualTo(2));
        }

        [Test]
        public void SendTo_reaches_only_that_seat_and_skips_ai_seats()
        {
            var a = new FakeLink(); var b = new FakeLink();
            var (room, _, log) = Make(Fast(), new[] { 0, 3 }, a, b);

            room.SendTo(3, ServerEvent.ActionRejected(5, "x"));
            room.SendTo(1, ServerEvent.SessionFaulted("ai-seat"));   // AI 좌석 — 연결 없음

            Assert.That(b.Received.Single().ActionSeq, Is.EqualTo(5));
            Assert.That(a.Received, Is.Empty);
            lock (log)
            {
                Assert.That(log.Exists(m => m.Contains("좌석 3 ActionRejected: x")), Is.True, "거절·폴트는 로그에 남긴다");
                Assert.That(log.Exists(m => m.Contains("좌석 1 SessionFaulted: ai-seat")), Is.True);
            }
        }
    }
}
```

- [ ] **Step 2: 실패 확인** — `dotnet test core/tests/Tichu.Server.Tests/Tichu.Server.Tests.csproj --filter "FullyQualifiedName~RoomTests" --nologo` → 컴파일 실패(`Room` 없음).

- [ ] **Step 3: 구현** — `Room.cs`(기존 `MatchHost` 의 수명 로직을 방 단위로 옮긴 것):

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
    /// 매치 하나(M3 스펙 §2.1). 빈자리를 PIMC 로 채워 MatchSession 을 돌리고, 세션이 끝나면(정상·폴트)
    /// 자기 연결만 닫고 onClosed 로 매치메이커에 자리를 돌려준다. 매치 중 사람이 끊기면 그 방에만
    /// SessionFaulted 를 보낸다 — MatchSession.Stop() 은 아무것도 방송하지 않으므로 방송은 여기 몫이다.
    /// 연결은 이 방에만 속하므로 이전 매치 연결을 거르는 세대 번호가 필요 없다.
    /// </summary>
    public sealed class Room : IRoom, ITichuTransport
    {
        private const int SeatCount = 4;

        private readonly int _index;
        private readonly ServerOptions _opt;
        private readonly Action<string> _log;
        private readonly Action _onClosed;
        private readonly IPlayerLink?[] _bySeat = new IPlayerLink?[SeatCount];
        private readonly object _gate = new object();
        private Action<ClientAction>? _handler;   // _gate
        private MatchSession? _session;           // _gate, Start 이후
        private int _faultedSeat = -1;            // _gate
        private int _ending;

        /// <summary>가볍다 — 매치메이커 잠금 안에서 불린다. 좌석 배정과 소속 전환만 한다.</summary>
        public Room(int index, IReadOnlyList<IPlayerLink> humans, int[] seats, ServerOptions opt,
            Action<string> log, Action onClosed)
        {
            if (humans.Count != seats.Length) throw new ArgumentException("humans and seats differ in length");
            _index = index;
            _opt = opt;
            _log = log;
            _onClosed = onClosed;
            for (int i = 0; i < humans.Count; i++)
            {
                _bySeat[seats[i]] = humans[i];
                humans[i].Owner = this;
            }
        }

        public void Start()
        {
            ulong seed = _opt.Seed is ulong s ? unchecked(s + (ulong)_index) : unchecked((ulong)DateTime.UtcNow.Ticks);
            var config = PolicyConfig.For(_opt.Difficulty);
            int budgetMs = PimcBudget.For(_opt.Difficulty);

            var agents = new IAgent?[SeatCount];
            int humans = 0;
            for (int seat = 0; seat < SeatCount; seat++)
            {
                if (_bySeat[seat] != null) { humans++; continue; }
                agents[seat] = new PacedPimcAgent(seed, seat, config, budgetMs, _opt.AiDelayMs);
            }

            // Lobby 가 첫 StateUpdate 보다 먼저 큐에 들어가야 한다(클라이언트 LobbyWaiter) — 세션 시작 전에 보낸다.
            for (int seat = 0; seat < SeatCount; seat++)
                _bySeat[seat]?.Send(ServerEvent.Lobby(seat, humans, humans));

            var session = new MatchSession(
                agents, this, seed,
                targetScore: _opt.Target,
                interRoundPause: TimeSpan.FromMilliseconds(_opt.InterRoundPauseMs));
            session.Start();
            lock (_gate)
            {
                _session = session;
                if (_faultedSeat >= 0) session.Stop();   // 시작 전에 이미 누가 나갔다
            }
            _log($"방 {_index} 시작 — 사람 {humans} · AI {SeatCount - humans} · seed {seed} · {_opt.Difficulty} · 목표 {_opt.Target}");

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
            _bySeat[seat]?.Send(ev);                    // AI 좌석은 연결이 없다 → 버린다
        }

        public void OnClientAction(Action<ClientAction> handler)
        {
            lock (_gate) _handler = handler;
        }

        public void Receive(IPlayerLink from, ClientAction a)
        {
            int seat = SeatOf(from);
            if (seat < 0) return;
            a.Seat = seat;                              // 사칭 방지 — 세션은 a.Seat 로 우편함을 고른다
            Action<ClientAction>? h;
            lock (_gate) h = _handler;
            h?.Invoke(a);
        }

        public void Left(IPlayerLink who)
        {
            if (Volatile.Read(ref _ending) == 1) return;   // 종료 중 우리가 닫은 연결
            int seat = SeatOf(who);
            if (seat < 0) return;
            lock (_gate)
            {
                if (_session != null && _session.Join(TimeSpan.Zero)) return;   // 이미 정상 종료 — 폴트 아님
                if (_faultedSeat < 0) _faultedSeat = seat;
                _session?.Stop();   // 방송은 세션 스레드가 끝난 뒤 End 가 한다
            }
        }

        private int SeatOf(IPlayerLink link)
        {
            for (int s = 0; s < SeatCount; s++)
                if (_bySeat[s] == link) return s;
            return -1;
        }

        private void End()
        {
            if (Interlocked.Exchange(ref _ending, 1) == 1) return;
            int faulted;
            lock (_gate) faulted = _faultedSeat;
            if (faulted >= 0)
            {
                _log($"방 {_index} 좌석 {faulted} 이탈 — 매치 중단");
                for (int s = 0; s < SeatCount; s++)
                    SendTo(s, ServerEvent.SessionFaulted($"좌석 {faulted} 연결 끊김"));
            }
            _log($"방 {_index} 종료");
            for (int s = 0; s < SeatCount; s++) _bySeat[s]?.Close("match over");
            _onClosed();
        }
    }
}
```

- [ ] **Step 4: 통과 확인** — Step 2 명령 → 7개 PASS. 서버 테스트 전체도 PASS.

- [ ] **Step 5: 음성 대조**(편집 → 지정 테스트 FAIL 확인 → 원복):
  1. `Left` 의 `_session.Join(TimeSpan.Zero)` 줄 제거 → `Normal_end_closes_links_without_fault` 실패(`_ending` 가드보다 늦게 오는 경우는 `_ending` 이 막으므로, 이 대조는 `Volatile.Read(ref _ending)` 가드와 **함께** 지워야 실패한다 — 두 줄을 같이 지워 확인).
  2. `Start` 의 `if (_faultedSeat >= 0) session.Stop();` 제거 → `Leave_before_start_faults_the_room_on_start` 실패(시간 초과).
  3. `Receive` 의 `a.Seat = seat;` 제거 → `Received_action_takes_the_seat_of_its_link` 실패.
  원복 후 전부 PASS.

- [ ] **Step 6: 커밋**

```bash
git add core/src/Tichu.Server/Matchmaking/Room.cs core/tests/Tichu.Server.Tests/RoomTests.cs
git commit -m "feat(server): 방 — 매치 하나의 수명, 이탈 폴트는 그 방에만"
```

---

### Task 5: `Connection` + 서버 조립 교체(구 전송 삭제)

**Files:**
- Create: `core/src/Tichu.Server/Connection.cs`
- Modify: `core/src/Tichu.Server/ServerApp.cs`, `Program.cs`, `ServerOptions.cs`(`Seats`·`--seats`·`ParseSeats` 삭제, 클래스 주석)
- Delete: `core/src/Tichu.Server/WebSocketServerTransport.cs`, `MatchHost.cs`, `SeatTable.cs`; `core/tests/Tichu.Server.Tests/ServerTransportTests.cs`, `SeatTableTests.cs`, `EndToEndTests.cs`(Task 6 에서 새로 씀)
- Create: `core/tests/Tichu.Server.Tests/ConnectionTests.cs`
- Modify: `core/tests/Tichu.Server.Tests/ServerOptionsTests.cs`(Seats 단언·`--seats` 케이스 정리), `deploy/server/server.env.default`, `tools/smoke-server.ps1`

**Interfaces:**
- Consumes: `Matchmaker`(Task 3), `Room`(Task 4), `SeatPicker`(Task 2), `TokenBucket`(기존).
- Produces: `Connection(WebSocket socket, TokenBucket? bucket, Action<string> log)` : `IPlayerLink` — `Task RunAsync(CancellationToken ct)`(수신 루프, 끝나면 `Owner?.Left(this)`), `static Task RejectAsync(WebSocket ws, string reason)`, `const int MaxMessageBytes = 16 * 1024`. 로그: `연결 #{id} 접속` / `연결 #{id} 끊김` / `연결 #{id} 연결 종료: {사유}` / `연결 #{id} 잘못된 메시지: …`.

- [ ] **Step 1: 실패하는 소켓 테스트** — `ConnectionTests.cs`(기존 `ServerTransportTests` 의 헬퍼 `Connect`·`ConnectRaw`·`SendText`·`Receive`·`ReadUntilClosed` 를 그대로 옮긴다 — 아래에 전부 포함):

```csharp
using System;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Tichu.GameFlow.Agents;
using Tichu.Net;

namespace Tichu.Server.Tests
{
    /// <summary>서버 연결 규칙을 원시 ClientWebSocket 으로 검증한다. 충원 대기를 길게 둬 방이 열리지 않게 한다.</summary>
    public class ConnectionTests
    {
        private static ServerOptions QueueOnly() => new ServerOptions
        {
            Difficulty = Difficulty.Easy, AiDelayMs = 0, InterRoundPauseMs = 0, Seed = 1, FillAfterSeconds = 600
        };

        private static Task<ClientWebSocket> Connect(Uri uri) => ConnectRaw(ProtocolVersion.Attach(uri));

        private static async Task<ClientWebSocket> ConnectRaw(Uri uri)
        {
            var ws = new ClientWebSocket();
            await ws.ConnectAsync(uri, CancellationToken.None);
            return ws;
        }

        private static async Task SendText(ClientWebSocket ws, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private static async Task<ServerEvent?> Receive(ClientWebSocket ws)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var buf = new byte[64 * 1024];
            int total = 0;
            WebSocketReceiveResult r;
            do
            {
                r = await ws.ReceiveAsync(new ArraySegment<byte>(buf, total, buf.Length - total), cts.Token);
                if (r.MessageType == WebSocketMessageType.Close) return null;
                total += r.Count;
            } while (!r.EndOfMessage);
            return ProtocolJson.DeserializeServerEvent(Encoding.UTF8.GetString(buf, 0, total));
        }

        private static async Task<WebSocketCloseStatus?> ReadUntilClosed(ClientWebSocket ws)
        {
            while (await Receive(ws) != null) { }
            return ws.CloseStatus;
        }

        [Test]
        public async Task First_message_is_the_queue_status()
        {
            await using var server = await TestServer.StartAsync(QueueOnly());
            using var a = await Connect(server.WsUri);

            var ev = await Receive(a);

            Assert.That(ev!.Kind, Is.EqualTo(ServerEventKind.Queued));
            Assert.That(ev.Waiting, Is.EqualTo(1));
            Assert.That(ev.Busy, Is.False);
            Assert.That(ev.FillInSeconds, Is.InRange(599, 600));
        }

        [Test]
        public async Task Oversized_message_closes_the_connection()
        {
            await using var server = await TestServer.StartAsync(QueueOnly());
            using var a = await Connect(server.WsUri);
            await Receive(a);
            await SendText(a, new string('x', Connection.MaxMessageBytes + 1));
            Assert.That(await ReadUntilClosed(a), Is.EqualTo(WebSocketCloseStatus.MessageTooBig));
        }

        [Test]
        public async Task Malformed_message_closes_the_connection()
        {
            await using var server = await TestServer.StartAsync(QueueOnly());
            using var a = await Connect(server.WsUri);
            await Receive(a);
            await SendText(a, "not json");
            Assert.That(await ReadUntilClosed(a), Is.EqualTo(WebSocketCloseStatus.InvalidPayloadData));
            await server.WaitForLogAsync("잘못된 메시지");
        }

        [Test]
        public async Task Exceeding_the_rate_limit_closes_the_connection()
        {
            // 대기 중엔 어떤 게임 액션이든 첫 메시지에서 닫히므로(대기 중 액션 규칙), 버킷을 0 으로 두어
            // 첫 메시지가 해석 전에 레이트리밋에 걸리는지 본다 — 검사 순서(버킷 → 해석)도 함께 고정된다.
            var opt = QueueOnly();
            opt.RateCapacity = 0;
            opt.RatePerSecond = 0.001;
            await using var server = await TestServer.StartAsync(opt);
            using var a = await Connect(server.WsUri);
            await Receive(a);

            await SendText(a, ProtocolJson.Serialize(ClientAction.Pass(0, 1)));

            Assert.That(await ReadUntilClosed(a), Is.EqualTo(WebSocketCloseStatus.PolicyViolation));
            await server.WaitForLogAsync("연결 종료: rate limit");
        }

        [Test]
        public async Task Game_action_while_queued_closes_the_connection()
        {
            await using var server = await TestServer.StartAsync(QueueOnly());
            using var a = await Connect(server.WsUri);
            await Receive(a);
            await SendText(a, ProtocolJson.Serialize(ClientAction.Pass(0, 1)));
            await ReadUntilClosed(a);
            await server.WaitForLogAsync("대기 중 게임 액션");
        }

        [Test]
        public async Task Leaving_the_queue_updates_the_others()
        {
            await using var server = await TestServer.StartAsync(QueueOnly());
            using var a = await Connect(server.WsUri);
            Assert.That((await Receive(a))!.Waiting, Is.EqualTo(1));
            var b = await Connect(server.WsUri);
            Assert.That((await Receive(a))!.Waiting, Is.EqualTo(2));

            b.Abort(); b.Dispose();

            Assert.That((await Receive(a))!.Waiting, Is.EqualTo(1));
        }

        [TestCase("")]
        [TestCase("?v=1")]
        [TestCase("?v=999")]
        [TestCase("?v=abc")]
        public async Task Missing_wrong_or_garbage_version_gets_the_update_notice_and_is_closed(string query)
        {
            await using var server = await TestServer.StartAsync(QueueOnly());
            using var old = await ConnectRaw(new Uri(server.WsUri + query));

            var ev = await Receive(old);

            Assert.That(ev!.Kind, Is.EqualTo(ServerEventKind.SessionFaulted));
            Assert.That(ev.Reason, Is.EqualTo(ProtocolVersion.UpdateRequiredReason));
            Assert.That(await ReadUntilClosed(old), Is.EqualTo(WebSocketCloseStatus.PolicyViolation));
            await server.WaitForLogAsync("프로토콜 버전 불일치");
        }

        [Test]
        public async Task Rejected_connection_does_not_join_the_queue()
        {
            await using var server = await TestServer.StartAsync(QueueOnly());
            using (var old = await ConnectRaw(server.WsUri))
            {
                await Receive(old);
                await ReadUntilClosed(old);
            }

            using var a = await Connect(server.WsUri);
            var ev = await Receive(a);

            Assert.That(ev!.Kind, Is.EqualTo(ServerEventKind.Queued));
            Assert.That(ev.Waiting, Is.EqualTo(1), "거절된 구 앱이 대기 인원에 세어지면 안 된다");
        }

        [Test]
        public async Task Client_transport_attaches_the_current_version()
        {
            await using var server = await TestServer.StartAsync(QueueOnly());
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
            Assert.That(first!.Kind, Is.EqualTo(ServerEventKind.Queued), "버전이 안 붙으면 SessionFaulted 가 온다");
        }
    }
}
```

`ServerOptionsTests.cs`: `Defaults_match_the_spec` 에서 `o.Seats` 단언 줄 삭제, `All_flags_parse` 에서 `"--seats", "0,1",` 인자와 `o.Seats` 단언 삭제, `Bad_arguments_throw` 의 `--seats` 케이스 4개를 다음 하나로 교체:

```csharp
        [TestCase("--seats", "0,2")]   // M3 에서 삭제된 인자 — 알 수 없는 인자로 거절
```

- [ ] **Step 2: 실패 확인** — `dotnet test core/tests/Tichu.Server.Tests/Tichu.Server.Tests.csproj --filter "FullyQualifiedName~ConnectionTests|FullyQualifiedName~ServerOptionsTests" --nologo` → 컴파일 실패(`Connection` 없음).

- [ ] **Step 3: `Connection.cs` 구현** — 기존 `WebSocketServerTransport` 의 내부 `Connection`·`ReceiveLoop`·`Abort`·`Dispatch`·`RejectAsync` 를 옮긴다:

```csharp
using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Tichu.Net;

namespace Tichu.Server
{
    /// <summary>
    /// 소켓 하나(M2 스펙 §5·§6, M3 스펙 §2.1). 송신은 큐에 넣기만 하고 연결마다 송신 태스크 하나가
    /// 순서대로 보낸다(WebSocket 은 한 소켓에 동시 전송 금지). 수신은 16KB 초과·텍스트 아님·레이트리밋
    /// 초과·깨진 JSON 이면 닫는다. 받은 액션은 지금 소속(Owner: 대기열 또는 방)에 넘긴다.
    /// </summary>
    public sealed class Connection : IPlayerLink
    {
        public const int MaxMessageBytes = 16 * 1024;
        private static int _nextId;

        private readonly Channel<string> _out =
            Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        private readonly TokenBucket? _bucket;
        private readonly Action<string> _log;
        private volatile ILinkOwner? _owner;
        private int _completed;
        private WebSocketCloseStatus _closeStatus = WebSocketCloseStatus.NormalClosure;
        private string _closeReason = "bye";

        public Connection(WebSocket socket, TokenBucket? bucket, Action<string> log)
        {
            Socket = socket;
            _bucket = bucket;
            _log = log;
            WriterTask = Task.Run(WriteLoop);
        }

        public int Id { get; } = Interlocked.Increment(ref _nextId);
        public WebSocket Socket { get; }
        public Task WriterTask { get; }

        public ILinkOwner? Owner
        {
            get => _owner;
            set => _owner = value;
        }

        public void Send(ServerEvent ev) => _out.Writer.TryWrite(ProtocolJson.Serialize(ev));

        public void Close(string reason) => Complete(WebSocketCloseStatus.NormalClosure, reason);

        /// <summary>더 보낼 것이 없다. 남은 큐를 비운 뒤 status 로 닫는다. 처음 호출만 유효.</summary>
        public void Complete(WebSocketCloseStatus status, string reason)
        {
            if (Interlocked.Exchange(ref _completed, 1) == 1) return;
            _closeStatus = status;
            _closeReason = reason;
            _out.Writer.TryComplete();
        }

        /// <summary>
        /// 좌석·대기열 없이 사유 하나(SessionFaulted)만 보낸 뒤 닫는다 — 프로토콜 버전 불일치(M2b §5.3).
        /// 바로 끊으면 클라이언트가 아직 읽지 않은 사유가 버려질 수 있으므로 상대의 닫기 응답을 최대 2초 기다린다.
        /// </summary>
        public static async Task RejectAsync(WebSocket ws, string reason)
        {
            var conn = new Connection(ws, null, _ => { });
            conn.Send(ServerEvent.SessionFaulted(reason));
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

        /// <summary>연결이 끝날 때까지 수신한다. 끝나면 소유자에게 Left 를 알리고 송신을 마친다.</summary>
        public async Task RunAsync(CancellationToken ct)
        {
            try
            {
                await ReceiveLoop(ct);
            }
            catch (WebSocketException) { /* 갑작스러운 끊김 */ }
            catch (OperationCanceledException) { /* 서버 종료 */ }
            finally
            {
                Complete(WebSocketCloseStatus.NormalClosure, "bye");
                _log($"연결 #{Id} 끊김");
                Owner?.Left(this);
            }
            await WriterTask;
        }

        /// <summary>메시지 하나를 해석해 소유자에게 넘긴다. 해석 실패면 false(호출자가 닫는다).</summary>
        internal bool Dispatch(string json)
        {
            ClientAction a;
            try { a = ProtocolJson.DeserializeClientAction(json); }
            catch (ProtocolException e)
            {
                _log($"연결 #{Id} 잘못된 메시지: {e.Message}");
                return false;
            }
            Owner?.Receive(this, a);   // 방이 Seat 을 연결 좌석으로 덮어쓴다(사칭 방지)
            return true;
        }

        private async Task ReceiveLoop(CancellationToken ct)
        {
            var buf = new byte[4096];
            var message = new MemoryStream();

            while (true)
            {
                message.SetLength(0);
                WebSocketReceiveResult r;
                do
                {
                    r = await Socket.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                    if (r.MessageType == WebSocketMessageType.Close) return;
                    if (r.MessageType != WebSocketMessageType.Text)
                    {
                        Abort(WebSocketCloseStatus.InvalidMessageType, "text only");
                        return;
                    }
                    message.Write(buf, 0, r.Count);
                    if (message.Length > MaxMessageBytes)
                    {
                        Abort(WebSocketCloseStatus.MessageTooBig, "message too big");
                        return;
                    }
                } while (!r.EndOfMessage);

                if (_bucket != null && !_bucket.TryTake())
                {
                    Abort(WebSocketCloseStatus.PolicyViolation, "rate limit");
                    return;
                }

                string json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                if (!Dispatch(json))
                {
                    Abort(WebSocketCloseStatus.InvalidPayloadData, "bad message");
                    return;
                }
            }
        }

        private void Abort(WebSocketCloseStatus status, string reason)
        {
            _log($"연결 #{Id} 연결 종료: {reason}");
            Complete(status, reason);
        }

        private async Task WriteLoop()
        {
            try
            {
                await foreach (var json in _out.Reader.ReadAllAsync())
                {
                    var bytes = Encoding.UTF8.GetBytes(json);
                    await Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
                if (Socket.State == WebSocketState.Open || Socket.State == WebSocketState.CloseReceived)
                    await Socket.CloseOutputAsync(_closeStatus, _closeReason, CancellationToken.None);
            }
            catch (WebSocketException) { }
            catch (ObjectDisposedException) { }
        }
    }
}
```

- [ ] **Step 4: `ServerApp.cs` 교체** — `Build` 본문의 전송·`MatchHost` 생성과 `/ws` 핸들러를 다음으로:

```csharp
            static double NowSeconds() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            // --seed 가 있으면 좌석 배정도 결정적이다(테스트·재현). SeatPicker 는 매치메이커 잠금 안에서만 불린다.
            var rng = opt.Seed is ulong seed ? new Random(unchecked((int)seed)) : new Random();
            Matchmaker? matchmaker = null;
            matchmaker = new Matchmaker(
                opt.FillAfterSeconds, opt.MaxRooms, () => DateTime.UtcNow,
                (index, humans) => new Room(index, humans, SeatPicker.Pick(humans.Count, rng), opt, log,
                    () => matchmaker!.RoomClosed()),
                log);
            var tick = new Timer(_ => matchmaker.Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            app.Lifetime.ApplicationStopping.Register(() => tick.Dispose());

            app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15), KeepAliveTimeout = TimeSpan.FromSeconds(30) });
            app.Map("/ws", (RequestDelegate)(async ctx =>
            {
                if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
                string? version = ctx.Request.Query[ProtocolVersion.QueryKey];
                using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
                if (!ProtocolVersion.Accepts(version))
                {
                    log($"프로토콜 버전 불일치 — 거절 (받은 값: {(string.IsNullOrEmpty(version) ? "없음" : version)}, 서버: {ProtocolVersion.Current})");
                    await Connection.RejectAsync(ws, ProtocolVersion.UpdateRequiredReason);
                    return;
                }
                var conn = new Connection(ws, new TokenBucket(opt.RateCapacity, opt.RatePerSecond, NowSeconds), log);
                log($"연결 #{conn.Id} 접속");
                matchmaker.Enqueue(conn);
                await conn.RunAsync(ctx.RequestAborted);
            }));
```

`using System.Threading;` 추가. 클래스 주석의 의미는 그대로.

`Program.cs`:

```csharp
    Console.Error.WriteLine("사용법: --difficulty Hard --target 1000 --port 7777 [--host 0.0.0.0] [--fill-after 30] [--max-rooms 2] [--ai-delay 900] [--seed N]");
```

```csharp
Console.WriteLine($"Tichu 서버 — 랜덤 매칭 · {opt.Difficulty} · 목표 {opt.Target} · 충원 {opt.FillAfterSeconds}초 · 방 상한 {opt.MaxRooms}");
Console.WriteLine($"접속 주소: ws://<이 PC 의 IP>:{opt.Port}/ws  (같은 Wi-Fi)");
Console.WriteLine("Ctrl+C 로 종료");
```

`ServerOptions.cs`: `Seats` 속성, `case "--seats"`, `ParseSeats` 삭제. 클래스 주석을 "서버 실행 설정. 프로세스 하나가 랜덤 매칭으로 여러 방을 돌린다(M3 스펙 §2.3). CLI: --difficulty --target --host --port --fill-after --max-rooms --ai-delay --seed." 로.

- [ ] **Step 5: 구 코드·테스트 삭제**

```bash
git rm core/src/Tichu.Server/WebSocketServerTransport.cs core/src/Tichu.Server/MatchHost.cs core/src/Tichu.Server/SeatTable.cs
git rm core/tests/Tichu.Server.Tests/ServerTransportTests.cs core/tests/Tichu.Server.Tests/SeatTableTests.cs core/tests/Tichu.Server.Tests/EndToEndTests.cs
```

(EndToEndTests 의 내용은 Task 6 이 매칭 기준으로 새로 쓴다. 지워지는 회귀 테스트 중 Dispatch 좌석 덮어쓰기·거절/폴트 로그는 Task 4 `RoomTests` 가, 크기·형식·레이트·버전은 이 태스크 `ConnectionTests` 가 대신한다. 세대 번호 테스트는 개념이 사라졌다.)

- [ ] **Step 6: 배포 자산** — `deploy/server/server.env.default` 2행:

```
TICHU_GAME_ARGS=--difficulty Hard --target 1000 --fill-after 30 --max-rooms 2
```

`tools/smoke-server.ps1`: 설명의 "Lobby 를 기대한다" → "Queued(대기열 상태)를 기대한다", "매치가 진행 중이면 서버가 "room full" 로 닫는다 … 반납된다" 두 줄 → "대기열에 잠깐 들어갔다 바로 나온다(충원 대기보다 짧아 방이 생기지 않는다).", 그리고 39행의 `$msg.Kind -eq 'Lobby'` → `$msg.Kind -eq 'Queued'`.

- [ ] **Step 7: 통과 확인** — `dotnet build core/src/Tichu.Server/Tichu.Server.csproj --nologo` 경고·오류 0 확인 후 `dotnet test core/tests/Tichu.Server.Tests/Tichu.Server.Tests.csproj --nologo` → 전부 PASS.

- [ ] **Step 8: 음성 대조** — `Connection.RunAsync` 의 `Owner?.Left(this);` 제거 → `Leaving_the_queue_updates_the_others` 실패(시간 초과) 확인 후 원복.

- [ ] **Step 9: 로컬 스모크**

```bash
dotnet run --project core/src/Tichu.Server -- --port 7799 --fill-after 600
# 다른 셸
pwsh tools/smoke-server.ps1 -Url ws://127.0.0.1:7799/ws          # OK 기대(Queued)
pwsh tools/smoke-server.ps1 -Url ws://127.0.0.1:7799/ws -NoVersion -ExpectReject   # OK 기대
```
서버는 Ctrl+C 로 종료.

- [ ] **Step 10: 커밋**

```bash
git add -A core/src/Tichu.Server core/tests/Tichu.Server.Tests deploy/server/server.env.default tools/smoke-server.ps1
git commit -m "feat(server): 연결 계층 분리 + 매치메이커·방으로 서버 조립 교체(--seats 삭제)"
```

---

### Task 6: 매칭 E2E(실제 WebSocket)

**Files:**
- Create: `core/tests/Tichu.Server.Tests/EndToEndTests.cs`

**Interfaces:**
- Consumes: `TestServer`(기존: `StartAsync`, `WsUri`, `Log`, `LogMark`, `WaitForLogAsync`), `WebSocketClientTransport.ConnectAsync`, `ScriptedClient(int seat, IClientTransport)`.
- 로그 문구: Task 4 의 `방 {i} 시작 — 사람 {h} · AI {a} · …`, `방 {i} 종료`, `방 {i} 좌석 {s} 이탈 — 매치 중단`.

- [ ] **Step 1: 테스트 작성**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Tichu.Core.Tests;
using Tichu.GameFlow.Agents;
using Tichu.Net;

namespace Tichu.Server.Tests
{
    /// <summary>
    /// 실제 Kestrel + 실제 WebSocketClientTransport + 스크립트 클라이언트 + 서버 AI. 빠르게 돌도록
    /// AI 는 Easy·지연 0, 라운드 사이 멈춤 0, 충원 1초(틱이 1초라 방은 1~2초 뒤 열린다).
    /// </summary>
    public class EndToEndTests
    {
        private static ServerOptions Fast(int target = 1, int maxRooms = 2) => new ServerOptions
        {
            Difficulty = Difficulty.Easy, Target = target, AiDelayMs = 0, InterRoundPauseMs = 0, Seed = 42,
            FillAfterSeconds = 1, MaxRooms = maxRooms,
            RateCapacity = 100000, RatePerSecond = 100000   // 지연 0 스크립트 클라이언트는 사람보다 훨씬 빠르다
        };

        private static Task<WebSocketClientTransport> Connect(TestServer s) =>
            WebSocketClientTransport.ConnectAsync(s.WsUri, TimeSpan.FromSeconds(5), CancellationToken.None);

        /// <summary>Lobby 까지 받은 이벤트를 쥐고 있다가 ScriptedClient 에 다시 흘려 주는 전송.</summary>
        private sealed class Joined : IClientTransport
        {
            private readonly WebSocketClientTransport _inner;
            private List<ServerEvent>? _backlog;
            public int Seat { get; }
            public List<ServerEvent> BeforeLobby { get; }

            public Joined(WebSocketClientTransport inner, int seat, List<ServerEvent> beforeLobby, List<ServerEvent> backlog)
            {
                _inner = inner; Seat = seat; BeforeLobby = beforeLobby; _backlog = backlog;
            }

            public IReadOnlyList<ServerEvent> Drain()
            {
                var got = _inner.Drain();
                if (_backlog == null) return got;
                var all = _backlog; _backlog = null;
                all.AddRange(got);
                return all;
            }

            public void Submit(ClientAction a) => _inner.Submit(a);
        }

        /// <summary>Lobby 를 받을 때까지 기다려 좌석을 알아낸다. Lobby 이후 이벤트는 잃지 않는다.
        /// already = 호출자가 이미 Drain 해 버린 이벤트(먼저 처리한다).</summary>
        private static async Task<Joined> AwaitLobby(WebSocketClientTransport t, double seconds = 10, List<ServerEvent>? already = null)
        {
            var before = new List<ServerEvent>();
            var pending = already ?? new List<ServerEvent>();
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
            while (DateTime.UtcNow < deadline)
            {
                var got = new List<ServerEvent>(pending);
                pending.Clear();
                got.AddRange(t.Drain());
                for (int i = 0; i < got.Count; i++)
                {
                    if (got[i].Kind == ServerEventKind.Lobby)
                        return new Joined(t, got[i].YourSeat, before, got.Skip(i + 1).ToList());
                    if (got[i].Kind == ServerEventKind.SessionFaulted) Assert.Fail($"대기 중 폴트: {got[i].Reason}");
                    before.Add(got[i]);
                }
                await Task.Delay(5);
            }
            Assert.Fail($"{seconds}초 안에 Lobby 가 오지 않았다");
            return null!;
        }

        private static async Task PlayToEnd(params ScriptedClient[] clients)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);
            while (DateTime.UtcNow < deadline && !clients.All(c => c.MatchEnded))
            {
                foreach (var c in clients)
                {
                    c.Pump();
                    if (c.FaultReason != null) Assert.Fail($"좌석 {c.Seat} 폴트: {c.FaultReason}");
                }
                await Task.Delay(2);
            }
            foreach (var c in clients) Assert.That(c.MatchEnded, Is.True, $"좌석 {c.Seat} 이 MatchEnded 를 받아야 한다");
        }

        private static int Count(TestServer s, string fragment)
        {
            lock (s.Log) return s.Log.Count(m => m.Contains(fragment));
        }

        [Test]
        public async Task Two_clients_share_a_room_with_two_ai_and_finish()
        {
            await using var server = await TestServer.StartAsync(Fast());
            using var t1 = await Connect(server);
            using var t2 = await Connect(server);
            var j1 = await AwaitLobby(t1);
            var j2 = await AwaitLobby(t2);

            Assert.That(j1.Seat, Is.Not.EqualTo(j2.Seat));
            Assert.That(j1.BeforeLobby.Any(e => e.Kind == ServerEventKind.Queued), Is.True, "대기 중엔 Queued 를 받는다");
            await PlayToEnd(new ScriptedClient(j1.Seat, j1), new ScriptedClient(j2.Seat, j2));
            await server.WaitForLogAsync("방 0 시작 — 사람 2 · AI 2");
        }

        [Test]
        public async Task Solo_client_plays_three_ai_after_the_fill_wait()
        {
            await using var server = await TestServer.StartAsync(Fast());
            using var t = await Connect(server);
            var j = await AwaitLobby(t);

            await PlayToEnd(new ScriptedClient(j.Seat, j));
            await server.WaitForLogAsync("방 0 시작 — 사람 1 · AI 3");
        }

        [Test]
        public async Task Five_clients_split_into_two_rooms()
        {
            await using var server = await TestServer.StartAsync(Fast());
            var ts = new List<WebSocketClientTransport>();
            for (int i = 0; i < 5; i++) ts.Add(await Connect(server));
            try
            {
                var joined = new List<Joined>();
                foreach (var t in ts) joined.Add(await AwaitLobby(t));

                await PlayToEnd(joined.Select(j => new ScriptedClient(j.Seat, j)).ToArray());
                await server.WaitForLogAsync("사람 4 · AI 0");
                await server.WaitForLogAsync("사람 1 · AI 3");
                Assert.That(Count(server, "시작 — 사람"), Is.EqualTo(2));
            }
            finally { foreach (var t in ts) t.Dispose(); }
        }

        [Test]
        public async Task Busy_queue_waits_for_a_free_room()
        {
            var opt = Fast(maxRooms: 1);
            opt.AiDelayMs = 20;                               // 첫 방이 B 접속 전에 끝나지 않게
            await using var server = await TestServer.StartAsync(opt);
            using var ta = await Connect(server);
            var ja = await AwaitLobby(ta);
            await server.WaitForLogAsync("방 0 시작");

            using var tb = await Connect(server);
            ServerEvent? busy = null;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            var early = new List<ServerEvent>();
            while (busy == null && DateTime.UtcNow < deadline)
            {
                foreach (var e in tb.Drain()) { early.Add(e); if (e.Kind == ServerEventKind.Queued && e.Busy) busy = e; }
                await Task.Delay(5);
            }
            Assert.That(busy, Is.Not.Null, "상한에선 Busy 를 받아야 한다");
            Assert.That(busy!.Position, Is.EqualTo(1));
            Assert.That(busy.FillInSeconds, Is.Null);

            var a = new ScriptedClient(ja.Seat, ja);
            await PlayToEnd(a);                               // 방 0 이 끝나면 B 가 들어간다
            var jb = await AwaitLobby(tb, 30, early);         // Busy 확인 중 먹은 이벤트부터 다시 본다
            await PlayToEnd(new ScriptedClient(jb.Seat, jb));
            await server.WaitForLogAsync("방 1 시작 — 사람 1 · AI 3");
        }

        [Test]
        public async Task Leaving_mid_match_faults_only_that_room()
        {
            var opt = Fast(target: 1000);
            opt.AiDelayMs = 50;                               // 끊기 전에 매치가 끝나지 않게
            await using var server = await TestServer.StartAsync(opt);
            using var t1 = await Connect(server);
            var t2 = await Connect(server);
            var j1 = await AwaitLobby(t1);
            await AwaitLobby(t2);
            await server.WaitForLogAsync("방 0 시작");

            using var t3 = await Connect(server);             // 방 1(혼자)
            var j3 = await AwaitLobby(t3);
            await server.WaitForLogAsync("방 1 시작");

            var c1 = new ScriptedClient(j1.Seat, j1);
            var c3 = new ScriptedClient(j3.Seat, j3);
            t2.Dispose();                                     // 방 0 의 한 사람이 사라진다

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTime.UtcNow < deadline && c1.FaultReason == null) { c1.Pump(); c3.Pump(); await Task.Delay(5); }
            Assert.That(c1.FaultReason, Does.Contain("연결 끊김"), "남은 사람에게 누가 끊겼는지 알려야 한다");

            var watch = DateTime.UtcNow + TimeSpan.FromSeconds(1);
            while (DateTime.UtcNow < watch) { c3.Pump(); await Task.Delay(5); }
            Assert.That(c3.FaultReason, Is.Null, "다른 방은 영향이 없다");
            Assert.That(Count(server, "방 1 종료"), Is.EqualTo(0));
        }

        [Test]
        public async Task No_fault_is_raised_when_server_closes_connections_after_match_end()
        {
            await using var server = await TestServer.StartAsync(Fast());
            using var t = await Connect(server);
            var j = await AwaitLobby(t);
            var c = new ScriptedClient(j.Seat, j);
            await PlayToEnd(c);
            await server.WaitForLogAsync("방 0 종료");
            await Task.Delay(500);                            // 서버 닫기 프레임이 도착할 시간
            c.Pump();

            Assert.That(c.FaultReason, Is.Null, "정상 종료 뒤 닫기를 끊김으로 보고하면 결과 화면 대신 메뉴로 튕긴다");
        }

        [Test]
        public void Connecting_to_a_dead_port_fails_within_the_timeout()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Assert.ThrowsAsync(Is.InstanceOf<Exception>(), async () =>
                await WebSocketClientTransport.ConnectAsync(new Uri("ws://127.0.0.1:1/ws"), TimeSpan.FromSeconds(2), CancellationToken.None));
            Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
        }
    }
}
```


- [ ] **Step 2: 실행** — `dotnet test core/tests/Tichu.Server.Tests/Tichu.Server.Tests.csproj --filter "FullyQualifiedName~EndToEndTests" --nologo` → 7개 PASS. 서버 테스트 전체도 PASS. 3회 반복 실행해 플래키 없음 확인(`for i in 1 2 3; do dotnet test ... --filter ...EndToEndTests --nologo | tail -3; done`).

- [ ] **Step 3: 음성 대조** — `Matchmaker` 생성 시 `opt.MaxRooms` 대신 `int.MaxValue` 를 넘기도록 `ServerApp` 을 잠시 바꿈 → `Busy_queue_waits_for_a_free_room` 실패 확인 → 원복.

- [ ] **Step 4: 커밋**

```bash
git add core/tests/Tichu.Server.Tests/EndToEndTests.cs
git commit -m "test(server): 매칭 E2E — 둘이 한 방·혼자 AI 충원·다섯이 두 방·상한 대기·방별 폴트"
```

---

### Task 7: 클라이언트 — 대기 화면 문구와 메뉴 이름

**Files:**
- Modify: `Assets/_Project/Presentation/Net/LobbyWaiter.cs`
- Modify: `Assets/_Project/Presentation/Shell/MenuShellPresenter.cs:90,118-127`
- Modify: `Assets/_Project/Presentation/Shell/Views/MenuShellView.cs:192`
- Test: `Assets/_Project/Presentation/Tests/LobbyWaiterTests.cs`

**Interfaces:**
- Consumes: `ServerEvent.Queued`·필드(Task 1).
- Produces: `LobbyWaiter(Func<double> nowSeconds)` + 기존 무인자 생성자(Stopwatch 시계). 속성 `Waiting`, `Busy`, `Position`. `StatusText` 문구:
  - Busy: `서버가 붐빕니다 · 대기 순번 {Position}`
  - 대기: `상대를 찾는 중 · {Waiting}명 대기 · {남은초}초 뒤 AI와 시작` (남은초 = ceil(받은 FillInSeconds − 경과), 0 미만이면 0)
  - Lobby 수신 후: 기존 `다른 플레이어 기다리는 중 {Connected}/{Required}`
  - 아무것도 없을 때: 기존 `서버 접속 중…`
- `RoundBootstrap` 은 이미 매 프레임 `status.text = lobby.StatusText` 를 하므로 바꾸지 않는다.

- [ ] **Step 1: 실패하는 테스트** — `LobbyWaiterTests.cs` 에 추가:

```csharp
        [Test]
        public void Queued_shows_a_countdown_from_when_it_arrived()
        {
            double now = 100;
            var w = new LobbyWaiter(() => now);
            w.Feed(new[] { ServerEvent.Queued(2, 18, false, 0) });

            Assert.That(w.StatusText, Is.EqualTo("상대를 찾는 중 · 2명 대기 · 18초 뒤 AI와 시작"));
            now = 105.2;
            Assert.That(w.StatusText, Is.EqualTo("상대를 찾는 중 · 2명 대기 · 13초 뒤 AI와 시작"));
            now = 200;
            Assert.That(w.StatusText, Is.EqualTo("상대를 찾는 중 · 2명 대기 · 0초 뒤 AI와 시작"));
        }

        [Test]
        public void Busy_shows_the_position()
        {
            var w = new LobbyWaiter(() => 0);
            w.Feed(new[] { ServerEvent.Queued(5, null, true, 3) });

            Assert.That(w.Busy, Is.True);
            Assert.That(w.StatusText, Is.EqualTo("서버가 붐빕니다 · 대기 순번 3"));
        }

        [Test]
        public void Queued_does_not_start_the_match()
        {
            var w = new LobbyWaiter(() => 0);
            w.Feed(new[] { ServerEvent.Queued(1, 30, false, 0) });

            Assert.That(w.Started, Is.False);
            Assert.That(w.Backlog, Is.Empty);
        }

        [Test]
        public void Queued_then_lobby_then_state_starts_with_the_seat()
        {
            var w = new LobbyWaiter(() => 0);
            var first = ServerEvent.StateUpdate(new PlayerView { Viewer = 3 }, null);
            w.Feed(new List<ServerEvent> { ServerEvent.Queued(1, 30, false, 0), ServerEvent.Lobby(3, 2, 2), first });

            Assert.That(w.Started, Is.True);
            Assert.That(w.Seat, Is.EqualTo(3));
            Assert.That(w.Backlog, Is.EqualTo(new[] { first }));
        }
```

- [ ] **Step 2: 실패 확인** — Unity 컴파일(에러 `LobbyWaiter(Func<double>)` 없음) 또는 `python unity_tests.py names Tichu.Presentation.Tests.LobbyWaiterTests` → 1(실패/컴파일 에러).

- [ ] **Step 3: 구현** — `LobbyWaiter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Tichu.Net;

namespace Tichu.Presentation.Net
{
    /// <summary>
    /// 네트워크 매치의 대기실. 매치가 시작되기 전 이벤트(Queued·Lobby·폴트)를 읽어 대기 상태와 내 좌석을
    /// 알아내고, 첫 매치 이벤트부터는 순서대로 Backlog 에 담아 TichuClient 에 넘긴다 —
    /// 마지막 Lobby 와 첫 StateUpdate 가 한 Drain 묶음에 섞여 와도 잃지 않기 위해서다.
    /// Queued 의 남은 초는 받은 시각부터 이 쪽에서 센다(서버는 대기열이 바뀔 때만 보낸다).
    /// 유니티 타입 무참조(EditMode 단위 테스트).
    /// </summary>
    public sealed class LobbyWaiter
    {
        private readonly Func<double> _now;
        private double? _fillAt;

        public LobbyWaiter() : this(() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency) { }

        public LobbyWaiter(Func<double> nowSeconds) { _now = nowSeconds; }

        public int Seat { get; private set; } = -1;
        public int Connected { get; private set; }
        public int Required { get; private set; }
        public int Waiting { get; private set; }
        public bool Busy { get; private set; }
        public int Position { get; private set; }
        public bool Started { get; private set; }
        public string FaultReason { get; private set; }
        public List<ServerEvent> Backlog { get; } = new List<ServerEvent>();

        public string StatusText
        {
            get
            {
                if (Required > 0) return $"다른 플레이어 기다리는 중 {Connected}/{Required}";
                if (Busy) return $"서버가 붐빕니다 · 대기 순번 {Position}";
                if (_fillAt.HasValue)
                {
                    int left = Math.Max(0, (int)Math.Ceiling(_fillAt.Value - _now()));
                    return $"상대를 찾는 중 · {Waiting}명 대기 · {left}초 뒤 AI와 시작";
                }
                return "서버 접속 중…";
            }
        }

        public void Feed(IReadOnlyList<ServerEvent> events)
        {
            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                if (Started) { Backlog.Add(ev); continue; }

                switch (ev.Kind)
                {
                    case ServerEventKind.Queued:
                        Waiting = ev.Waiting;
                        Busy = ev.Busy;
                        Position = ev.Position;
                        _fillAt = ev.FillInSeconds.HasValue ? _now() + ev.FillInSeconds.Value : (double?)null;
                        break;
                    case ServerEventKind.Lobby:
                        Seat = ev.YourSeat;
                        Connected = ev.Connected;
                        Required = ev.Required;
                        break;
                    case ServerEventKind.SessionFaulted:
                        FaultReason = ev.Reason;
                        break;
                    default:
                        Started = true;
                        Backlog.Add(ev);
                        break;
                }
            }
        }
    }
}
```

`MenuShellPresenter.cs`: 90행 `Wire(ScreenState.ModeSelect, "친구방",    OnFriendRoom);` → `Wire(ScreenState.ModeSelect, "온라인 대전", OnFriendRoom);`. 118행 주석 → `// 온라인 대전: 개발 빌드(에디터 포함)에서만 접속 화면을 연다. 릴리스는 스텁 안내(출시 주소 고정은 M4).`, 124행 토스트 → `ShowToast("온라인 대전은 준비 중입니다");`.

`MenuShellView.cs` 192행: `ScreenState.NetJoin    => "온라인 대전 (개발용)",`.

- [ ] **Step 4: 통과 확인** — Unity 컴파일 에러 0 → `python unity_tests.py assembly Tichu.Presentation.Tests` → 0(기존 196 + 4 = 200 전부 PASS).

- [ ] **Step 5: 커밋**

```bash
git add Assets/_Project/Presentation/Net/LobbyWaiter.cs Assets/_Project/Presentation/Tests/LobbyWaiterTests.cs Assets/_Project/Presentation/Shell/MenuShellPresenter.cs Assets/_Project/Presentation/Shell/Views/MenuShellView.cs
git commit -m "feat(presentation): 대기열 카운트다운·붐빔 순번 표시, 메뉴 '온라인 대전'"
```

---

### Task 8: 회귀·로컬 실기·완료 보고

**Files:**
- Create: `docs/reports/design/티츄_M3_매칭_결과.html`(결과 요약, 프로젝트 보고서 스타일)

- [ ] **Step 1: 전체 회귀**

```bash
dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --nologo
dotnet test core/tests/Tichu.Server.Tests/Tichu.Server.Tests.csproj --nologo
```
Expected: 둘 다 실패 0. Unity: `python unity_tests.py assembly Tichu.Presentation.Tests` → 0, `python unity_tests.py names Tichu.Core.Tests.ProtocolJsonTests Tichu.Core.Tests.ProtocolVersionTests Tichu.Core.Tests.SeatRotationTests Tichu.Core.Tests.LoopbackMatchTests Tichu.Core.Tests.RemoteAgentTests` → 0. 미러 diff(Task 1 Step 5 명령) 출력 없음.

- [ ] **Step 2: 로컬 실기(사람)** — PC 서버 `dotnet run --project core/src/Tichu.Server -- --fill-after 30`(유선 IP 192.168.45.213, 같은 Wi-Fi). 체크리스트:
  1. 에디터 + S23 이 30초 안에 접속 → 같은 방, AI 2명과 대전, 좌석이 서로 다름.
  2. 혼자 접속 → "상대를 찾는 중 · 1명 대기 · N초" 카운트다운 → 0 근처에서 AI 3명과 시작.
  3. 서버를 `--max-rooms 1 --fill-after 5` 로 재시작 → 한 기기 시작 후 다른 기기 접속 시 "서버가 붐빕니다 · 대기 순번 1" → 첫 방이 끝나면 자동 입장.
  4. 대기 중 [취소] → 메뉴 복귀, 서버 로그 "대기열 이탈".
  5. 매치 중 한 기기 앱 종료 → 다른 기기에 연결 끊김 안내 후 메뉴.
  6. (APK 빌드 후) 구 APK 가 업데이트 안내를 받는다.
- [ ] **Step 3: 새 개발 APK 빌드** — 에디터를 닫고 `DevBuild.BuildDevApk` 배치 빌드(약 15분) → `Build/TichuMaster-dev-arm64.apk`. ⚠️ S23 에 릴리스 서명 앱이 있으면 먼저 제거. 밀린 실기 확인(동시 결정·티츄 즉시·캔버스·차례 프롬프트 유지)도 이 APK 로 함께 본다.
- [ ] **Step 4: 결과 보고 HTML 작성·커밋** — 무엇을 바꿨는지(구조도), 테스트 수, 실기 체크 결과, 남은 일(클라우드 부하 실측으로 `--max-rooms` 확정, M5). `git add docs/reports/design/티츄_M3_매칭_결과.html && git commit -m "docs(mp): M3 결과 보고"`.
- [ ] **Step 5: 머지 승인 요청** — main 머지·푸시는 사용자 승인 후(브랜치 `feat/mp-m3` 는 `feat/mp-m2b` 위에 있으므로 M2b 미머지 커밋도 함께 들어간다는 점을 알린다).
