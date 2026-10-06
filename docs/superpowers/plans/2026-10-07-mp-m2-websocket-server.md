# 멀티플레이 M2 — WebSocket 전송 · 로컬 .NET 서버 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** PC에서 `dotnet run`으로 띄운 권위 서버에 Unity 클라이언트 둘(에디터 + S23)이 같은 Wi-Fi로 접속해, 빈자리를 서버 PIMC가 채운 매치를 끝까지 둔다.

**Architecture:** M1의 `ITichuTransport` 뒤에 `WebSocketServerTransport`(ASP.NET Core, net10)를 끼우고 `MatchSession`은 그대로 돌린다. 클라이언트는 새 `IClientTransport` 이음새 뒤에 `LoopbackClient`(싱글)와 `WebSocketClientTransport`(네트워크)를 둔다. 메시지는 Newtonsoft.Json이고, 클라이언트 경계의 `SeatRotation`이 "나 = 좌석 0" 가정을 지켜 줘서 뷰모델과 뷰는 고치지 않는다.

**Tech Stack:** C# (core netstandard2.1 / LangVersion 9) · ASP.NET Core 최소 API (net10.0) · Newtonsoft.Json 13 (core NuGet `13.0.3`, Unity `com.unity.nuget.newtonsoft-json` `3.2.1`) · `System.Net.WebSockets` · NUnit 4(dotnet) / NUnit 3.5(Unity) · Unity 6000.3.17f1 · R3 · UniTask

**Spec:** `docs/superpowers/specs/2026-10-07-mp-m2-websocket-server-design.md` (§10 = 이 계획을 쓰며 확정한 정제)

## Global Constraints

- **정본은 `core/`**, Unity 사본은 `tools/sync-core-to-unity.ps1`이 만든다. core 파일을 고쳤으면 반드시 `pwsh tools/sync-core-to-unity.ps1`을 돌리고 사본까지 커밋한다(`.meta` 포함). `-Check`로 무드리프트 확인.
- **`MatchSession` · `RemoteAgent` · `GameDriver` · PIMC 로직 · 프로토콜 의미론은 바꾸지 않는다.** 바꾸면 스펙 위반이다.
- `core/src/Tichu.Net`·`Tichu.GameFlow`는 netstandard2.1, `LangVersion 9.0`, `Nullable enable`, `ImplicitUsings disable`. Unity 컴파일이 되는 문법만 쓴다.
- `Tichu.Server`·`Tichu.Server.Tests`는 **net10.0**이고 Unity로 미러하지 않는다. `Tichu.Core.Tests`는 net9.0 그대로 둔다.
- **NUnit 버전 차이**: `core/tests/Tichu.Core.Tests`는 Unity(NUnit 3.5)로 미러된다. 거기엔 NUnit 4 전용 API(`Does.Not.Contain(object)` 같은 것)를 쓰지 않는다. `Assert.That` + `Is.*`/`Does.Contain(string)`만 쓴다. "dotnet 그린 ≠ 유니티 그린."
- Unity 쪽 미러 테스트(`Tichu.Core.Tests` asmdef)는 `overrideReferences: true`라 Newtonsoft를 직접 참조하지 못한다 → **미러되는 테스트는 Newtonsoft 타입을 쓰지 않는다**(`ProtocolJson` API와 `ProtocolException`만).
- 기본값(스펙 원문): 포트 `7777` · 인간 좌석 `0,2` · 난이도 `Hard` · 목표 `1000` · AI 지연 `900`ms · 라운드 사이 멈춤 `3.5`초 · 프레임 상한 `16KB` · 토큰 버킷 `초당 10건, 버스트 20건` · 접속 타임아웃 `5초`.
- 커밋 메시지는 한국어, 끝에 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. 브랜치 `feat/mp-m2`.
- ⚠️ **Unity 전체 `Tichu.Core.Tests` 실행 금지**(10만 판 시뮬레이터가 에디터를 점유). Unity 검증은 `Tichu.Presentation.Tests` 어셈블리만 돌린다 — 컴파일은 전 어셈블리가 되므로 미러 코드의 컴파일 오류도 여기서 잡힌다.
- **Unity 검증 절차**(Unity MCP 미연결 환경 기준, **에디터를 닫은 상태**에서):
  ```powershell
  Remove-Item "$env:TEMP\r.xml","$env:TEMP\r.log" -ErrorAction SilentlyContinue
  $p = Start-Process -FilePath "E:\6000.3.17f1\Editor\Unity.exe" -PassThru -ArgumentList @("-batchmode","-runTests","-projectPath","C:\Users\user\Desktop\Project\Tichu","-testPlatform","EditMode","-assemblyNames","Tichu.Presentation.Tests","-testResults","$env:TEMP\r.xml","-logFile","$env:TEMP\r.log")
  ```
  그다음 `$env:TEMP\r.xml`이 생길 때까지 폴링한다(`-Wait` 금지 — Android 플랫폼이면 ADB 서버 때문에 프로세스가 안 끝난다). 결과는 `<test-run ... result= total= passed= failed=>`이고, XML이 안 생기면 `r.log`에서 `error CS`를 찾는다. 끝나면 `taskkill /PID <pid> /F`.

## Review Focus

1. **좌석 2 클라이언트 화면** — `--seats 0,2`의 두 번째 접속자는 좌석 2다. 자기 손패가 아래, "나" 라벨, 프롬프트, 우리/상대 점수가 모두 자기 기준으로 보여야 한다 → Task 3의 `TichuClient` 좌석 2 완주 테스트와 홀수 좌석 팀 뒤집기 테스트.
2. **정상 종료 뒤 가짜 "연결 끊김"** — `MatchEnded` 후 서버가 소켓을 닫으면 클라이언트가 `SessionFaulted`를 만들어 결과 화면 대신 메뉴로 튕기면 안 된다 → Task 7 `No_fault_is_raised_when_server_closes_after_match_end`.
3. **마지막 `Lobby`와 첫 `StateUpdate`가 한 번에 도착** — 같은 `Drain()` 묶음에 섞여 와도 첫 화면 상태를 잃지 않아야 한다 → Task 8 `LobbyWaiterTests.Events_after_start_go_to_backlog_in_order`.
4. **서버 주소 입력 형식** — `192.168.0.5`, `192.168.0.5:9000`, 앞뒤 공백, `ws://…/ws` 전체 URL을 모두 받아야 하고, `http://`나 빈 값은 접속 시도 없이 거절해야 한다 → Task 7 `ServerAddressTests`.
5. **영원히 안 차는 방 / 만석·진행 중인 서버에 접속** — 기다리는 화면에서 갇히면 안 된다(취소 버튼으로 메뉴 복귀). 거절당하면 "기다리는 중"에 멈추지 말고 메뉴로 돌아가야 한다 → Task 6 `Connection_is_closed_when_room_is_full` + Task 7 `Rejected_client_gets_session_faulted` + Task 8 `LobbyWaiterTests.Fault_before_start_is_reported`.

---

## File Structure

| 파일 | 책임 | 태스크 |
|---|---|---|
| `core/src/Tichu.GameFlow/Agents/{PimcAgent,Pimc,Determinizer,HeuristicRolloutPolicy,PolicyConfig,ReachWeight,Difficulty,IWorldEvaluator}.cs` | PIMC 정본(이동만) | 1 |
| `core/src/Tichu.GameFlow/Agents/PacedPimcAgent.cs` | 페이싱 어댑터 정본(이동 + 네임스페이스) | 1 |
| `core/src/Tichu.GameFlow/Agents/PimcBudget.cs` | 난이도별 1수 예산표 | 1 |
| `core/src/Tichu.Net/IClientTransport.cs` · `LoopbackClient.cs` | 클라이언트 전송 이음새 · 싱글 어댑터 | 2 |
| `core/src/Tichu.Net/SeatRotation.cs` | 서버↔로컬 좌석 좌표 변환 | 3 |
| `core/src/Tichu.Net/Serialization/{ProtocolJson,ProtocolException,CardJsonConverter,CombinationJsonConverter}.cs` | JSON 직렬화 | 4 |
| `core/src/Tichu.Net/Protocol/ServerEvent.cs` | `Lobby` 종류 추가 | 4 |
| `core/src/Tichu.Server/{ServerOptions,SeatTable,TokenBucket}.cs` | 순수 설정·좌석표·레이트리밋 | 5 |
| `core/src/Tichu.Server/{WebSocketServerTransport,MatchHost,ServerApp,Program}.cs` | 서버 전송·매치 수명·앱 조립 | 6 |
| `core/src/Tichu.Net/{WebSocketClientTransport,ServerAddress}.cs` | 클라이언트 WebSocket · 주소 정규화 | 7 |
| `Assets/_Project/Presentation/Net/{TichuClient,LobbyWaiter}.cs` | 펌프(전송 이음새·회전·백로그) · 대기실 | 2·3·8 |
| `Assets/_Project/Presentation/RoundBootstrap.cs` | 네트워크 매치 분기 | 8 |
| `Assets/_Project/Presentation/Shell/*` | `NetJoin` 화면 · 알림 | 8·9 |

---

### Task 1: PIMC를 core로 승격

**Files:**
- Move (git mv, 내용 무변경): `Assets/_Project/GameFlow/Agents/{PimcAgent,Pimc,Determinizer,HeuristicRolloutPolicy,PolicyConfig,ReachWeight,Difficulty,IWorldEvaluator}.cs` → `core/src/Tichu.GameFlow/Agents/` (`.meta`는 Unity 쪽에 그대로 둔다 — 미러가 같은 자리에 다시 쓴다)
- Move + Modify: `Assets/_Project/Presentation/Agents/PacedPimcAgent.cs` → `core/src/Tichu.GameFlow/Agents/PacedPimcAgent.cs` (`.meta`는 `Assets/_Project/GameFlow/Agents/PacedPimcAgent.cs.meta`로 git mv)
- Create: `core/src/Tichu.GameFlow/Agents/PimcBudget.cs`
- Modify: `Assets/_Project/Presentation/RoundBootstrap.cs` (using · `BudgetMsFor` 위임)
- Move (테스트): `Assets/_Project/Tests/EditMode/{DeterminizerTests,HeuristicRolloutPolicyTests,PimcAgentTests,PolicyConfigTests,ReachWeightTests}.cs` → `core/tests/Tichu.Core.Tests/` (`.meta`는 그대로 둔다)
- Move + Modify: `Assets/_Project/Presentation/Tests/PacedPimcAgentTests.cs` → `core/tests/Tichu.Core.Tests/PacedPimcAgentTests.cs` (`.meta` → `Assets/_Project/Tests/EditMode/PacedPimcAgentTests.cs.meta`)
- Test: `core/tests/Tichu.Core.Tests/PimcBudgetTests.cs`

**Interfaces:**
- Produces: `namespace Tichu.GameFlow.Agents { public static class PimcBudget { public static int For(Difficulty d); } }` · `Tichu.GameFlow.Agents.PacedPimcAgent(ulong roundSeed, int seat, PolicyConfig config, int budgetMs, int delayMs, Func<bool>? fastForward = null)` (이전 `Tichu.Presentation.PacedPimcAgent`)

- [ ] **Step 1: 파일 이동 (내용 무변경 8개 + 테스트 5개)**

```bash
cd /c/Users/user/Desktop/Project/Tichu
for f in PimcAgent Pimc Determinizer HeuristicRolloutPolicy PolicyConfig ReachWeight Difficulty IWorldEvaluator; do
  git mv "Assets/_Project/GameFlow/Agents/$f.cs" "core/src/Tichu.GameFlow/Agents/$f.cs"
done
for t in DeterminizerTests HeuristicRolloutPolicyTests PimcAgentTests PolicyConfigTests ReachWeightTests; do
  git mv "Assets/_Project/Tests/EditMode/$t.cs" "core/tests/Tichu.Core.Tests/$t.cs"
done
git mv Assets/_Project/Presentation/Agents/PacedPimcAgent.cs core/src/Tichu.GameFlow/Agents/PacedPimcAgent.cs
git mv Assets/_Project/Presentation/Agents/PacedPimcAgent.cs.meta Assets/_Project/GameFlow/Agents/PacedPimcAgent.cs.meta
git mv Assets/_Project/Presentation/Tests/PacedPimcAgentTests.cs core/tests/Tichu.Core.Tests/PacedPimcAgentTests.cs
git mv Assets/_Project/Presentation/Tests/PacedPimcAgentTests.cs.meta Assets/_Project/Tests/EditMode/PacedPimcAgentTests.cs.meta
```

- [ ] **Step 2: `PacedPimcAgent` 네임스페이스·널러블·주석 정리 (동작 무변경)**

`core/src/Tichu.GameFlow/Agents/PacedPimcAgent.cs`에서:
- `using Tichu.GameFlow.Agents;` 줄을 지우고 `namespace Tichu.Presentation` → `namespace Tichu.GameFlow.Agents`
- `using Tichu.GameFlow;` 를 추가한다(`DecisionContext`·`TurnDecision`·`ExchangeChoice` — 컴파일 오류가 나면 추가, 이미 해소되면 생략)
- 필드 `private readonly Func<bool> _fastForward;` → `private readonly Func<bool>? _fastForward;`, 생성자 매개변수 `Func<bool> fastForward = null` → `Func<bool>? fastForward = null`
- 요약 주석에서 `<see cref="PimcDecisionAgent"/>` → `PimcDecisionAgent`(Presentation 전용이라 cref 불가, 일반 텍스트로)
- 마지막 두 줄
  ```
  /// PIMC 가 아직 core 에 없어(유니티 쪽 GameFlow 전용) 이 파일은 미러되지 않는다.
  /// 서버가 빈자리 AI 를 페이싱하는 일은 PIMC 의 core 승격(M5) 뒤의 몫이다.
  ```
  을 다음으로 바꾼다:
  ```
  /// 서버(Tichu.Server)도 빈자리 AI 를 이 어댑터로 페이싱한다 — 빨리감기가 없으므로 fastForward 는 null.
  ```

`core/tests/Tichu.Core.Tests/PacedPimcAgentTests.cs`에서 `namespace Tichu.Presentation.Tests` → `namespace Tichu.Core.Tests`.

`Assets/_Project/Presentation/RoundBootstrap.cs`는 `PacedPimcAgent`를 `Tichu.Presentation` 네임스페이스 안에서 이름만으로 쓰므로, 파일 맨 위 using 목록에 `using Tichu.GameFlow.Agents;`를 추가한다.

- [ ] **Step 3: 예산표 실패 테스트 작성**

`core/tests/Tichu.Core.Tests/PimcBudgetTests.cs`:

```csharp
using NUnit.Framework;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// 난이도별 1수 예산표. 서버와 유니티가 같은 표를 쓴다(티어 사다리 불변식을 두 벌로 들지 않는다).
    /// 값은 P2-G 티어 정합성 결정(Easy=탐색OFF, 나머지 900 통일 — 관전 지연과 겹쳐 체감 0)이다.
    /// </summary>
    public class PimcBudgetTests
    {
        [TestCase(Difficulty.Easy, 0)]
        [TestCase(Difficulty.Normal, 900)]
        [TestCase(Difficulty.Hard, 900)]
        [TestCase(Difficulty.Expert, 900)]
        public void Budget_matches_tier_table(Difficulty d, int expectedMs)
        {
            Assert.That(PimcBudget.For(d), Is.EqualTo(expectedMs));
        }

        [Test]
        public void Budget_is_non_decreasing_across_tiers()
        {
            Assert.That(PimcBudget.For(Difficulty.Easy), Is.LessThanOrEqualTo(PimcBudget.For(Difficulty.Normal)));
            Assert.That(PimcBudget.For(Difficulty.Normal), Is.LessThanOrEqualTo(PimcBudget.For(Difficulty.Hard)));
            Assert.That(PimcBudget.For(Difficulty.Hard), Is.LessThanOrEqualTo(PimcBudget.For(Difficulty.Expert)));
        }
    }
}
```

- [ ] **Step 4: 실패 확인**

Run: `dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~PimcBudgetTests" --nologo`
Expected: 빌드 실패 — `The name 'PimcBudget' does not exist`

- [ ] **Step 5: `PimcBudget` 구현 + `RoundBootstrap` 위임**

`core/src/Tichu.GameFlow/Agents/PimcBudget.cs`:

```csharp
namespace Tichu.GameFlow.Agents
{
    /// <summary>
    /// 난이도별 인게임 1수 시간예산(ms). 0 = 탐색 OFF(Easy).
    /// P2-G 정합성: 예산은 티어 오름차순 비감소. 탐색은 관전 딜레이(~900ms)와 겹쳐 돌아
    /// 900 이내는 체감 지연 0 → Normal/Hard/Expert 를 900으로 통일했다(예산 역전 제거).
    /// 유니티(RoundBootstrap)와 서버(Tichu.Server)가 이 표 하나를 공유한다.
    /// </summary>
    public static class PimcBudget
    {
        public static int For(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Easy:   return 0;
                case Difficulty.Normal: return 900;
                case Difficulty.Hard:   return 900;
                case Difficulty.Expert: return 900;
                default:                return 80;
            }
        }
    }
}
```

`Assets/_Project/Presentation/RoundBootstrap.cs`의 `BudgetMsFor` 본문(switch 전체)을 위임 한 줄로 바꾼다. 요약 주석은 남긴다:

```csharp
        /// <summary>난이도별 인게임 1수 시간예산(ms). 정본은 core 의 PimcBudget — 기존 테스트 호환용 위임.</summary>
        internal static int BudgetMsFor(Tichu.GameFlow.Agents.Difficulty d) => PimcBudget.For(d);
```

- [ ] **Step 6: 미러 동기 후 dotnet 테스트**

Run:
```bash
pwsh tools/sync-core-to-unity.ps1
pwsh tools/sync-core-to-unity.ps1 -Check
dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~PimcBudgetTests|FullyQualifiedName~PimcAgentTests|FullyQualifiedName~DeterminizerTests|FullyQualifiedName~HeuristicRolloutPolicyTests|FullyQualifiedName~PolicyConfigTests|FullyQualifiedName~ReachWeightTests|FullyQualifiedName~PacedPimcAgentTests" --nologo
```
Expected: `Mirror is in sync.` · 테스트 전부 PASS. 옮긴 테스트가 NUnit 4에서 컴파일 오류(고전 `Assert.AreEqual` 등)를 내면 `Assert.That(x, Is.EqualTo(y))` 꼴로 바꾼다(사전 확인으로는 고전 Assert 0건).

그다음 전체 dotnet: `dotnet test core/Tichu.sln --nologo` → 전부 PASS.

- [ ] **Step 7: Unity 컴파일·회귀 확인**

Global Constraints의 Unity 검증 절차를 돌린다.
Expected: `result="Passed"`, `failed="0"`. (`PacedPimcAgentTests`가 Presentation에서 빠졌으므로 total은 이전 180보다 그만큼 줄어든다 — 숫자를 보고에 적는다.) `error CS0246 PacedPimcAgent`가 나오면 Step 2의 using을 확인한다.

- [ ] **Step 8: Commit**

```bash
git add -A core/src/Tichu.GameFlow core/tests/Tichu.Core.Tests Assets/_Project/GameFlow Assets/_Project/Tests/EditMode Assets/_Project/Presentation/Agents Assets/_Project/Presentation/Tests Assets/_Project/Presentation/RoundBootstrap.cs
git status --short   # Library/·Build/ 등 엉뚱한 경로가 없는지 확인
git commit -m "refactor(gameflow): PIMC 를 core 정본으로 승격 + 예산표 PimcBudget

PIMC 8개 파일·PacedPimcAgent·관련 테스트를 유니티 전용에서 core 로 옮긴다(로직 무변경).
서버가 빈자리 AI 를 같은 PIMC 로 채우기 위한 선행 작업. BudgetMsFor 는 PimcBudget 위임.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: 클라이언트 전송 이음새 `IClientTransport`

**Files:**
- Create: `core/src/Tichu.Net/IClientTransport.cs`, `core/src/Tichu.Net/LoopbackClient.cs`
- Modify: `core/tests/Tichu.Core.Tests/ScriptedClient.cs`
- Modify: `Assets/_Project/Presentation/Net/TichuClient.cs`, `Assets/_Project/Presentation/RoundBootstrap.cs:85`
- Modify: `Assets/_Project/Presentation/Tests/TichuClientTests.cs` (생성 지점 7곳)
- Test: `core/tests/Tichu.Core.Tests/LoopbackClientTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public interface IClientTransport { IReadOnlyList<ServerEvent> Drain(); void Submit(ClientAction a); }
  public sealed class LoopbackClient : IClientTransport { public LoopbackClient(LoopbackTransport transport, int seat); }
  // TichuClient (Presentation)
  public TichuClient(IClientTransport transport, TableViewModel vm, int mySeat,
      Action<MatchSummary> onMatchEnded = null, IReadOnlyList<ServerEvent> backlog = null)
  // ScriptedClient (tests)
  public ScriptedClient(int seat, IClientTransport transport); public ScriptedClient(int seat, LoopbackTransport transport);
  ```

- [ ] **Step 1: 실패 테스트**

`core/tests/Tichu.Core.Tests/LoopbackClientTests.cs`:

```csharp
using NUnit.Framework;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    public class LoopbackClientTests
    {
        [Test]
        public void Drain_returns_only_the_bound_seat_events()
        {
            var transport = new LoopbackTransport();
            transport.SendTo(0, ServerEvent.ActionRejected(1, "zero"));
            transport.SendTo(2, ServerEvent.ActionRejected(2, "two"));

            var client = new LoopbackClient(transport, 2);
            var got = client.Drain();

            Assert.That(got.Count, Is.EqualTo(1));
            Assert.That(got[0].Reason, Is.EqualTo("two"));
            Assert.That(client.Drain().Count, Is.EqualTo(0), "한 번 비운 수신함은 다시 비어 있어야 한다");
            Assert.That(transport.Drain(0).Count, Is.EqualTo(1), "다른 좌석 수신함은 건드리지 않는다");
        }

        [Test]
        public void Submit_forwards_to_the_session_handler()
        {
            var transport = new LoopbackTransport();
            ClientAction? received = null;
            transport.OnClientAction(a => received = a);

            new LoopbackClient(transport, 1).Submit(ClientAction.Pass(1, 7));

            Assert.That(received, Is.Not.Null);
            Assert.That(received!.ActionSeq, Is.EqualTo(7));
        }
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~LoopbackClientTests" --nologo`
Expected: 빌드 실패 — `LoopbackClient` 없음

- [ ] **Step 3: 구현**

`core/src/Tichu.Net/IClientTransport.cs`:

```csharp
using System.Collections.Generic;

namespace Tichu.Net
{
    /// <summary>
    /// 클라이언트가 보는 전송 계층. 서버 쪽 ITichuTransport 의 짝이다 — 클라이언트는 자기 좌석
    /// 하나만 다루므로 좌석 인자가 없다. 뒤에 LoopbackClient(싱글) / WebSocketClientTransport(M2) 가 온다.
    /// Drain 은 메인 스레드 폴링용이고, Submit 은 막히지 않고 즉시 돌아와야 한다.
    /// </summary>
    public interface IClientTransport
    {
        /// <summary>아직 가져가지 않은 이벤트를 도착 순서대로 꺼낸다.</summary>
        IReadOnlyList<ServerEvent> Drain();

        /// <summary>액션을 서버로 보낸다.</summary>
        void Submit(ClientAction a);
    }
}
```

`core/src/Tichu.Net/LoopbackClient.cs`:

```csharp
using System.Collections.Generic;

namespace Tichu.Net
{
    /// <summary>같은 프로세스 루프백을 좌석 하나에 묶은 클라이언트 전송. 싱글플레이가 쓴다.</summary>
    public sealed class LoopbackClient : IClientTransport
    {
        private readonly LoopbackTransport _transport;
        private readonly int _seat;

        public LoopbackClient(LoopbackTransport transport, int seat)
        {
            _transport = transport;
            _seat = seat;
        }

        public IReadOnlyList<ServerEvent> Drain() => _transport.Drain(_seat);

        public void Submit(ClientAction a) => _transport.Submit(a);
    }
}
```

`core/tests/Tichu.Core.Tests/ScriptedClient.cs`:
- 필드 `private readonly LoopbackTransport _transport;` → `private readonly IClientTransport _transport;`
- 생성자를 둘로:
  ```csharp
        public ScriptedClient(int seat, IClientTransport transport)
        {
            _seat = seat;
            _transport = transport;
        }

        public ScriptedClient(int seat, LoopbackTransport transport)
            : this(seat, new LoopbackClient(transport, seat)) { }
  ```
- `Pump()`의 `foreach (var ev in _transport.Drain(_seat))` → `foreach (var ev in _transport.Drain())`

`Assets/_Project/Presentation/Net/TichuClient.cs`:
- `using System.Collections.Generic;` 추가
- 필드 `private readonly LoopbackTransport _transport;` → `private readonly IClientTransport _transport;`, 그리고 `private IReadOnlyList<ServerEvent> _backlog;` 추가
- 생성자:
  ```csharp
        /// <param name="backlog">대기실(LobbyWaiter)이 이미 꺼내 둔 이벤트. 첫 Pump 에서 먼저 적용한다.</param>
        public TichuClient(IClientTransport transport, TableViewModel vm, int mySeat,
            Action<MatchSummary> onMatchEnded = null, IReadOnlyList<ServerEvent> backlog = null)
        {
            _transport = transport;
            _vm = vm;
            _mySeat = mySeat;
            _onMatchEnded = onMatchEnded;
            _backlog = backlog;
            _outbound = _vm.Submitted.Subscribe(_transport.Submit);
        }
  ```
- `Pump()`:
  ```csharp
        public void Pump()
        {
            if (_backlog != null)
            {
                var pending = _backlog;
                _backlog = null;
                for (int i = 0; i < pending.Count; i++) Handle(pending[i]);
            }

            var events = _transport.Drain();
            for (int i = 0; i < events.Count; i++) Handle(events[i]);
        }
  ```
- 클래스 요약 주석의 "전송 수신함" 설명은 그대로 둔다. `_mySeat`는 Task 3에서 쓴다(지금은 미사용 경고가 나도 둔다).

`Assets/_Project/Presentation/RoundBootstrap.cs:85`:
`using var client = new TichuClient(transport, vm, _args.MySeat, s => summary = s);` →
`using var client = new TichuClient(new LoopbackClient(transport, _args.MySeat), vm, _args.MySeat, s => summary = s);`

`Assets/_Project/Presentation/Tests/TichuClientTests.cs`: `new TichuClient(transport, vm, MySeat` 일곱 곳을 전부 `new TichuClient(new LoopbackClient(transport, MySeat), vm, MySeat` 로 바꾼다(`grep -n "new TichuClient(transport" ...`으로 0건 확인).

- [ ] **Step 4: 통과 확인 (dotnet)**

Run:
```bash
pwsh tools/sync-core-to-unity.ps1
dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~LoopbackClientTests|FullyQualifiedName~LoopbackMatchTests" --nologo
```
Expected: PASS (LoopbackMatchTests는 ScriptedClient 경유 회귀 확인)

- [ ] **Step 5: 백로그 테스트 (Unity, `TichuClientTests`에 추가)**

```csharp
        [Test]
        public void Backlog_is_applied_before_new_events()
        {
            // 대기실이 미리 꺼낸 이벤트(라운드 결과)가 먼저 적용돼야 한다 — 순서가 뒤집히면
            // RoundScored 뒤 첫 StateUpdate 에서 비우는 "새 라운드" 판정이 어긋난다.
            var transport = new LoopbackTransport();
            var vm = new TableViewModel(MySeat);
            var round = new RoundResult { TeamATotal = 100, TeamBTotal = 0 };
            var backlog = new List<ServerEvent> { ServerEvent.RoundScored(round) };

            using var client = new TichuClient(new LoopbackClient(transport, MySeat), vm, MySeat, null, backlog);
            client.Pump();

            Assert.That(vm.CumulativeA.CurrentValue, Is.EqualTo(100), "백로그의 RoundScored 가 적용돼야 한다");
            client.Pump();
            Assert.That(vm.CumulativeA.CurrentValue, Is.EqualTo(100), "백로그는 한 번만 적용된다");
        }
```

(`TableViewModel.OnRoundScored`가 `CumulativeA.Value += r.TeamATotal`이라 백로그 적용 여부가 그대로 관측된다.)

- [ ] **Step 6: Unity 확인**

Unity 검증 절차 → `failed="0"`.

- [ ] **Step 7: Commit**

```bash
git add core/src/Tichu.Net core/tests/Tichu.Core.Tests Assets/_Project/Net Assets/_Project/Tests/EditMode Assets/_Project/Presentation/Net/TichuClient.cs Assets/_Project/Presentation/RoundBootstrap.cs Assets/_Project/Presentation/Tests/TichuClientTests.cs
git commit -m "feat(net): 클라이언트 전송 이음새 IClientTransport + LoopbackClient

TichuClient 가 LoopbackTransport 구체 타입 대신 이음새에 의존한다(재배선 이월분).
대기실이 먼저 꺼낸 이벤트를 넘길 backlog 인자를 더한다. 싱글플레이 동작 동일.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: 좌석 회전 `SeatRotation`

**Files:**
- Create: `core/src/Tichu.Net/SeatRotation.cs`
- Modify: `Assets/_Project/Presentation/Net/TichuClient.cs`
- Test: `core/tests/Tichu.Core.Tests/SeatRotationTests.cs`, `Assets/_Project/Presentation/Tests/TichuClientTests.cs`

**Interfaces:**
- Consumes: Task 2 `TichuClient(IClientTransport, TableViewModel, int mySeat, ...)`
- Produces:
  ```csharp
  public static class SeatRotation {
      public static int ToLocalSeat(int abs, int mySeat);    // 음수는 그대로
      public static int ToServerSeat(int local, int mySeat); // 음수는 그대로
      public static ServerEvent ToLocal(ServerEvent ev, int mySeat);   // mySeat==0 이면 같은 객체
      public static ClientAction ToServer(ClientAction a, int mySeat); // mySeat==0 이면 같은 객체
  }
  ```
  규약: `TichuClient`의 뷰모델은 **항상 `new TableViewModel(0)`** 으로 만든다(호출자 책임). `Lobby.YourSeat`는 회전하지 않는다(대기실이 회전 전에 읽는 절대 좌석 배정이다).

- [ ] **Step 1: 실패 테스트**

`core/tests/Tichu.Core.Tests/SeatRotationTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// 테이블 UI 는 "나 = 좌석 0" 을 가정한다. 클라이언트 경계에서 서버 좌표를 로컬로 돌리고
    /// 보낼 때 되돌린다. 홀수 좌석이면 팀 A/B 가 뒤집힌다(로컬 팀 A = 나·파트너).
    /// </summary>
    public class SeatRotationTests
    {
        private static PlayerView View(int viewer) => new PlayerView
        {
            Viewer = viewer,
            Turn = 3,
            DragonGiftWinner = 1,
            HandCounts = new[] { 10, 11, 12, 13 },
            Calls = new[] { TichuCall.None, TichuCall.Tichu, TichuCall.None, TichuCall.GrandTichu },
            IsOut = new[] { false, true, false, false },
            FinishOrder = new[] { 0, 1, 0, 0 },
            WonCards = new[] { new List<Card>(), new List<Card> { Card.Dragon }, new List<Card>(), new List<Card>() },
            CurrentTrick = new TrickView
            {
                TopOwnerSeat = 2,
                History = new List<PlayView> { new PlayView { Seat = 2 }, new PlayView { Seat = 3, IsPass = true } }
            },
            CompletedTricks = new List<TrickSummary> { new TrickSummary { TopOwnerSeat = 1, DragonGiftRecipient = 2 } },
            TeamAScore = 300,
            TeamBScore = 150,
            TargetScore = 1000,
            Awaiting = new Awaiting { Kind = StepKind.Play, Seat = 2, CanPass = true },
        };

        [TestCase(0, 0)] [TestCase(1, 3)] [TestCase(2, 2)] [TestCase(3, 1)]
        public void Seat_two_is_rotated_relative_to_my_seat(int mySeat, int expectedLocal)
        {
            Assert.That(SeatRotation.ToLocalSeat(2, mySeat), Is.EqualTo(expectedLocal));
        }

        [Test]
        public void Seat_round_trips_for_every_pair_and_negative_is_preserved()
        {
            for (int my = 0; my < 4; my++)
            {
                for (int abs = 0; abs < 4; abs++)
                    Assert.That(SeatRotation.ToServerSeat(SeatRotation.ToLocalSeat(abs, my), my), Is.EqualTo(abs));
                Assert.That(SeatRotation.ToLocalSeat(-1, my), Is.EqualTo(-1));
                Assert.That(SeatRotation.ToServerSeat(-1, my), Is.EqualTo(-1));
            }
            Assert.That(SeatRotation.ToLocalSeat(0, 0), Is.EqualTo(0));
        }

        [Test]
        public void My_seat_zero_is_identity()
        {
            var ev = ServerEvent.StateUpdate(View(0), null);
            Assert.That(SeatRotation.ToLocal(ev, 0), Is.SameAs(ev));
            var a = ClientAction.Pass(0, 1);
            Assert.That(SeatRotation.ToServer(a, 0), Is.SameAs(a));
        }

        [Test]
        public void Even_seat_rotates_seats_without_flipping_teams()
        {
            var awaiting = new Awaiting { Kind = StepKind.Play, Seat = 2, CanPass = true };
            var ev = SeatRotation.ToLocal(ServerEvent.StateUpdate(View(2), awaiting), 2);
            var v = ev.View!;

            Assert.That(v.Viewer, Is.EqualTo(0));
            Assert.That(v.Turn, Is.EqualTo(1));
            Assert.That(v.DragonGiftWinner, Is.EqualTo(3));
            Assert.That(v.HandCounts, Is.EqualTo(new[] { 12, 13, 10, 11 }));
            Assert.That(v.Calls[3], Is.EqualTo(TichuCall.Tichu));
            Assert.That(v.IsOut[3], Is.True);
            Assert.That(v.WonCards[3].Count, Is.EqualTo(1));
            Assert.That(v.CurrentTrick!.TopOwnerSeat, Is.EqualTo(0));
            Assert.That(v.CurrentTrick.History[0].Seat, Is.EqualTo(0));
            Assert.That(v.CurrentTrick.History[1].Seat, Is.EqualTo(1));
            Assert.That(v.CompletedTricks[0].TopOwnerSeat, Is.EqualTo(3));
            Assert.That(v.CompletedTricks[0].DragonGiftRecipient, Is.EqualTo(0));
            Assert.That(v.Awaiting!.Seat, Is.EqualTo(0));
            Assert.That(v.Awaiting.CanPass, Is.True);
            Assert.That(v.TeamAScore, Is.EqualTo(300), "짝수 좌석은 팀이 그대로다");
            Assert.That(v.TeamBScore, Is.EqualTo(150));
        }

        [Test]
        public void Odd_seat_flips_teams_in_view_round_and_match_end()
        {
            var v = SeatRotation.ToLocal(ServerEvent.StateUpdate(View(1), null), 1).View!;
            Assert.That(v.TeamAScore, Is.EqualTo(150), "좌석 1 의 팀은 서버 팀 B");
            Assert.That(v.TeamBScore, Is.EqualTo(300));

            var round = new RoundResult
            {
                TeamACardPoints = 70, TeamBCardPoints = 30, TeamATichuDelta = 100, TeamBTichuDelta = 0,
                TeamATotal = 170, TeamBTotal = 30
            };
            var r = SeatRotation.ToLocal(ServerEvent.RoundScored(round), 1).Round!;
            Assert.That(r.TeamACardPoints, Is.EqualTo(30));
            Assert.That(r.TeamBCardPoints, Is.EqualTo(70));
            Assert.That(r.TeamATichuDelta, Is.EqualTo(0));
            Assert.That(r.TeamBTichuDelta, Is.EqualTo(100));
            Assert.That(r.TeamATotal, Is.EqualTo(30));
            Assert.That(r.TeamBTotal, Is.EqualTo(170));
            Assert.That(round.TeamATotal, Is.EqualTo(170), "원본은 건드리지 않는다");

            var end = SeatRotation.ToLocal(ServerEvent.MatchEnded(0, 1000, 600), 3);
            Assert.That(end.WinningTeam, Is.EqualTo(1), "서버 팀 A 승리 = 좌석 3 에겐 상대 팀 승리");
            Assert.That(end.TeamA, Is.EqualTo(600));
            Assert.That(end.TeamB, Is.EqualTo(1000));
        }

        [Test]
        public void Applied_action_seats_rotate_and_missing_recipient_stays_minus_one()
        {
            var play = new AppliedAction { Kind = GameActionKind.Play, Seat = 3, RecipientSeat = -1 };
            var gift = new AppliedAction { Kind = GameActionKind.GiveDragon, Seat = 0, RecipientSeat = 1 };

            var p = SeatRotation.ToLocal(ServerEvent.StateUpdate(View(2), null, play), 2).Applied!;
            var g = SeatRotation.ToLocal(ServerEvent.StateUpdate(View(2), null, gift), 2).Applied!;

            Assert.That(p.Seat, Is.EqualTo(1));
            Assert.That(p.RecipientSeat, Is.EqualTo(-1));
            Assert.That(g.Seat, Is.EqualTo(2));
            Assert.That(g.RecipientSeat, Is.EqualTo(3));
        }

        [Test]
        public void Lobby_seat_is_not_rotated()
        {
            var ev = SeatRotation.ToLocal(ServerEvent.Lobby(2, 1, 2), 2);
            Assert.That(ev.YourSeat, Is.EqualTo(2), "대기실이 회전 전에 읽는 절대 좌석 배정");
            Assert.That(ev.Connected, Is.EqualTo(1));
            Assert.That(ev.Required, Is.EqualTo(2));
        }

        [Test]
        public void Outgoing_actions_rotate_back_and_exchange_targets_stay_relative()
        {
            var gift = SeatRotation.ToServer(ClientAction.GiveDragon(0, 5, 1), 2);
            Assert.That(gift.Seat, Is.EqualTo(2));
            Assert.That(gift.RecipientSeat, Is.EqualTo(3));
            Assert.That(gift.ActionSeq, Is.EqualTo(5));

            var ex = SeatRotation.ToServer(ClientAction.Exchange(0, 6, Card.Dog, Card.Dragon, Card.Phoenix), 3);
            Assert.That(ex.Seat, Is.EqualTo(3));
            Assert.That(ex.ToLeft, Is.EqualTo(Card.Dog));
            Assert.That(ex.ToPartner, Is.EqualTo(Card.Dragon));
            Assert.That(ex.ToRight, Is.EqualTo(Card.Phoenix));

            var pass = SeatRotation.ToServer(ClientAction.Pass(0, 7), 1);
            Assert.That(pass.RecipientSeat, Is.EqualTo(0), "GiveDragon 이 아니면 RecipientSeat 을 건드리지 않는다");
        }
    }
}
```

`ServerEvent.Lobby`는 Task 4에서 만든다. **이 태스크에서는 `Lobby_seat_is_not_rotated` 테스트를 Task 4 Step 1로 옮겨 적는다** — 이 파일에는 처음부터 넣지 않는다(빌드가 안 되므로). 아래 구현의 `YourSeat`/`Connected`/`Required` 복사 줄도 Task 4에서 추가한다.

- [ ] **Step 2: 실패 확인**

Run: `dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~SeatRotationTests" --nologo`
Expected: 빌드 실패 — `SeatRotation` 없음

- [ ] **Step 3: 구현**

`core/src/Tichu.Net/SeatRotation.cs`:

```csharp
using System.Collections.Generic;
using Tichu.Core.Game;

namespace Tichu.Net
{
    /// <summary>
    /// 서버 좌표 ↔ 클라이언트 로컬 좌표 변환. 테이블 UI 는 "나 = 좌석 0"(아래), 1 = 오른쪽,
    /// 2 = 파트너, 3 = 왼쪽을 가정하고 팀 A 를 우리 팀으로 그린다. 서버는 절대 좌석을 쓴다.
    /// 클라이언트 경계(TichuClient)에서 받을 때 ToLocal, 보낼 때 ToServer 를 적용하면
    /// 뷰모델·뷰는 어느 좌석에 앉든 좌석 0 경로 하나로 동작한다.
    ///
    /// 회전: local = (abs − mySeat + 4) % 4. 음수(예: RecipientSeat = −1 "없음")는 그대로.
    /// 내 좌석이 홀수면 로컬 팀 A(0·2) = 서버 팀 B 이므로 팀 단위 값도 맞바꾼다.
    /// 입력 객체는 건드리지 않고 새 객체를 만든다(같은 이벤트를 다른 좌석이 볼 수 있다).
    /// Lobby.YourSeat 는 회전하지 않는다 — 대기실이 회전 전에 읽는 절대 좌석 배정이다.
    /// </summary>
    public static class SeatRotation
    {
        private const int SeatCount = 4;

        public static int ToLocalSeat(int abs, int mySeat) => abs < 0 ? abs : (abs - mySeat + SeatCount) % SeatCount;

        public static int ToServerSeat(int local, int mySeat) => local < 0 ? local : (local + mySeat) % SeatCount;

        public static ServerEvent ToLocal(ServerEvent ev, int mySeat)
        {
            if (mySeat == 0) return ev;
            bool flip = (mySeat & 1) == 1;

            return new ServerEvent
            {
                Kind = ev.Kind,
                Reason = ev.Reason,
                ActionSeq = ev.ActionSeq,
                View = ev.View == null ? null : RotateView(ev.View, mySeat, flip),
                Round = ev.Round == null ? null : (flip ? Flip(ev.Round) : ev.Round),
                Applied = ev.Applied == null ? null : RotateApplied(ev.Applied, mySeat),
                WinningTeam = flip && ev.Kind == ServerEventKind.MatchEnded ? 1 - ev.WinningTeam : ev.WinningTeam,
                TeamA = flip ? ev.TeamB : ev.TeamA,
                TeamB = flip ? ev.TeamA : ev.TeamB,
            };
        }

        public static ClientAction ToServer(ClientAction a, int mySeat)
        {
            if (mySeat == 0) return a;

            return new ClientAction
            {
                Kind = a.Kind,
                Seat = ToServerSeat(a.Seat, mySeat),
                ActionSeq = a.ActionSeq,
                Cards = a.Cards,
                Rank = a.Rank,
                Wish = a.Wish,
                ToLeft = a.ToLeft,          // 교환 대상은 원래 상대 좌표(왼쪽/파트너/오른쪽)다
                ToPartner = a.ToPartner,
                ToRight = a.ToRight,
                RecipientSeat = a.Kind == ClientActionKind.GiveDragon
                    ? ToServerSeat(a.RecipientSeat, mySeat)
                    : a.RecipientSeat,
            };
        }

        private static PlayerView RotateView(PlayerView v, int my, bool flip)
        {
            var completed = new List<TrickSummary>(v.CompletedTricks.Count);
            foreach (var t in v.CompletedTricks)
                completed.Add(new TrickSummary
                {
                    TopOwnerSeat = ToLocalSeat(t.TopOwnerSeat, my),
                    AccumulatedPoints = t.AccumulatedPoints,
                    WonByDragon = t.WonByDragon,
                    DragonGiftRecipient = t.DragonGiftRecipient.HasValue
                        ? ToLocalSeat(t.DragonGiftRecipient.Value, my) : (int?)null,
                });

            return new PlayerView
            {
                Viewer = ToLocalSeat(v.Viewer, my),
                Phase = v.Phase,
                Turn = ToLocalSeat(v.Turn, my),
                Wish = v.Wish,
                DragonGiftWinner = v.DragonGiftWinner.HasValue ? ToLocalSeat(v.DragonGiftWinner.Value, my) : (int?)null,
                MyHand = v.MyHand,
                HandCounts = Permute(v.HandCounts, my),
                Calls = Permute(v.Calls, my),
                IsOut = Permute(v.IsOut, my),
                FinishOrder = Permute(v.FinishOrder, my),
                WonCards = Permute(v.WonCards, my),
                CurrentTrick = v.CurrentTrick == null ? null : RotateTrick(v.CurrentTrick, my),
                CompletedTricks = completed,
                TeamAScore = flip ? v.TeamBScore : v.TeamAScore,
                TeamBScore = flip ? v.TeamAScore : v.TeamBScore,
                TargetScore = v.TargetScore,
                Awaiting = v.Awaiting == null ? null : new Awaiting
                {
                    Kind = v.Awaiting.Kind,
                    Seat = ToLocalSeat(v.Awaiting.Seat, my),
                    DeadlineUtc = v.Awaiting.DeadlineUtc,
                    LegalMoves = v.Awaiting.LegalMoves,
                    CanPass = v.Awaiting.CanPass,
                },
            };
        }

        private static TrickView RotateTrick(TrickView t, int my)
        {
            var history = new List<PlayView>(t.History.Count);
            foreach (var p in t.History)
                history.Add(new PlayView
                {
                    Seat = ToLocalSeat(p.Seat, my),
                    IsPass = p.IsPass,
                    IsBombInterrupt = p.IsBombInterrupt,
                    Cards = p.Cards,
                });

            return new TrickView
            {
                LeadType = t.LeadType,
                LeadLength = t.LeadLength,
                Top = t.Top,
                TopOwnerSeat = ToLocalSeat(t.TopOwnerSeat, my),
                AccumulatedPoints = t.AccumulatedPoints,
                History = history,
            };
        }

        private static AppliedAction RotateApplied(AppliedAction a, int my) => new AppliedAction
        {
            Kind = a.Kind,
            Seat = ToLocalSeat(a.Seat, my),
            Cards = a.Cards,
            RecipientSeat = ToLocalSeat(a.RecipientSeat, my),
        };

        private static RoundResult Flip(RoundResult r) => new RoundResult
        {
            TeamACardPoints = r.TeamBCardPoints,
            TeamBCardPoints = r.TeamACardPoints,
            TeamATichuDelta = r.TeamBTichuDelta,
            TeamBTichuDelta = r.TeamATichuDelta,
            TeamATotal = r.TeamBTotal,
            TeamBTotal = r.TeamATotal,
        };

        private static T[] Permute<T>(T[] abs, int my)
        {
            var local = new T[abs.Length];
            for (int i = 0; i < abs.Length; i++) local[ToLocalSeat(i, my)] = abs[i];
            return local;
        }
    }
}
```

- [ ] **Step 4: 통과 확인**

Run: `pwsh tools/sync-core-to-unity.ps1; dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~SeatRotationTests" --nologo`
Expected: PASS

- [ ] **Step 5: `TichuClient`에 회전 적용**

`Assets/_Project/Presentation/Net/TichuClient.cs`:
- 생성자의 `_outbound = _vm.Submitted.Subscribe(_transport.Submit);` → `_outbound = _vm.Submitted.Subscribe(a => _transport.Submit(SeatRotation.ToServer(a, _mySeat)));`
- `Pump()`의 두 `Handle(...)` 호출을 `Handle(SeatRotation.ToLocal(pending[i], _mySeat))` / `Handle(SeatRotation.ToLocal(events[i], _mySeat))`로
- 생성자 위 주석에 한 줄: `/// 뷰모델은 항상 좌석 0 기준(new TableViewModel(0))이어야 한다 — 회전은 여기서 한다.`

`Assets/_Project/Presentation/RoundBootstrap.cs:51` `var vm = new TableViewModel(args.MySeat);` → `var vm = new TableViewModel(0);   // 뷰모델은 항상 "나 = 좌석 0" — 회전은 TichuClient 가 한다`

- [ ] **Step 6: 좌석 2 완주 테스트 (Unity, `TichuClientTests`에 추가)**

```csharp
        [Test]
        public void Client_seated_at_two_sees_itself_as_local_seat_zero_and_finishes()
        {
            const int serverSeat = 2;
            var transport = new LoopbackTransport();
            var vm = new TableViewModel(0);                 // 뷰모델은 항상 로컬 좌석 0
            MatchSummary? summary = null;
            bool sawMyPrompt = false;
            var agents = new IAgent[] { new AiAgent(1UL, 0), new AiAgent(1UL, 1), null, new AiAgent(1UL, 3) };

            using (var client = new TichuClient(new LoopbackClient(transport, serverSeat), vm, serverSeat, s => summary = s))
            using (vm.Prompt.Subscribe(aw => { if (aw != null && aw.Seat == 0) sawMyPrompt = true; }))
            {
                var responder = new AutoResponder(vm);
                var session = new MatchSession(agents, transport, masterSeed: 4242UL, targetScore: 1);
                session.Start();
                try
                {
                    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
                    while (!summary.HasValue && DateTime.UtcNow < deadline)
                    {
                        client.Pump();
                        responder.Tick();
                        Thread.Sleep(2);
                    }
                }
                finally
                {
                    session.Stop();
                    session.Join(TimeSpan.FromSeconds(5));
                }
            }

            Assert.That(summary.HasValue, Is.True, "좌석 2 클라이언트도 매치를 끝까지 둬야 한다(회전된 액션이 서버에서 받아들여짐)");
            Assert.That(sawMyPrompt, Is.True, "내 차례 프롬프트가 로컬 좌석 0 으로 와야 한다");
        }
```

- [ ] **Step 7: Unity 확인**

Unity 검증 절차 → `failed="0"`. 이 테스트가 시간 초과로 실패하면 `AutoResponder`의 용 양도 대상(로컬 1)이 회전돼 서버 좌석 3으로 갔는지부터 본다.

- [ ] **Step 8: Commit**

```bash
git add core/src/Tichu.Net core/tests/Tichu.Core.Tests Assets/_Project/Net Assets/_Project/Tests/EditMode Assets/_Project/Presentation/Net/TichuClient.cs Assets/_Project/Presentation/RoundBootstrap.cs Assets/_Project/Presentation/Tests/TichuClientTests.cs
git commit -m "feat(net): 클라이언트 경계 좌석 회전 SeatRotation

테이블 UI 의 '나 = 좌석 0' 가정을 지키도록 받을 때 로컬로, 보낼 때 서버로 돌린다.
홀수 좌석은 팀 점수·라운드 결과·승리 팀을 맞바꾼다. 뷰모델·뷰 무변경.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: JSON 직렬화 + `Lobby` 이벤트

**Files:**
- Modify: `core/src/Tichu.Net/Tichu.Net.csproj` (Newtonsoft 참조)
- Create: `core/src/Tichu.Net/Serialization/ProtocolJson.cs`, `ProtocolException.cs`, `CardJsonConverter.cs`, `CombinationJsonConverter.cs`
- Modify: `core/src/Tichu.Net/Protocol/ServerEvent.cs`, `core/src/Tichu.Net/SeatRotation.cs`
- Modify: `Packages/manifest.json`, `Assets/_Project/Net/Tichu.Net.asmdef`
- Create: `Assets/link.xml`
- Test: `core/tests/Tichu.Core.Tests/ProtocolJsonTests.cs`, `core/tests/Tichu.Core.Tests/SeatRotationTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public static class ProtocolJson {
      public static string Serialize(ServerEvent ev); public static string Serialize(ClientAction a);
      public static ServerEvent DeserializeServerEvent(string json);   // 실패 시 ProtocolException
      public static ClientAction DeserializeClientAction(string json); // 실패 시 ProtocolException
  }
  public sealed class ProtocolException : Exception
  // ServerEvent
  ServerEventKind.Lobby; public int YourSeat; public int Connected; public int Required;
  public static ServerEvent Lobby(int yourSeat, int connected, int required);
  ```
  카드 JSON 형식(테스트가 의존): `{"r":<Rank>,"s":"<Suit>","x":"<Special>","p":<Points>}` — 이 순서·이 키.

- [ ] **Step 1: 실패 테스트**

`SeatRotationTests.cs`에 Task 3에서 미뤄 둔 `Lobby_seat_is_not_rotated` 테스트를 추가한다(Task 3 Step 1의 코드 그대로).

`core/tests/Tichu.Core.Tests/ProtocolJsonTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// 프로토콜 JSON 왕복. 유니티(IL2CPP)와 서버(dotnet)가 같은 코드로 주고받는다.
    /// 미러되는 테스트라 Newtonsoft 타입을 직접 쓰지 않는다(ProtocolJson·ProtocolException 만).
    /// </summary>
    public class ProtocolJsonTests
    {
        private static string Fragment(Card c) =>
            "{\"r\":" + c.Rank + ",\"s\":\"" + c.Suit + "\",\"x\":\"" + c.Special + "\",\"p\":" + c.Points + "}";

        private static ServerEvent RoundTrip(ServerEvent ev) =>
            ProtocolJson.DeserializeServerEvent(ProtocolJson.Serialize(ev));

        private static ClientAction RoundTrip(ClientAction a) =>
            ProtocolJson.DeserializeClientAction(ProtocolJson.Serialize(a));

        [Test]
        public void Every_client_action_kind_round_trips()
        {
            var cards = new List<Card> { Card.Normal(5, Suit.Jade), Card.Phoenix };

            var play = RoundTrip(ClientAction.Play(2, 9, cards, 10, 7));
            Assert.That(play.Kind, Is.EqualTo(ClientActionKind.Play));
            Assert.That(play.Seat, Is.EqualTo(2));
            Assert.That(play.ActionSeq, Is.EqualTo(9));
            Assert.That(play.Cards, Is.EqualTo(cards));
            Assert.That(play.Rank, Is.EqualTo(10));
            Assert.That(play.Wish, Is.EqualTo(7));

            var ex = RoundTrip(ClientAction.Exchange(1, 3, Card.Dog, Card.Mahjong, Card.Dragon));
            Assert.That(ex.ToLeft, Is.EqualTo(Card.Dog));
            Assert.That(ex.ToPartner, Is.EqualTo(Card.Mahjong));
            Assert.That(ex.ToRight, Is.EqualTo(Card.Dragon));

            Assert.That(RoundTrip(ClientAction.GiveDragon(0, 4, 3)).RecipientSeat, Is.EqualTo(3));
            Assert.That(RoundTrip(ClientAction.CallGrandTichu(0, 1)).Kind, Is.EqualTo(ClientActionKind.CallGrandTichu));
            Assert.That(RoundTrip(ClientAction.DeclineGrandTichu(0, 1)).Kind, Is.EqualTo(ClientActionKind.DeclineGrandTichu));
            Assert.That(RoundTrip(ClientAction.CallTichu(0, 1)).Kind, Is.EqualTo(ClientActionKind.CallTichu));
            Assert.That(RoundTrip(ClientAction.Pass(0, 1)).Kind, Is.EqualTo(ClientActionKind.Pass));
        }

        [Test]
        public void Special_cards_keep_rank_suit_kind_and_points()
        {
            foreach (var c in new[] { Card.Mahjong, Card.Dog, Card.Phoenix, Card.Dragon, Card.Normal(13, Suit.Star) })
            {
                var back = RoundTrip(ClientAction.Play(0, 1, new List<Card> { c }, 0, null)).Cards![0];
                Assert.That(back, Is.EqualTo(c));
                Assert.That(back.Points, Is.EqualTo(c.Points), c.ToString());
            }
        }

        [Test]
        public void State_update_with_full_view_round_trips()
        {
            var top = new Combination(CombinationType.Pair,
                new[] { Card.Normal(9, Suit.Sword), Card.Normal(9, Suit.Pagoda) }, 2, 18, 0);
            var view = new PlayerView
            {
                Viewer = 1, Phase = RoundPhase.Play, Turn = 2, Wish = 8, DragonGiftWinner = 3,
                MyHand = new List<Card> { Card.Normal(14, Suit.Star), Card.Dragon },
                HandCounts = new[] { 3, 2, 5, 0 },
                Calls = new[] { TichuCall.None, TichuCall.Tichu, TichuCall.None, TichuCall.GrandTichu },
                IsOut = new[] { false, false, false, true },
                FinishOrder = new[] { 0, 0, 0, 1 },
                WonCards = new[] { new List<Card> { Card.Normal(5, Suit.Jade) }, new List<Card>(), new List<Card>(), new List<Card>() },
                CurrentTrick = new TrickView
                {
                    LeadType = CombinationType.Pair, LeadLength = 2, Top = top, TopOwnerSeat = 0, AccumulatedPoints = 10,
                    History = new List<PlayView> { new PlayView { Seat = 0, Cards = new List<Card>(top.Cards) }, new PlayView { Seat = 1, IsPass = true } },
                },
                CompletedTricks = new List<TrickSummary> { new TrickSummary { TopOwnerSeat = 3, AccumulatedPoints = 25, WonByDragon = true, DragonGiftRecipient = 0 } },
                TeamAScore = 120, TeamBScore = 80, TargetScore = 1000,
            };
            var awaiting = new Awaiting { Kind = StepKind.Play, Seat = 1, CanPass = true, LegalMoves = new List<Combination> { top } };
            var applied = new AppliedAction { Kind = GameActionKind.Play, Seat = 0, Cards = new List<Card>(top.Cards), RecipientSeat = -1 };

            var back = RoundTrip(ServerEvent.StateUpdate(view, awaiting, applied));

            var v = back.View!;
            Assert.That(back.Kind, Is.EqualTo(ServerEventKind.StateUpdate));
            Assert.That(v.Viewer, Is.EqualTo(1));
            Assert.That(v.Phase, Is.EqualTo(RoundPhase.Play));
            Assert.That(v.Wish, Is.EqualTo(8));
            Assert.That(v.DragonGiftWinner, Is.EqualTo(3));
            Assert.That(v.MyHand, Is.EqualTo(view.MyHand));
            Assert.That(v.HandCounts, Is.EqualTo(view.HandCounts));
            Assert.That(v.Calls, Is.EqualTo(view.Calls));
            Assert.That(v.IsOut, Is.EqualTo(view.IsOut));
            Assert.That(v.FinishOrder, Is.EqualTo(view.FinishOrder));
            Assert.That(v.WonCards[0], Is.EqualTo(view.WonCards[0]));
            Assert.That(v.CurrentTrick!.Top!.Type, Is.EqualTo(CombinationType.Pair));
            Assert.That(v.CurrentTrick.Top.Rank, Is.EqualTo(18));
            Assert.That(v.CurrentTrick.Top.Length, Is.EqualTo(2));
            Assert.That(v.CurrentTrick.Top.Cards, Is.EqualTo(top.Cards));
            Assert.That(v.CurrentTrick.History.Count, Is.EqualTo(2));
            Assert.That(v.CurrentTrick.History[1].IsPass, Is.True);
            Assert.That(v.CompletedTricks[0].DragonGiftRecipient, Is.EqualTo(0));
            Assert.That(v.CompletedTricks[0].WonByDragon, Is.True);
            Assert.That(v.TeamAScore, Is.EqualTo(120));
            Assert.That(v.Awaiting!.LegalMoves!.Count, Is.EqualTo(1));
            Assert.That(v.Awaiting.LegalMoves[0].Cards, Is.EqualTo(top.Cards));
            Assert.That(v.Awaiting.CanPass, Is.True);
            Assert.That(back.Applied!.Cards, Is.EqualTo(applied.Cards));
            Assert.That(back.Applied.RecipientSeat, Is.EqualTo(-1));
        }

        [Test]
        public void Empty_trick_view_keeps_null_top()
        {
            var view = new PlayerView { CurrentTrick = new TrickView { Top = null, TopOwnerSeat = -1 } };
            var v = RoundTrip(ServerEvent.StateUpdate(view, null)).View!;
            Assert.That(v.CurrentTrick!.Top, Is.Null);
            Assert.That(v.Awaiting, Is.Null);
        }

        [Test]
        public void Other_server_events_round_trip()
        {
            var round = RoundTrip(ServerEvent.RoundScored(new RoundResult { TeamACardPoints = 60, TeamBCardPoints = 40, TeamATichuDelta = -100, TeamATotal = -40, TeamBTotal = 40 }));
            Assert.That(round.Round!.TeamATichuDelta, Is.EqualTo(-100));
            Assert.That(round.Round.TeamBTotal, Is.EqualTo(40));

            var end = RoundTrip(ServerEvent.MatchEnded(1, 900, 1020));
            Assert.That(end.WinningTeam, Is.EqualTo(1));
            Assert.That(end.TeamB, Is.EqualTo(1020));

            var rej = RoundTrip(ServerEvent.ActionRejected(12, "카드 없음"));
            Assert.That(rej.ActionSeq, Is.EqualTo(12));
            Assert.That(rej.Reason, Is.EqualTo("카드 없음"));

            Assert.That(RoundTrip(ServerEvent.SessionFaulted("좌석 2 연결 끊김")).Reason, Is.EqualTo("좌석 2 연결 끊김"));

            var lobby = RoundTrip(ServerEvent.Lobby(2, 1, 2));
            Assert.That(lobby.Kind, Is.EqualTo(ServerEventKind.Lobby));
            Assert.That(lobby.YourSeat, Is.EqualTo(2));
            Assert.That(lobby.Connected, Is.EqualTo(1));
            Assert.That(lobby.Required, Is.EqualTo(2));
        }

        [TestCase("not json")]
        [TestCase("{}")]
        [TestCase("{\"Kind\":\"Teleport\"}")]
        [TestCase("{\"Kind\":3}")]
        [TestCase("null")]
        [TestCase("{\"Kind\":\"Play\",\"Cards\":[{\"r\":5}]}")]
        public void Malformed_client_messages_throw_protocol_exception(string json)
        {
            Assert.Throws<ProtocolException>(() => ProtocolJson.DeserializeClientAction(json));
        }

        [TestCase("{\"Kind\":\"Explode\"}")]
        [TestCase("[1,2]")]
        public void Malformed_server_messages_throw_protocol_exception(string json)
        {
            Assert.Throws<ProtocolException>(() => ProtocolJson.DeserializeServerEvent(json));
        }

        [Test]
        public void Serialized_view_on_the_wire_contains_no_other_seat_card()
        {
            // 유선 위 마스킹 불변식(기획서 7.5): 갓 나눈 라운드는 공개 카드가 0장이므로,
            // 좌석 0 에게 보내는 JSON 원문에 나타나는 카드는 좌석 0 손패뿐이어야 한다 —
            // 타 좌석 손패와 미배분 덱(큰 티츄 단계의 남은 6장씩) 모두 없어야 한다.
            // 티츄 카드는 (Rank, Suit, Special) 로 유일하므로 조각 검색으로 판정할 수 있다.
            var state = GameEngine.NewRound(2026UL);
            var mine = state.Seats[0].Hand;
            string json = ProtocolJson.Serialize(ServerEvent.StateUpdate(ViewProjector.ProjectFor(state, 0), null));

            foreach (var c in mine)
                Assert.That(json, Does.Contain(Fragment(c)), "양성 대조: 내 카드는 형식대로 실려야 한다 " + c);
            foreach (var c in Deck.CreateStandard())
                if (!mine.Contains(c))
                    Assert.That(json.Contains(Fragment(c)), Is.False, $"내 것이 아닌 카드 {c} 가 유선에 샜다");
        }
    }
}
```

(`GameEngine.NewRound`는 큰 티츄 단계에서 8장씩만 나눈다. `Deck.CreateStandard()`는 56장 전체이고 `Card.Equals`는 (Rank, Suit, Special) 비교라 `mine.Contains`가 정확하다.)

- [ ] **Step 2: 실패 확인**

Run: `dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~ProtocolJsonTests|FullyQualifiedName~SeatRotationTests" --nologo`
Expected: 빌드 실패 — `ProtocolJson`, `ServerEvent.Lobby` 없음

- [ ] **Step 3: Newtonsoft 참조 추가 (core)**

`core/src/Tichu.Net/Tichu.Net.csproj`의 `ItemGroup`에:
```xml
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
```

- [ ] **Step 4: `ServerEvent`에 `Lobby` 추가**

`core/src/Tichu.Net/Protocol/ServerEvent.cs`:
- enum 끝에 `Lobby` 추가(`SessionFaulted,` 다음 줄 `Lobby`) — 기존 값 순서는 바꾸지 않는다.
- 속성 추가(`TeamB` 아래):
  ```csharp
        /// <summary>Lobby: 이 연결에 배정된 절대 좌석. 회전하지 않는다(대기실이 회전 전에 읽는다).</summary>
        public int YourSeat { get; set; }

        /// <summary>Lobby: 접속한 인간 수 / 시작에 필요한 인간 수.</summary>
        public int Connected { get; set; }
        public int Required { get; set; }
  ```
- 팩토리 추가:
  ```csharp
        public static ServerEvent Lobby(int yourSeat, int connected, int required) =>
            new ServerEvent { Kind = ServerEventKind.Lobby, YourSeat = yourSeat, Connected = connected, Required = required };
  ```

`core/src/Tichu.Net/SeatRotation.cs`의 `ToLocal` 객체 초기화에 세 줄 추가:
```csharp
                YourSeat = ev.YourSeat,     // 회전하지 않는다 — 클래스 주석 참고
                Connected = ev.Connected,
                Required = ev.Required,
```

- [ ] **Step 5: 직렬화 구현**

`core/src/Tichu.Net/Serialization/ProtocolException.cs`:

```csharp
using System;

namespace Tichu.Net
{
    /// <summary>
    /// 프로토콜 메시지를 해석할 수 없다(깨진 JSON · 모르는 Kind · 필드 누락).
    /// 서버는 이걸 받으면 그 연결을 닫는다. Newtonsoft 예외를 감싸 호출자가 라이브러리를 모르게 한다.
    /// </summary>
    public sealed class ProtocolException : Exception
    {
        public ProtocolException(string message) : base(message) { }
        public ProtocolException(string message, Exception inner) : base(message, inner) { }
    }
}
```

`core/src/Tichu.Net/Serialization/CardJsonConverter.cs`:

```csharp
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tichu.Core.Cards;

namespace Tichu.Net
{
    /// <summary>
    /// Card(readonly 필드 struct) ↔ {"r":Rank,"s":Suit,"x":Special,"p":Points}.
    /// 네 필드를 다 싣는다 — 봉황(Rank 0, Points −25)처럼 Rank 로 다른 값을 유도할 수 없는 카드가 있다.
    /// </summary>
    public sealed class CardJsonConverter : JsonConverter<Card>
    {
        public override void WriteJson(JsonWriter writer, Card value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("r"); writer.WriteValue(value.Rank);
            writer.WritePropertyName("s"); writer.WriteValue(value.Suit.ToString());
            writer.WritePropertyName("x"); writer.WriteValue(value.Special.ToString());
            writer.WritePropertyName("p"); writer.WriteValue(value.Points);
            writer.WriteEndObject();
        }

        public override Card ReadJson(JsonReader reader, Type objectType, Card existingValue, bool hasExistingValue,
            JsonSerializer serializer)
        {
            var o = JObject.Load(reader);
            return new Card(
                Required(o, "r").Value<int>(),
                ParseEnum<Suit>(Required(o, "s")),
                ParseEnum<SpecialKind>(Required(o, "x")),
                Required(o, "p").Value<int>());
        }

        internal static JToken Required(JObject o, string key) =>
            o[key] ?? throw new ProtocolException($"필드 '{key}' 가 없습니다");

        internal static T ParseEnum<T>(JToken token) where T : struct
        {
            if (token.Type != JTokenType.String || !Enum.TryParse(token.Value<string>(), false, out T value)
                || !Enum.IsDefined(typeof(T), value))
                throw new ProtocolException($"{typeof(T).Name} 값이 올바르지 않습니다: {token}");
            return value;
        }
    }
}
```

`core/src/Tichu.Net/Serialization/CombinationJsonConverter.cs`:

```csharp
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;

namespace Tichu.Net
{
    /// <summary>Combination(생성자 전용 불변 클래스) ↔ {"t":Type,"c":[카드],"l":Length,"r":Rank,"p":PointsInPlay}.</summary>
    public sealed class CombinationJsonConverter : JsonConverter<Combination>
    {
        public override void WriteJson(JsonWriter writer, Combination? value, JsonSerializer serializer)
        {
            if (value == null) { writer.WriteNull(); return; }
            writer.WriteStartObject();
            writer.WritePropertyName("t"); writer.WriteValue(value.Type.ToString());
            writer.WritePropertyName("c"); serializer.Serialize(writer, value.Cards);
            writer.WritePropertyName("l"); writer.WriteValue(value.Length);
            writer.WritePropertyName("r"); writer.WriteValue(value.Rank);
            writer.WritePropertyName("p"); writer.WriteValue(value.PointsInPlay);
            writer.WriteEndObject();
        }

        public override Combination? ReadJson(JsonReader reader, Type objectType, Combination? existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var o = JObject.Load(reader);
            var cards = CardJsonConverter.Required(o, "c").ToObject<List<Card>>(serializer)
                        ?? throw new ProtocolException("조합 카드가 없습니다");
            return new Combination(
                CardJsonConverter.ParseEnum<CombinationType>(CardJsonConverter.Required(o, "t")),
                cards,
                CardJsonConverter.Required(o, "l").Value<int>(),
                CardJsonConverter.Required(o, "r").Value<int>(),
                CardJsonConverter.Required(o, "p").Value<int>());
        }
    }
}
```

`core/src/Tichu.Net/Serialization/ProtocolJson.cs`:

```csharp
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace Tichu.Net
{
    /// <summary>
    /// 프로토콜 메시지 ↔ JSON 텍스트. 서버(dotnet)와 유니티(IL2CPP)가 이 한 곳을 공유한다.
    /// WebSocket 텍스트 프레임 하나 = 메시지 하나이고 봉투가 없다 — 두 메시지 모두 Kind 구분자를 가진다.
    /// 열거형은 이름 문자열로 싣는다(로그 판독 + 모르는 값을 예외로 거른다).
    /// 해석 실패는 전부 ProtocolException 으로 바꾼다.
    /// </summary>
    public static class ProtocolJson
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Converters = { new StringEnumConverter(), new CardJsonConverter(), new CombinationJsonConverter() },
            NullValueHandling = NullValueHandling.Ignore,
            // 기본값(Auto)은 생성자가 만든 빈 리스트에 이어 붙인다 — 받은 값으로 갈아끼운다.
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            MissingMemberHandling = MissingMemberHandling.Ignore,
        };

        private static readonly JsonSerializer Serializer = JsonSerializer.Create(Settings);

        public static string Serialize(ServerEvent ev) => JsonConvert.SerializeObject(ev, Settings);

        public static string Serialize(ClientAction a) => JsonConvert.SerializeObject(a, Settings);

        public static ServerEvent DeserializeServerEvent(string json) => Deserialize<ServerEvent, ServerEventKind>(json);

        public static ClientAction DeserializeClientAction(string json) => Deserialize<ClientAction, ClientActionKind>(json);

        private static T Deserialize<T, TKind>(string json) where T : class where TKind : struct
        {
            try
            {
                if (!(JToken.Parse(json) is JObject o)) throw new ProtocolException("메시지는 JSON 객체여야 합니다");
                var kind = o["Kind"] ?? throw new ProtocolException("Kind 가 없습니다");
                CardJsonConverter.ParseEnum<TKind>(kind);
                return o.ToObject<T>(Serializer) ?? throw new ProtocolException("빈 메시지");
            }
            catch (ProtocolException) { throw; }
            catch (Exception e) { throw new ProtocolException(e.Message, e); }
        }
    }
}
```

- [ ] **Step 6: 통과 확인**

Run:
```bash
pwsh tools/sync-core-to-unity.ps1
dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~ProtocolJsonTests|FullyQualifiedName~SeatRotationTests" --nologo
dotnet test core/Tichu.sln --nologo
```
Expected: 전부 PASS. `Card` 비교가 `Points`를 보지 않으므로(`Card.Equals`) `Special_cards_keep_rank_suit_kind_and_points`가 Points를 따로 단언한다는 점에 유의.

- [ ] **Step 7: Unity 쪽 Newtonsoft·asmdef·link.xml**

`Packages/manifest.json`의 `"dependencies"`에 한 줄 추가(알파벳 위치):
```json
    "com.unity.nuget.newtonsoft-json": "3.2.1",
```

`Assets/_Project/Net/Tichu.Net.asmdef`에서 `"overrideReferences": false` → `true`, `"precompiledReferences": []` → `["Newtonsoft.Json.dll"]`.

`Assets/link.xml` 생성:
```xml
<!-- IL2CPP 코드 스트리핑 방지: 프로토콜 타입은 Newtonsoft 가 리플렉션으로 만든다(M2 스펙 §7). -->
<linker>
  <assembly fullname="Tichu.Net" preserve="all" />
  <assembly fullname="Tichu.Core" preserve="all" />
  <assembly fullname="Tichu.GameFlow" preserve="all" />
</linker>
```

- [ ] **Step 8: Unity 확인**

Unity 검증 절차 → `failed="0"`. 첫 실행은 패키지 해석(네트워크) 때문에 오래 걸린다. `error CS0246: ... 'Newtonsoft'`가 나오면 `Library/PackageCache/com.unity.nuget.newtonsoft-json*/Runtime/` 아래 dll 파일명을 확인해 asmdef의 `precompiledReferences` 값을 그 이름으로 맞춘다.

- [ ] **Step 9: Commit**

```bash
git add core/src/Tichu.Net core/tests/Tichu.Core.Tests Assets/_Project/Net Assets/_Project/Tests/EditMode Packages/manifest.json Packages/packages-lock.json Assets/link.xml
git status --short
git commit -m "feat(net): 프로토콜 JSON 직렬화(Newtonsoft) + Lobby 이벤트

Card·Combination 변환기, 열거형 문자열, 해석 실패는 ProtocolException.
유선 위 마스킹 불변식 테스트(갓 나눈 라운드 JSON 에 타 좌석 카드 0장).
유니티: newtonsoft-json 패키지 · asmdef 참조 · link.xml.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: 서버 프로젝트 골격 — 설정 · 좌석표 · 토큰 버킷

**Files:**
- Create: `core/src/Tichu.Server/Tichu.Server.csproj`, `ServerOptions.cs`, `SeatTable.cs`, `TokenBucket.cs`, `Program.cs`(임시 — Task 6에서 완성)
- Create: `core/tests/Tichu.Server.Tests/Tichu.Server.Tests.csproj`, `ServerOptionsTests.cs`, `SeatTableTests.cs`, `TokenBucketTests.cs`
- Modify: `core/Tichu.sln`, `.github/workflows/core-tests.yml`

**Interfaces:**
- Produces:
  ```csharp
  namespace Tichu.Server {
    public sealed class ServerOptions { int[] Seats={0,2}; Difficulty Difficulty=Hard; int Target=1000; int Port=7777;
        int AiDelayMs=900; int InterRoundPauseMs=3500; ulong? Seed; int RateCapacity=20; double RatePerSecond=10;
        public static ServerOptions Parse(string[] args); }   // 잘못된 인자 → ArgumentException
    public sealed class SeatTable { public SeatTable(IReadOnlyList<int> seats); int Required; int Connected; bool IsFull;
        public bool TryTake(out int seat); public void Release(int seat); }
    public sealed class TokenBucket { public TokenBucket(int capacity, double perSecond, Func<double> nowSeconds); public bool TryTake(); }
  }
  ```

- [ ] **Step 1: 프로젝트 생성**

`core/src/Tichu.Server/Tichu.Server.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <RootNamespace>Tichu.Server</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Tichu.Net\Tichu.Net.csproj" />
    <InternalsVisibleTo Include="Tichu.Server.Tests" />
  </ItemGroup>
</Project>
```

`core/src/Tichu.Server/Program.cs`(임시):
```csharp
using System;
using Tichu.Server;

var opt = ServerOptions.Parse(args);
Console.WriteLine($"Tichu 서버 설정: 좌석 {string.Join(",", opt.Seats)} · {opt.Difficulty} · 목표 {opt.Target} · 포트 {opt.Port}");
```

`core/tests/Tichu.Server.Tests/Tichu.Server.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <RootNamespace>Tichu.Server.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="NUnit" Version="4.2.2" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Tichu.Server\Tichu.Server.csproj" />
  </ItemGroup>

  <!-- 스크립트 클라이언트는 Tichu.Core.Tests 의 것을 그대로 컴파일한다(복제 대신 링크). -->
  <ItemGroup>
    <Compile Include="..\Tichu.Core.Tests\ScriptedClient.cs" Link="Shared\ScriptedClient.cs" />
  </ItemGroup>
</Project>
```

```bash
cd /c/Users/user/Desktop/Project/Tichu
dotnet sln core/Tichu.sln add core/src/Tichu.Server/Tichu.Server.csproj --solution-folder src
dotnet sln core/Tichu.sln add core/tests/Tichu.Server.Tests/Tichu.Server.Tests.csproj --solution-folder tests
```

`.github/workflows/core-tests.yml`의 `dotnet-version: "9.0.x"`를:
```yaml
          dotnet-version: |
            9.0.x
            10.0.x
```

- [ ] **Step 2: 실패 테스트**

`core/tests/Tichu.Server.Tests/ServerOptionsTests.cs`:

```csharp
using System;
using NUnit.Framework;
using Tichu.GameFlow.Agents;

namespace Tichu.Server.Tests
{
    public class ServerOptionsTests
    {
        [Test]
        public void Defaults_match_the_spec()
        {
            var o = ServerOptions.Parse(Array.Empty<string>());
            Assert.That(o.Seats, Is.EqualTo(new[] { 0, 2 }));
            Assert.That(o.Difficulty, Is.EqualTo(Difficulty.Hard));
            Assert.That(o.Target, Is.EqualTo(1000));
            Assert.That(o.Port, Is.EqualTo(7777));
            Assert.That(o.AiDelayMs, Is.EqualTo(900));
            Assert.That(o.InterRoundPauseMs, Is.EqualTo(3500));
            Assert.That(o.Seed, Is.Null);
            Assert.That(o.RateCapacity, Is.EqualTo(20));
            Assert.That(o.RatePerSecond, Is.EqualTo(10));
        }

        [Test]
        public void All_flags_parse()
        {
            var o = ServerOptions.Parse(new[]
            {
                "--seats", "0,1", "--difficulty", "expert", "--target", "200",
                "--port", "9000", "--ai-delay", "0", "--seed", "42"
            });
            Assert.That(o.Seats, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(o.Difficulty, Is.EqualTo(Difficulty.Expert));
            Assert.That(o.Target, Is.EqualTo(200));
            Assert.That(o.Port, Is.EqualTo(9000));
            Assert.That(o.AiDelayMs, Is.EqualTo(0));
            Assert.That(o.Seed, Is.EqualTo(42UL));
        }

        [TestCase("--seats", "0,0")]
        [TestCase("--seats", "4")]
        [TestCase("--seats", "")]
        [TestCase("--seats", "0,1,2,3,0")]
        [TestCase("--difficulty", "Godlike")]
        [TestCase("--difficulty", "7")]
        [TestCase("--target", "abc")]
        [TestCase("--bogus", "1")]
        public void Bad_arguments_throw(string key, string value)
        {
            Assert.Throws<ArgumentException>(() => ServerOptions.Parse(new[] { key, value }));
        }

        [Test]
        public void Missing_value_throws()
        {
            Assert.Throws<ArgumentException>(() => ServerOptions.Parse(new[] { "--port" }));
        }
    }
}
```

`core/tests/Tichu.Server.Tests/SeatTableTests.cs`:

```csharp
using NUnit.Framework;

namespace Tichu.Server.Tests
{
    public class SeatTableTests
    {
        [Test]
        public void Seats_are_given_in_list_order_until_full()
        {
            var t = new SeatTable(new[] { 0, 2 });
            Assert.That(t.Required, Is.EqualTo(2));

            Assert.That(t.TryTake(out int a), Is.True);
            Assert.That(a, Is.EqualTo(0));
            Assert.That(t.IsFull, Is.False);
            Assert.That(t.TryTake(out int b), Is.True);
            Assert.That(b, Is.EqualTo(2));
            Assert.That(t.IsFull, Is.True);
            Assert.That(t.Connected, Is.EqualTo(2));
            Assert.That(t.TryTake(out _), Is.False, "만석이면 거절");
        }

        [Test]
        public void Released_seat_is_reused_first()
        {
            var t = new SeatTable(new[] { 0, 2 });
            t.TryTake(out _);
            t.TryTake(out _);
            t.Release(0);

            Assert.That(t.Connected, Is.EqualTo(1));
            Assert.That(t.TryTake(out int again), Is.True);
            Assert.That(again, Is.EqualTo(0));
        }

        [Test]
        public void Releasing_a_free_seat_does_nothing()
        {
            var t = new SeatTable(new[] { 1 });
            t.Release(1);
            Assert.That(t.Connected, Is.EqualTo(0));
        }
    }
}
```

`core/tests/Tichu.Server.Tests/TokenBucketTests.cs`:

```csharp
using NUnit.Framework;

namespace Tichu.Server.Tests
{
    public class TokenBucketTests
    {
        [Test]
        public void Burst_is_allowed_then_refused()
        {
            double now = 0;
            var b = new TokenBucket(capacity: 3, perSecond: 1, () => now);
            Assert.That(b.TryTake(), Is.True);
            Assert.That(b.TryTake(), Is.True);
            Assert.That(b.TryTake(), Is.True);
            Assert.That(b.TryTake(), Is.False);
        }

        [Test]
        public void Tokens_refill_over_time_up_to_capacity()
        {
            double now = 0;
            var b = new TokenBucket(capacity: 2, perSecond: 10, () => now);
            b.TryTake(); b.TryTake();
            Assert.That(b.TryTake(), Is.False);

            now = 0.1;                            // 0.1초 × 10/초 = 1개
            Assert.That(b.TryTake(), Is.True);
            Assert.That(b.TryTake(), Is.False);

            now = 100;                            // 오래 쉬어도 용량을 넘지 않는다
            Assert.That(b.TryTake(), Is.True);
            Assert.That(b.TryTake(), Is.True);
            Assert.That(b.TryTake(), Is.False);
        }
    }
}
```

- [ ] **Step 3: 실패 확인**

Run: `dotnet test core/tests/Tichu.Server.Tests/Tichu.Server.Tests.csproj --nologo`
Expected: 빌드 실패 — `ServerOptions`/`SeatTable`/`TokenBucket` 없음

- [ ] **Step 4: 구현**

`core/src/Tichu.Server/ServerOptions.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using Tichu.GameFlow.Agents;

namespace Tichu.Server
{
    /// <summary>
    /// 서버 실행 설정. 프로세스 하나 = 매치 하나(M2 스펙 §4.1).
    /// CLI 로 받는 값: --seats --difficulty --target --port --ai-delay --seed.
    /// 나머지(라운드 사이 멈춤·레이트리밋)는 테스트가 코드로 바꾸는 값이다.
    /// </summary>
    public sealed class ServerOptions
    {
        /// <summary>인간 좌석 목록. 접속 순서대로 이 순서로 배정한다. 기본 0,2 = 친구와 같은 팀.</summary>
        public int[] Seats { get; set; } = { 0, 2 };
        public Difficulty Difficulty { get; set; } = Difficulty.Hard;
        public int Target { get; set; } = 1000;
        public int Port { get; set; } = 7777;
        /// <summary>AI 관전 지연(ms). 유니티 GameLaunchArgs.AiDelayMs 기본값과 같다.</summary>
        public int AiDelayMs { get; set; } = 900;
        /// <summary>라운드 결과를 읽을 시간(ms). 유니티 싱글플레이와 같은 3.5초.</summary>
        public int InterRoundPauseMs { get; set; } = 3500;
        /// <summary>매치 시드. null 이면 시작 시각에서 뽑는다.</summary>
        public ulong? Seed { get; set; }
        /// <summary>연결당 토큰 버킷 — 버스트 20건, 초당 10건(스펙 §6.3).</summary>
        public int RateCapacity { get; set; } = 20;
        public double RatePerSecond { get; set; } = 10;

        public static ServerOptions Parse(string[] args)
        {
            var o = new ServerOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i];
                if (i + 1 >= args.Length) throw new ArgumentException($"{key} 의 값이 없습니다");
                string value = args[++i];
                switch (key)
                {
                    case "--seats":      o.Seats = ParseSeats(value); break;
                    case "--difficulty": o.Difficulty = ParseDifficulty(value); break;
                    case "--target":     o.Target = ParseInt(key, value); break;
                    case "--port":       o.Port = ParseInt(key, value); break;
                    case "--ai-delay":   o.AiDelayMs = ParseInt(key, value); break;
                    case "--seed":
                        if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seed))
                            throw new ArgumentException($"--seed 값이 올바르지 않습니다: {value}");
                        o.Seed = seed;
                        break;
                    default: throw new ArgumentException($"알 수 없는 인자: {key}");
                }
            }
            return o;
        }

        private static int ParseInt(string key, string value) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
                ? v : throw new ArgumentException($"{key} 값이 올바르지 않습니다: {value}");

        private static Difficulty ParseDifficulty(string value)
        {
            // 숫자 문자열("7")도 Enum.TryParse 를 통과하므로 이름만 받는다.
            foreach (Difficulty d in Enum.GetValues(typeof(Difficulty)))
                if (string.Equals(d.ToString(), value, StringComparison.OrdinalIgnoreCase)) return d;
            throw new ArgumentException($"--difficulty 는 Easy/Normal/Hard/Expert 중 하나: {value}");
        }

        private static int[] ParseSeats(string value)
        {
            var seats = new List<int>();
            foreach (var part in value.Split(','))
            {
                if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int s) || s < 0 || s > 3)
                    throw new ArgumentException($"--seats 의 좌석은 0~3: {value}");
                if (seats.Contains(s)) throw new ArgumentException($"--seats 에 중복 좌석: {value}");
                seats.Add(s);
            }
            return seats.ToArray();
        }
    }
}
```

(`"".Split(',')`은 `[""]`를 내므로 빈 값도 `int.TryParse` 실패로 거절된다. 다섯 개 이상은 0~3 중 중복이 생겨 거절된다.)

`core/src/Tichu.Server/SeatTable.cs`:

```csharp
using System.Collections.Generic;

namespace Tichu.Server
{
    /// <summary>
    /// 인간 좌석 배정표. 목록 순서에서 비어 있는 첫 좌석을 준다 — 끊긴 좌석은 다음 접속자가 받는다.
    /// 스레드 안전하지 않다. 호출자(WebSocketServerTransport)가 잠금 안에서 쓴다.
    /// </summary>
    public sealed class SeatTable
    {
        private readonly int[] _order;
        private readonly HashSet<int> _taken = new HashSet<int>();

        public SeatTable(IReadOnlyList<int> seats)
        {
            _order = new int[seats.Count];
            for (int i = 0; i < seats.Count; i++) _order[i] = seats[i];
        }

        public int Required => _order.Length;
        public int Connected => _taken.Count;
        public bool IsFull => _taken.Count == _order.Length;

        public bool TryTake(out int seat)
        {
            foreach (int s in _order)
            {
                if (_taken.Contains(s)) continue;
                _taken.Add(s);
                seat = s;
                return true;
            }
            seat = -1;
            return false;
        }

        public void Release(int seat) => _taken.Remove(seat);
    }
}
```

`core/src/Tichu.Server/TokenBucket.cs`:

```csharp
using System;

namespace Tichu.Server
{
    /// <summary>
    /// 연결당 레이트리밋. 용량만큼 버스트를 허용하고 초당 perSecond 개씩 채운다.
    /// 시계를 주입받아 테스트가 시간을 조종한다. 한 연결의 수신 루프에서만 쓰므로 잠금이 없다.
    /// </summary>
    public sealed class TokenBucket
    {
        private readonly int _capacity;
        private readonly double _perSecond;
        private readonly Func<double> _now;
        private double _tokens;
        private double _last;

        public TokenBucket(int capacity, double perSecond, Func<double> nowSeconds)
        {
            _capacity = capacity;
            _perSecond = perSecond;
            _now = nowSeconds;
            _tokens = capacity;
            _last = nowSeconds();
        }

        public bool TryTake()
        {
            double t = _now();
            _tokens = Math.Min(_capacity, _tokens + (t - _last) * _perSecond);
            _last = t;
            if (_tokens < 1) return false;
            _tokens -= 1;
            return true;
        }
    }
}
```

- [ ] **Step 5: 통과 확인**

Run: `dotnet test core/tests/Tichu.Server.Tests/Tichu.Server.Tests.csproj --nologo` → PASS
그다음 `dotnet test core/Tichu.sln --nologo` → 전부 PASS (`Tichu.Core.Tests` net9 + `Tichu.Server.Tests` net10 둘 다 돈다)

- [ ] **Step 6: Commit**

```bash
git add core/src/Tichu.Server core/tests/Tichu.Server.Tests core/Tichu.sln .github/workflows/core-tests.yml
git commit -m "feat(server): Tichu.Server 골격 — 실행 인자 · 좌석표 · 토큰 버킷

net10 ASP.NET 프로젝트와 테스트 프로젝트를 솔루션에 추가하고 CI 에 10.0 SDK 를 더한다.
좌석은 목록 순서로 배정하고 끊긴 좌석을 재사용한다. 레이트리밋은 버스트 20·초당 10.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: 서버 전송 · 매치 수명 · 앱 조립

**Files:**
- Create: `core/src/Tichu.Server/WebSocketServerTransport.cs`, `MatchHost.cs`, `ServerApp.cs`
- Modify: `core/src/Tichu.Server/Program.cs`
- Test: `core/tests/Tichu.Server.Tests/TestServer.cs`, `ServerTransportTests.cs`

**Interfaces:**
- Consumes: Task 4 `ProtocolJson`/`ProtocolException`/`ServerEvent.Lobby` · Task 5 `ServerOptions`/`SeatTable`/`TokenBucket` · Task 1 `PacedPimcAgent`/`PimcBudget`/`PolicyConfig.For`
- Produces:
  ```csharp
  public sealed class WebSocketServerTransport : ITichuTransport {
      public const int MaxMessageBytes = 16 * 1024;
      public WebSocketServerTransport(SeatTable seats, Func<TokenBucket> newBucket, Action<string> log);
      public event Action? SeatsFilled;        // 마지막 좌석이 찬 순간 1회
      public event Action<int>? SeatLeft;      // 매치 시작 후 끊긴 좌석
      public void MarkMatchStarted();
      public Task HandleAsync(WebSocket ws, CancellationToken ct);
      public Task CloseAllAsync();
      internal bool Dispatch(int seat, string json);
  }
  public sealed class MatchHost { public MatchHost(ServerOptions opt, WebSocketServerTransport transport, Func<Task> shutdown, Action<string> log); }
  public static class ServerApp { public static WebApplication Build(ServerOptions opt, string url, Action<string>? log = null); }
  // tests: TestServer.StartAsync(ServerOptions) → .WsUri, .Log, .WaitForStopAsync(TimeSpan), DisposeAsync
  ```

- [ ] **Step 1: 테스트 하니스 + 실패 테스트**

`core/tests/Tichu.Server.Tests/TestServer.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Tichu.Server.Tests
{
    /// <summary>프로세스 안 실제 Kestrel. 임의 포트(0)로 띄우고 실제 주소를 읽는다.</summary>
    internal sealed class TestServer : IAsyncDisposable
    {
        public WebApplication App { get; }
        public Uri WsUri { get; }
        public List<string> Log { get; }

        private TestServer(WebApplication app, Uri wsUri, List<string> log)
        {
            App = app; WsUri = wsUri; Log = log;
        }

        public static async Task<TestServer> StartAsync(ServerOptions opt)
        {
            var log = new List<string>();
            var app = ServerApp.Build(opt, "http://127.0.0.1:0", m => { lock (log) log.Add(m); });
            await app.StartAsync();
            string addr = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.First();
            return new TestServer(app, new Uri(addr.Replace("http://", "ws://") + "/ws"), log);
        }

        /// <summary>서버가 스스로 종료를 요청했는가(매치 종료·폴트 후 StopApplication).</summary>
        public async Task<bool> WaitForStopAsync(TimeSpan timeout)
        {
            var stopping = new TaskCompletionSource();
            using var reg = App.Lifetime.ApplicationStopping.Register(() => stopping.TrySetResult());
            return await Task.WhenAny(stopping.Task, Task.Delay(timeout)) == stopping.Task;
        }

        public async ValueTask DisposeAsync()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await App.StopAsync(cts.Token); } catch (OperationCanceledException) { }
            await App.DisposeAsync();
        }
    }
}
```

`core/tests/Tichu.Server.Tests/ServerTransportTests.cs`:

```csharp
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Tichu.GameFlow.Agents;
using Tichu.Net;

namespace Tichu.Server.Tests
{
    /// <summary>
    /// 서버 전송 규칙을 원시 ClientWebSocket 으로 검증한다(클라이언트 전송 구현과 독립).
    /// 매치가 시작되지 않도록 대부분 인간 2좌석 중 하나만 접속한다.
    /// </summary>
    [Timeout(30000)]
    public class ServerTransportTests
    {
        private static ServerOptions Lobby2() => new ServerOptions
        {
            Seats = new[] { 0, 2 }, Difficulty = Difficulty.Easy, AiDelayMs = 0, InterRoundPauseMs = 0, Seed = 1
        };

        private static async Task<ClientWebSocket> Connect(Uri uri)
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

        /// <summary>다음 메시지를 받는다. 닫히면 null(그때 ws.CloseStatus 를 본다).</summary>
        private static async Task<ServerEvent?> Receive(ClientWebSocket ws)
        {
            var buf = new byte[64 * 1024];
            int total = 0;
            WebSocketReceiveResult r;
            do
            {
                r = await ws.ReceiveAsync(new ArraySegment<byte>(buf, total, buf.Length - total), CancellationToken.None);
                if (r.MessageType == WebSocketMessageType.Close) return null;
                total += r.Count;
            } while (!r.EndOfMessage);
            return ProtocolJson.DeserializeServerEvent(Encoding.UTF8.GetString(buf, 0, total));
        }

        /// <summary>Close 가 올 때까지 메시지를 버리며 읽는다.</summary>
        private static async Task<WebSocketCloseStatus?> ReadUntilClosed(ClientWebSocket ws)
        {
            while (await Receive(ws) != null) { }
            return ws.CloseStatus;
        }

        [Test]
        public async Task First_client_gets_lobby_with_its_seat()
        {
            await using var server = await TestServer.StartAsync(Lobby2());
            using var a = await Connect(server.WsUri);

            var ev = await Receive(a);

            Assert.That(ev!.Kind, Is.EqualTo(ServerEventKind.Lobby));
            Assert.That(ev.YourSeat, Is.EqualTo(0));
            Assert.That(ev.Connected, Is.EqualTo(1));
            Assert.That(ev.Required, Is.EqualTo(2));
        }

        [Test]
        public async Task Second_client_fills_the_room_and_lobby_precedes_the_first_state_update()
        {
            await using var server = await TestServer.StartAsync(Lobby2());
            using var a = await Connect(server.WsUri);
            await Receive(a);                                 // Lobby 1/2
            using var b = await Connect(server.WsUri);

            var bFirst = await Receive(b);
            Assert.That(bFirst!.Kind, Is.EqualTo(ServerEventKind.Lobby));
            Assert.That(bFirst.YourSeat, Is.EqualTo(2));
            Assert.That(bFirst.Connected, Is.EqualTo(2));

            var aSecond = await Receive(a);
            Assert.That(aSecond!.Kind, Is.EqualTo(ServerEventKind.Lobby), "마지막 Lobby 가 매치 이벤트보다 먼저 와야 한다");
            Assert.That(aSecond.Connected, Is.EqualTo(2));

            var aThird = await Receive(a);
            Assert.That(aThird!.Kind, Is.EqualTo(ServerEventKind.StateUpdate), "방이 차면 매치가 시작된다");
            Assert.That(aThird.View!.Viewer, Is.EqualTo(0));
        }

        [Test]
        public async Task Seat_freed_before_start_goes_to_the_next_client()
        {
            await using var server = await TestServer.StartAsync(Lobby2());
            var a = await Connect(server.WsUri);
            await Receive(a);
            await a.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            a.Dispose();

            // 서버가 좌석을 반납할 때까지 기다린다. 반납 전에 b 가 붙으면 좌석 2 를 받아
            // 방이 차고 매치가 시작돼 버린다(재시도로 풀 수 없는 경쟁).
            var until = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < until)
            {
                lock (server.Log) if (server.Log.Exists(m => m.Contains("좌석 0 끊김"))) break;
                await Task.Delay(10);
            }

            using var b = await Connect(server.WsUri);
            var ev = await Receive(b);

            Assert.That(ev!.YourSeat, Is.EqualTo(0), "반납된 좌석 0 을 다시 받아야 한다");
            Assert.That(ev.Connected, Is.EqualTo(1));
        }

        [Test]
        public async Task Connection_is_closed_when_room_is_full()
        {
            var opt = Lobby2();
            opt.Seats = new[] { 0 };                          // 한 명으로 즉시 만석·시작
            await using var server = await TestServer.StartAsync(opt);
            using var a = await Connect(server.WsUri);
            await Receive(a);

            using var b = await Connect(server.WsUri);
            Assert.That(await ReadUntilClosed(b), Is.EqualTo(WebSocketCloseStatus.PolicyViolation));
        }

        [Test]
        public async Task Oversized_message_closes_the_connection()
        {
            await using var server = await TestServer.StartAsync(Lobby2());
            using var a = await Connect(server.WsUri);
            await Receive(a);

            await SendText(a, new string('x', WebSocketServerTransport.MaxMessageBytes + 1));

            Assert.That(await ReadUntilClosed(a), Is.EqualTo(WebSocketCloseStatus.MessageTooBig));
        }

        [Test]
        public async Task Malformed_message_closes_the_connection()
        {
            await using var server = await TestServer.StartAsync(Lobby2());
            using var a = await Connect(server.WsUri);
            await Receive(a);

            await SendText(a, "not json");

            Assert.That(await ReadUntilClosed(a), Is.EqualTo(WebSocketCloseStatus.InvalidPayloadData));
        }

        [Test]
        public async Task Exceeding_the_rate_limit_closes_the_connection()
        {
            var opt = Lobby2();
            opt.RateCapacity = 5;
            opt.RatePerSecond = 0.001;
            await using var server = await TestServer.StartAsync(opt);
            using var a = await Connect(server.WsUri);
            await Receive(a);

            for (int i = 1; i <= 6; i++)
                await SendText(a, ProtocolJson.Serialize(ClientAction.Pass(0, i)));

            Assert.That(await ReadUntilClosed(a), Is.EqualTo(WebSocketCloseStatus.PolicyViolation));
        }

        [Test]
        public void Dispatch_overwrites_the_claimed_seat_with_the_connection_seat()
        {
            var transport = new WebSocketServerTransport(
                new SeatTable(new[] { 0, 2 }), () => new TokenBucket(20, 10, () => 0), _ => { });
            ClientAction? got = null;
            transport.OnClientAction(a => got = a);

            bool ok = transport.Dispatch(2, ProtocolJson.Serialize(ClientAction.Pass(0, 1)));   // 좌석 0 을 사칭

            Assert.That(ok, Is.True);
            Assert.That(got!.Seat, Is.EqualTo(2), "연결에 배정된 좌석으로 덮어써야 한다(스펙 §5.4)");
        }

        [Test]
        public void Dispatch_rejects_malformed_json()
        {
            var transport = new WebSocketServerTransport(
                new SeatTable(new[] { 0 }), () => new TokenBucket(20, 10, () => 0), _ => { });
            transport.OnClientAction(_ => Assert.Fail("깨진 메시지가 세션에 닿으면 안 된다"));

            Assert.That(transport.Dispatch(0, "{\"Kind\":\"Teleport\"}"), Is.False);
        }
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test core/tests/Tichu.Server.Tests/Tichu.Server.Tests.csproj --filter "FullyQualifiedName~ServerTransportTests" --nologo`
Expected: 빌드 실패 — `ServerApp`/`WebSocketServerTransport` 없음

- [ ] **Step 3: `WebSocketServerTransport` 구현**

`core/src/Tichu.Server/WebSocketServerTransport.cs`:

```csharp
using System;
using System.Collections.Generic;
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
    /// 권위 세션이 보는 WebSocket 전송(M2 스펙 §5·§6). 연결 하나 = 인간 좌석 하나.
    ///
    /// - 좌석: 접속 순서대로 SeatTable 에서 받는다. 만석·매치 진행 중이면 즉시 닫는다.
    /// - Lobby: 들어오고 나갈 때마다 전원에게 보낸다. 배정·Lobby 큐잉·만석 판정을 한 잠금 안에서
    ///   해서, 마지막 Lobby 가 세션의 첫 StateUpdate 보다 반드시 먼저 큐에 들어간다.
    /// - 송신: 세션 스레드는 SendTo 로 큐에 넣기만 한다(네트워크에서 막히지 않는다). 연결마다
    ///   송신 태스크 하나가 순서대로 보낸다 — WebSocket 은 한 소켓에 동시 전송이 금지다.
    /// - 수신: 16KB 초과·텍스트 아님·깨진 JSON·레이트리밋 초과는 연결을 닫는다.
    ///   받은 액션의 Seat 은 연결 좌석으로 덮어쓴다(사칭 방지).
    /// </summary>
    public sealed class WebSocketServerTransport : ITichuTransport
    {
        public const int MaxMessageBytes = 16 * 1024;

        private readonly SeatTable _seats;
        private readonly Func<TokenBucket> _newBucket;
        private readonly Action<string> _log;
        private readonly object _gate = new object();
        private readonly Dictionary<int, Connection> _bySeat = new Dictionary<int, Connection>();
        private Action<ClientAction>? _handler;
        private bool _matchStarted;
        private bool _filledRaised;

        /// <summary>마지막 인간 좌석이 찬 순간 한 번 발생한다. 호출 스레드 = 그 연결의 처리 스레드.</summary>
        public event Action? SeatsFilled;

        /// <summary>매치 시작 후 인간이 끊겼다. 시작 전 끊김은 좌석 반납으로 처리하고 알리지 않는다.</summary>
        public event Action<int>? SeatLeft;

        public WebSocketServerTransport(SeatTable seats, Func<TokenBucket> newBucket, Action<string> log)
        {
            _seats = seats;
            _newBucket = newBucket;
            _log = log;
        }

        /// <summary>이후의 끊김은 좌석 반납이 아니라 SeatLeft 가 된다. 새 접속은 거절된다.</summary>
        public void MarkMatchStarted()
        {
            lock (_gate) _matchStarted = true;
        }

        public void SendTo(int seat, ServerEvent ev)
        {
            Connection? c;
            lock (_gate) _bySeat.TryGetValue(seat, out c);
            c?.Enqueue(ProtocolJson.Serialize(ev));   // AI 좌석·끊긴 좌석은 연결이 없다 → 버린다
        }

        public void OnClientAction(Action<ClientAction> handler)
        {
            lock (_gate) _handler = handler;
        }

        /// <summary>연결 하나를 끝날 때까지 처리한다. ASP.NET 핸들러가 await 한다.</summary>
        public async Task HandleAsync(WebSocket ws, CancellationToken ct)
        {
            var conn = new Connection(ws);
            int seat;
            bool raiseFilled = false;

            lock (_gate)
            {
                if (_matchStarted || !_seats.TryTake(out seat))
                {
                    seat = -1;
                }
                else
                {
                    _bySeat[seat] = conn;
                    EnqueueLobbyLocked();
                    if (_seats.IsFull && !_filledRaised) { _filledRaised = true; raiseFilled = true; }
                }
            }

            if (seat < 0)
            {
                _log("만석 — 접속 거절");
                conn.Complete(WebSocketCloseStatus.PolicyViolation, "room full");
                await conn.WriterTask;
                return;
            }

            _log($"좌석 {seat} 접속 ({_seats.Connected}/{_seats.Required})");
            if (raiseFilled) SeatsFilled?.Invoke();

            try
            {
                await ReceiveLoop(conn, seat, ct);
            }
            catch (WebSocketException) { /* 갑작스러운 끊김 */ }
            catch (OperationCanceledException) { /* 서버 종료 */ }
            finally
            {
                bool started;
                lock (_gate)
                {
                    _bySeat.Remove(seat);
                    started = _matchStarted;
                    if (!started)
                    {
                        _seats.Release(seat);
                        _filledRaised = false;
                        EnqueueLobbyLocked();
                    }
                }
                conn.Complete(WebSocketCloseStatus.NormalClosure, "bye");
                _log($"좌석 {seat} 끊김");
                if (started) SeatLeft?.Invoke(seat);
            }

            await conn.WriterTask;
        }

        /// <summary>남은 메시지를 다 보낸 뒤 모든 연결을 정상 종료한다(매치 종료·폴트 후).</summary>
        public async Task CloseAllAsync()
        {
            List<Connection> all;
            lock (_gate) all = new List<Connection>(_bySeat.Values);
            foreach (var c in all) c.Complete(WebSocketCloseStatus.NormalClosure, "match over");

            var writers = new List<Task>();
            foreach (var c in all) writers.Add(c.WriterTask);
            await Task.WhenAny(Task.WhenAll(writers), Task.Delay(TimeSpan.FromSeconds(2)));
        }

        /// <summary>메시지 하나를 해석해 세션에 넘긴다. 해석 실패면 false(호출자가 연결을 닫는다).</summary>
        internal bool Dispatch(int seat, string json)
        {
            ClientAction a;
            try { a = ProtocolJson.DeserializeClientAction(json); }
            catch (ProtocolException e)
            {
                _log($"좌석 {seat} 잘못된 메시지: {e.Message}");
                return false;
            }

            a.Seat = seat;                    // 사칭 방지(스펙 §5.4) — 세션은 a.Seat 로 우편함을 고른다

            Action<ClientAction>? h;
            lock (_gate) h = _handler;
            h?.Invoke(a);                     // 매치 시작 전(핸들러 없음)에는 버린다
            return true;
        }

        private async Task ReceiveLoop(Connection conn, int seat, CancellationToken ct)
        {
            var bucket = _newBucket();
            var buf = new byte[4096];
            var message = new MemoryStream();

            while (true)
            {
                message.SetLength(0);
                WebSocketReceiveResult r;
                do
                {
                    r = await conn.Socket.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                    if (r.MessageType == WebSocketMessageType.Close) return;
                    if (r.MessageType != WebSocketMessageType.Text)
                    {
                        Abort(conn, seat, WebSocketCloseStatus.InvalidMessageType, "text only");
                        return;
                    }
                    message.Write(buf, 0, r.Count);
                    if (message.Length > MaxMessageBytes)
                    {
                        Abort(conn, seat, WebSocketCloseStatus.MessageTooBig, "message too big");
                        return;
                    }
                } while (!r.EndOfMessage);

                if (!bucket.TryTake())
                {
                    Abort(conn, seat, WebSocketCloseStatus.PolicyViolation, "rate limit");
                    return;
                }

                string json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                if (!Dispatch(seat, json))
                {
                    Abort(conn, seat, WebSocketCloseStatus.InvalidPayloadData, "bad message");
                    return;
                }
            }
        }

        private void Abort(Connection conn, int seat, WebSocketCloseStatus status, string reason)
        {
            _log($"좌석 {seat} 연결 종료: {reason}");
            conn.Complete(status, reason);
        }

        private void EnqueueLobbyLocked()
        {
            foreach (var kv in _bySeat)
                kv.Value.Enqueue(ProtocolJson.Serialize(ServerEvent.Lobby(kv.Key, _seats.Connected, _seats.Required)));
        }

        /// <summary>소켓 하나 + 송신 큐 + 송신 태스크. 송신이 끝나면 지정된 상태로 닫는다.</summary>
        private sealed class Connection
        {
            private readonly Channel<string> _out =
                Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
            private int _completed;
            private WebSocketCloseStatus _closeStatus = WebSocketCloseStatus.NormalClosure;
            private string _closeReason = "bye";

            public WebSocket Socket { get; }
            public Task WriterTask { get; }

            public Connection(WebSocket socket)
            {
                Socket = socket;
                WriterTask = Task.Run(WriteLoop);
            }

            public void Enqueue(string json) => _out.Writer.TryWrite(json);

            /// <summary>더 보낼 것이 없다. 남은 큐를 비운 뒤 status 로 닫는다. 처음 호출만 유효.</summary>
            public void Complete(WebSocketCloseStatus status, string reason)
            {
                if (Interlocked.Exchange(ref _completed, 1) == 1) return;
                _closeStatus = status;
                _closeReason = reason;
                _out.Writer.TryComplete();
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
}
```

- [ ] **Step 4: `MatchHost` · `ServerApp` · `Program` 구현**

`core/src/Tichu.Server/MatchHost.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Tichu.GameFlow.Agents;
using Tichu.Net;

namespace Tichu.Server
{
    /// <summary>
    /// 매치 하나의 수명. 방이 차면 빈자리를 PIMC 로 채워 MatchSession 을 시작하고, 세션 스레드가
    /// 끝나면(MatchEnded 또는 폴트) 연결을 정상 종료한 뒤 프로세스 종료를 요청한다.
    /// 매치 중 인간이 끊기면 남은 연결에 SessionFaulted 를 보내고 세션을 멈춘다 — MatchSession.Stop()
    /// 은 아무것도 방송하지 않으므로(취소를 조용히 삼킨다) 방송은 여기 몫이다(스펙 §6.1).
    /// </summary>
    public sealed class MatchHost
    {
        private const int SeatCount = 4;

        private readonly ServerOptions _opt;
        private readonly WebSocketServerTransport _transport;
        private readonly Func<Task> _shutdown;
        private readonly Action<string> _log;
        private MatchSession? _session;
        private int _ending;

        public MatchHost(ServerOptions opt, WebSocketServerTransport transport, Func<Task> shutdown, Action<string> log)
        {
            _opt = opt;
            _transport = transport;
            _shutdown = shutdown;
            _log = log;
            transport.SeatsFilled += StartMatch;
            transport.SeatLeft += OnSeatLeft;
        }

        private void StartMatch()
        {
            ulong seed = _opt.Seed ?? unchecked((ulong)DateTime.UtcNow.Ticks);
            var config = PolicyConfig.For(_opt.Difficulty);
            int budgetMs = PimcBudget.For(_opt.Difficulty);

            var agents = new IAgent?[SeatCount];
            for (int seat = 0; seat < SeatCount; seat++)
                if (Array.IndexOf(_opt.Seats, seat) < 0)
                    agents[seat] = new PacedPimcAgent(seed, seat, config, budgetMs, _opt.AiDelayMs);

            _transport.MarkMatchStarted();
            var session = new MatchSession(
                agents, _transport, seed,
                targetScore: _opt.Target,
                interRoundPause: TimeSpan.FromMilliseconds(_opt.InterRoundPauseMs));
            _session = session;
            session.Start();
            _log($"매치 시작 — seed {seed} · {_opt.Difficulty} · 목표 {_opt.Target}");

            Task.Factory.StartNew(async () =>
            {
                session.Join(Timeout.InfiniteTimeSpan);
                await EndAsync();
            }, TaskCreationOptions.LongRunning);
        }

        private void OnSeatLeft(int seat)
        {
            if (Volatile.Read(ref _ending) == 1) return;   // 종료 중 우리가 닫은 연결
            _log($"매치 중 좌석 {seat} 이탈 — 매치 중단");
            for (int s = 0; s < SeatCount; s++)
                _transport.SendTo(s, ServerEvent.SessionFaulted($"좌석 {seat} 연결 끊김"));
            _session?.Stop();                                // 세션 스레드가 끝나면 EndAsync 가 이어 받는다
        }

        private async Task EndAsync()
        {
            if (Interlocked.Exchange(ref _ending, 1) == 1) return;
            _log("매치 종료 — 연결을 닫고 서버를 끝낸다");
            await _transport.CloseAllAsync();
            await _shutdown();
        }
    }
}
```

`core/src/Tichu.Server/ServerApp.cs`:

```csharp
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tichu.Server
{
    /// <summary>서버 앱 조립. Program 과 테스트(TestServer)가 같은 경로를 쓴다.</summary>
    public static class ServerApp
    {
        public static WebApplication Build(ServerOptions opt, string url, Action<string>? log = null)
        {
            log ??= m => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {m}");

            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls(url);
            builder.Logging.ClearProviders();
            // 클라이언트가 닫기에 응답하지 않아도 종료가 30초씩 매달리지 않게 한다.
            builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(3));
            var app = builder.Build();

            static double NowSeconds() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            var transport = new WebSocketServerTransport(
                new SeatTable(opt.Seats),
                () => new TokenBucket(opt.RateCapacity, opt.RatePerSecond, NowSeconds),
                log);
            _ = new MatchHost(opt, transport, () => { app.Lifetime.StopApplication(); return Task.CompletedTask; }, log);

            app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
            app.Map("/ws", (RequestDelegate)(async ctx =>
            {
                if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
                using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
                await transport.HandleAsync(ws, ctx.RequestAborted);
            }));

            return app;
        }
    }
}
```

`core/src/Tichu.Server/Program.cs`(교체):

```csharp
using System;
using Tichu.Server;

ServerOptions opt;
try { opt = ServerOptions.Parse(args); }
catch (ArgumentException e)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine("사용법: --seats 0,2 --difficulty Hard --target 1000 --port 7777 [--ai-delay 900] [--seed N]");
    return 2;
}

var app = ServerApp.Build(opt, $"http://0.0.0.0:{opt.Port}");
Console.WriteLine($"Tichu 서버 — 인간 좌석 {string.Join(",", opt.Seats)} · {opt.Difficulty} · 목표 {opt.Target}");
Console.WriteLine($"접속 주소: ws://<이 PC 의 IP>:{opt.Port}/ws  (같은 Wi-Fi)");
await app.RunAsync();
return 0;
```

- [ ] **Step 5: 통과 확인**

Run: `dotnet test core/tests/Tichu.Server.Tests/Tichu.Server.Tests.csproj --nologo`
Expected: 전부 PASS. `Second_client_fills_the_room...`이 Lobby 대신 StateUpdate를 먼저 받으면 `HandleAsync`의 잠금 범위(배정·Lobby 큐잉·만석 판정이 한 블록)부터 확인한다.

- [ ] **Step 6: Commit**

```bash
git add core/src/Tichu.Server core/tests/Tichu.Server.Tests
git commit -m "feat(server): WebSocket 서버 전송 · 매치 수명 · 앱 조립

접속순 좌석 + Lobby, 좌석 덮어쓰기, 16KB·깨진 JSON·레이트리밋 시 연결 종료.
방이 차면 빈자리를 PIMC 로 채워 MatchSession 시작, 매치 중 이탈은 SessionFaulted 후 종료.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: 클라이언트 WebSocket 전송 · 주소 정규화 · E2E

**Files:**
- Create: `core/src/Tichu.Net/WebSocketClientTransport.cs`, `core/src/Tichu.Net/ServerAddress.cs`
- Test: `core/tests/Tichu.Core.Tests/ServerAddressTests.cs`, `core/tests/Tichu.Server.Tests/EndToEndTests.cs`

**Interfaces:**
- Consumes: Task 2 `IClientTransport` · Task 4 `ProtocolJson` · Task 6 `TestServer`
- Produces:
  ```csharp
  public sealed class WebSocketClientTransport : IClientTransport, IDisposable {
      public static Task<WebSocketClientTransport> ConnectAsync(Uri uri, TimeSpan timeout, CancellationToken ct); // 실패 시 예외
  }   // 끊기면 수신 큐에 SessionFaulted("서버 연결 끊김") 1회. MatchEnded·서버 SessionFaulted 뒤나 Dispose 뒤에는 만들지 않는다.
  public static class ServerAddress { public const int DefaultPort = 7777; public static bool TryParse(string? input, out Uri? uri); }
  ```

- [ ] **Step 1: 실패 테스트 — 주소**

`core/tests/Tichu.Core.Tests/ServerAddressTests.cs`:

```csharp
using System;
using NUnit.Framework;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    public class ServerAddressTests
    {
        [TestCase("192.168.0.5", "ws://192.168.0.5:7777/ws")]
        [TestCase("  192.168.0.5  ", "ws://192.168.0.5:7777/ws")]
        [TestCase("192.168.0.5:9000", "ws://192.168.0.5:9000/ws")]
        [TestCase("ws://192.168.0.5:7777/ws", "ws://192.168.0.5:7777/ws")]
        [TestCase("ws://my-pc.local", "ws://my-pc.local:7777/ws")]
        [TestCase("wss://example.com/ws", "wss://example.com:7777/ws")]
        public void Accepted_forms_normalize(string input, string expected)
        {
            Assert.That(ServerAddress.TryParse(input, out var uri), Is.True);
            Assert.That(uri!.ToString(), Is.EqualTo(expected));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("http://192.168.0.5")]
        [TestCase("a b")]
        [TestCase("ws://")]
        public void Bad_input_is_rejected(string? input)
        {
            Assert.That(ServerAddress.TryParse(input, out var uri), Is.False);
            Assert.That(uri, Is.Null);
        }
    }
}
```

(`wss://`는 기본 포트 443이 "기본값"이라 7777로 바뀐다 — 포트를 생략하면 7777이라는 규칙 하나로 통일. M2b에서 `wss` 기본 포트가 필요해지면 그때 고친다.)

- [ ] **Step 2: 실패 확인 → 구현 → 통과**

Run: `dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~ServerAddressTests" --nologo` → 빌드 실패

`core/src/Tichu.Net/ServerAddress.cs`:

```csharp
using System;

namespace Tichu.Net
{
    /// <summary>
    /// 사람이 입력한 서버 주소를 WebSocket URI 로 정규화한다. 받는 형식:
    /// "192.168.0.5" · "192.168.0.5:9000" · "ws://host:7777/ws"(앞뒤 공백 허용).
    /// 스킴 생략 → ws, 포트 생략 → 7777, 경로 생략 → /ws. http 등 다른 스킴은 거절.
    /// </summary>
    public static class ServerAddress
    {
        public const int DefaultPort = 7777;

        public static bool TryParse(string? input, out Uri? uri)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(input)) return false;

            string s = input!.Trim();
            if (s.IndexOf("://", StringComparison.Ordinal) < 0) s = "ws://" + s;
            if (!Uri.TryCreate(s, UriKind.Absolute, out var parsed)) return false;
            if (parsed.Scheme != "ws" && parsed.Scheme != "wss") return false;
            if (string.IsNullOrEmpty(parsed.Host)) return false;

            var b = new UriBuilder(parsed);
            if (parsed.IsDefaultPort) b.Port = DefaultPort;
            if (b.Path == "/" || b.Path.Length == 0) b.Path = "/ws";
            uri = b.Uri;
            return true;
        }
    }
}
```

Run: `pwsh tools/sync-core-to-unity.ps1; dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~ServerAddressTests" --nologo` → PASS

- [ ] **Step 3: 실패 테스트 — E2E**

`core/tests/Tichu.Server.Tests/EndToEndTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Tichu.Core.Tests;
using Tichu.GameFlow.Agents;
using Tichu.Net;

namespace Tichu.Server.Tests
{
    /// <summary>
    /// 실제 Kestrel + 실제 WebSocketClientTransport + 스크립트 클라이언트 2 + 서버 PIMC 2.
    /// 빠르게 돌도록 AI 는 Easy(탐색 OFF)·지연 0, 라운드 사이 멈춤 0.
    /// </summary>
    [Timeout(120000)]
    public class EndToEndTests
    {
        private static ServerOptions Fast(int target = 400) => new ServerOptions
        {
            Seats = new[] { 0, 2 }, Difficulty = Difficulty.Easy, Target = target,
            AiDelayMs = 0, InterRoundPauseMs = 0, Seed = 42
        };

        private static Task<WebSocketClientTransport> Connect(TestServer s) =>
            WebSocketClientTransport.ConnectAsync(s.WsUri, TimeSpan.FromSeconds(5), CancellationToken.None);

        [Test]
        public async Task Two_websocket_clients_and_two_server_ai_finish_a_match()
        {
            await using var server = await TestServer.StartAsync(Fast());
            using var t0 = await Connect(server);          // 먼저 접속 → 좌석 0
            using var t2 = await Connect(server);          // 다음 → 좌석 2
            var clients = new[] { new ScriptedClient(0, t0), new ScriptedClient(2, t2) };

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);
            while (DateTime.UtcNow < deadline && !(clients[0].MatchEnded && clients[1].MatchEnded))
            {
                foreach (var c in clients)
                {
                    c.Pump();
                    if (c.FaultReason != null) Assert.Fail($"좌석 {c.Seat} 폴트: {c.FaultReason}");
                }
                await Task.Delay(2);
            }

            Assert.That(clients[0].MatchEnded, Is.True, "좌석 0 이 MatchEnded 를 받아야 한다");
            Assert.That(clients[1].MatchEnded, Is.True, "좌석 2 가 MatchEnded 를 받아야 한다");
            Assert.That(await server.WaitForStopAsync(TimeSpan.FromSeconds(10)), Is.True, "매치가 끝나면 서버가 종료돼야 한다");
        }

        [Test]
        public async Task No_fault_is_raised_when_server_closes_after_match_end()
        {
            await using var server = await TestServer.StartAsync(Fast(target: 1));
            using var t0 = await Connect(server);
            using var t2 = await Connect(server);
            var clients = new[] { new ScriptedClient(0, t0), new ScriptedClient(2, t2) };

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
            while (DateTime.UtcNow < deadline && !(clients[0].MatchEnded && clients[1].MatchEnded))
            {
                foreach (var c in clients) c.Pump();
                await Task.Delay(2);
            }
            Assert.That(await server.WaitForStopAsync(TimeSpan.FromSeconds(10)), Is.True);
            await Task.Delay(500);                           // 서버 닫기 프레임이 도착할 시간
            foreach (var c in clients) c.Pump();

            Assert.That(clients[0].FaultReason, Is.Null, "정상 종료 뒤 닫기를 끊김으로 보고하면 결과 화면 대신 메뉴로 튕긴다");
            Assert.That(clients[1].FaultReason, Is.Null);
        }

        [Test]
        public async Task Human_leaving_mid_match_faults_the_other_and_stops_the_server()
        {
            var opt = Fast(target: 1000);
            opt.AiDelayMs = 50;                              // 끊기 전에 매치가 끝나지 않게
            await using var server = await TestServer.StartAsync(opt);
            using var t0 = await Connect(server);
            var t2 = await Connect(server);
            var c0 = new ScriptedClient(0, t0);

            var started = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < started) { c0.Pump(); t2.Drain(); await Task.Delay(5); if (c0.FaultReason != null) break; }
            t2.Dispose();                                    // 좌석 2 가 사라진다

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTime.UtcNow < deadline && c0.FaultReason == null) { c0.Pump(); await Task.Delay(5); }

            Assert.That(c0.FaultReason, Does.Contain("좌석 2"), "남은 사람에게 누가 끊겼는지 알려야 한다");
            Assert.That(await server.WaitForStopAsync(TimeSpan.FromSeconds(10)), Is.True);
        }

        [Test]
        public async Task Rejected_client_gets_session_faulted()
        {
            var opt = Fast();
            opt.Seats = new[] { 0 };
            await using var server = await TestServer.StartAsync(opt);
            using var t0 = await Connect(server);
            using var late = await Connect(server);          // 만석 — 서버가 바로 닫는다

            var got = new List<ServerEvent>();
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline && got.TrueForAll(e => e.Kind != ServerEventKind.SessionFaulted))
            {
                got.AddRange(late.Drain());
                await Task.Delay(10);
            }

            Assert.That(got.Exists(e => e.Kind == ServerEventKind.SessionFaulted), Is.True,
                "거절당한 클라이언트가 '기다리는 중'에 갇히지 않으려면 폴트를 받아야 한다");
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

`Human_leaving_mid_match...`의 "started" 루프는 매치가 시작돼 c0가 몇 수를 두는 시간(최대 10초, 폴트가 오면 즉시 탈출)을 준다. 좌석 2의 수신함은 비우기만 하고 응답하지 않으므로, 서버는 좌석 2의 차례에서 기다리게 된다 — 그 상태에서 끊는 것이 정확히 실기 시나리오다.

- [ ] **Step 4: 실패 확인**

Run: `dotnet test core/tests/Tichu.Server.Tests/Tichu.Server.Tests.csproj --filter "FullyQualifiedName~EndToEndTests" --nologo`
Expected: 빌드 실패 — `WebSocketClientTransport` 없음

- [ ] **Step 5: 구현**

`core/src/Tichu.Net/WebSocketClientTransport.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tichu.Net
{
    /// <summary>
    /// 실제 서버에 붙는 클라이언트 전송(M2). 수신 태스크가 이벤트를 큐에 쌓고 메인 스레드가
    /// Drain 으로 꺼낸다(루프백 폴링과 같은 모양). Submit 은 송신 큐에 넣고 즉시 돌아온다 —
    /// 송신 태스크 하나가 순서대로 보내므로 동시 전송이 생기지 않는다.
    ///
    /// 연결이 끊기면 수신 큐에 SessionFaulted("서버 연결 끊김")을 직접 넣어, 위층(TichuClient)이
    /// 서버 폴트와 똑같이 처리하게 한다. 단 MatchEnded·서버의 SessionFaulted 를 이미 받았거나
    /// 우리가 Dispose 한 뒤의 닫힘은 정상이므로 만들지 않는다.
    /// </summary>
    public sealed class WebSocketClientTransport : IClientTransport, IDisposable
    {
        private const string LostReason = "서버 연결 끊김";

        private readonly ClientWebSocket _ws;
        private readonly ConcurrentQueue<ServerEvent> _inbox = new ConcurrentQueue<ServerEvent>();
        private readonly ConcurrentQueue<string?> _outbox = new ConcurrentQueue<string?>();   // null = 닫기 응답
        private readonly SemaphoreSlim _outSignal = new SemaphoreSlim(0);
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private int _endedNormally;
        private int _disposed;

        private WebSocketClientTransport(ClientWebSocket ws)
        {
            _ws = ws;
            Task.Run(ReceiveLoop);
            Task.Run(SendLoop);
        }

        /// <summary>접속한다. 시간 초과·거절·주소 오류는 예외로 올라온다.</summary>
        public static async Task<WebSocketClientTransport> ConnectAsync(Uri uri, TimeSpan timeout, CancellationToken ct)
        {
            var ws = new ClientWebSocket();
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                linked.CancelAfter(timeout);
                try { await ws.ConnectAsync(uri, linked.Token).ConfigureAwait(false); }
                catch { ws.Dispose(); throw; }
            }
            return new WebSocketClientTransport(ws);
        }

        public IReadOnlyList<ServerEvent> Drain()
        {
            var taken = new List<ServerEvent>();
            while (_inbox.TryDequeue(out var ev)) taken.Add(ev);
            return taken;
        }

        public void Submit(ClientAction a)
        {
            _outbox.Enqueue(ProtocolJson.Serialize(a));
            _outSignal.Release();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            _cts.Cancel();
            _ws.Abort();
            _ws.Dispose();
        }

        private async Task ReceiveLoop()
        {
            var buf = new byte[8192];
            var message = new MemoryStream();
            try
            {
                while (true)
                {
                    message.SetLength(0);
                    WebSocketReceiveResult r;
                    do
                    {
                        r = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), _cts.Token).ConfigureAwait(false);
                        if (r.MessageType == WebSocketMessageType.Close)
                        {
                            RequestCloseReply();
                            ReportLostIfUnexpected();
                            return;
                        }
                        message.Write(buf, 0, r.Count);
                    } while (!r.EndOfMessage);

                    var ev = ProtocolJson.DeserializeServerEvent(
                        Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                    if (ev.Kind == ServerEventKind.MatchEnded || ev.Kind == ServerEventKind.SessionFaulted)
                        Interlocked.Exchange(ref _endedNormally, 1);
                    _inbox.Enqueue(ev);
                }
            }
            catch (Exception)
            {
                ReportLostIfUnexpected();
            }
        }

        private async Task SendLoop()
        {
            try
            {
                while (true)
                {
                    await _outSignal.WaitAsync(_cts.Token).ConfigureAwait(false);
                    if (!_outbox.TryDequeue(out var json)) continue;
                    if (json == null)
                    {
                        await _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None)
                            .ConfigureAwait(false);
                        return;
                    }
                    var bytes = Encoding.UTF8.GetBytes(json);
                    await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception) { /* 끊김은 ReceiveLoop 가 보고한다 */ }
        }

        /// <summary>서버의 닫기에 응답한다 — 송신 태스크를 거쳐 동시 전송을 피한다.</summary>
        private void RequestCloseReply()
        {
            _outbox.Enqueue(null);
            _outSignal.Release();
        }

        private void ReportLostIfUnexpected()
        {
            if (Volatile.Read(ref _disposed) == 1) return;
            if (Interlocked.Exchange(ref _endedNormally, 1) == 1) return;   // 이미 정상 종료(또는 이미 보고)
            _inbox.Enqueue(ServerEvent.SessionFaulted(LostReason));
        }
    }
}
```

- [ ] **Step 6: 통과 확인**

Run:
```bash
pwsh tools/sync-core-to-unity.ps1
dotnet test core/tests/Tichu.Server.Tests/Tichu.Server.Tests.csproj --nologo
dotnet test core/Tichu.sln --nologo
pwsh tools/sync-core-to-unity.ps1 -Check
```
Expected: 전부 PASS · `Mirror is in sync.`

- [ ] **Step 7: Unity 컴파일 확인**

Unity 검증 절차 → `failed="0"`(`WebSocketClientTransport`·`ServerAddress`가 Unity에서 컴파일되는지 확인).

- [ ] **Step 8: Commit**

```bash
git add core/src/Tichu.Net core/tests Assets/_Project/Net Assets/_Project/Tests/EditMode
git commit -m "feat(net): WebSocket 클라이언트 전송 · 서버 주소 정규화 · E2E

수신 태스크→큐→Drain, 송신 큐 직렬화, 끊김은 SessionFaulted 로 표면화(정상 종료 뒤엔 안 함).
E2E: 실제 Kestrel 위에서 스크립트 2 + PIMC 2 완주, 이탈 시 폴트·서버 종료, 만석 거절.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Unity 네트워크 매치 경로 (대기실 · 알림)

**Files:**
- Create: `Assets/_Project/Presentation/Net/LobbyWaiter.cs`
- Modify: `Assets/_Project/Presentation/Shell/GameLaunchArgs.cs`, `MatchSettings.cs`, `GameSessionPresenter.cs`, `MenuShellPresenter.cs`
- Modify: `Assets/_Project/Presentation/RoundBootstrap.cs`
- Test: `Assets/_Project/Presentation/Tests/LobbyWaiterTests.cs`

**Interfaces:**
- Consumes: Task 2 `TichuClient(..., backlog)` · Task 3 `TableViewModel(0)` 규약 · Task 7 `WebSocketClientTransport.ConnectAsync`
- Produces:
  ```csharp
  public sealed class LobbyWaiter { int Seat=-1; int Connected; int Required; bool Started; string FaultReason;
      List<ServerEvent> Backlog; string StatusText; void Feed(IReadOnlyList<ServerEvent> events); }
  GameLaunchArgs.ServerAddress : System.Uri (null = AI 대전)
  MatchSettings.ServerAddress : System.Uri; MatchSettings.PendingNotice : string
  RoundBootstrap.Begin(GameLaunchArgs args, Action<MatchSummary> onComplete, Action<string> onFault)  // reason null = 알림 없음(사용자 취소)
  ```

- [ ] **Step 1: 실패 테스트**

`Assets/_Project/Presentation/Tests/LobbyWaiterTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Net;
using Tichu.Presentation.Net;

namespace Tichu.Presentation.Tests
{
    public class LobbyWaiterTests
    {
        [Test]
        public void Lobby_events_update_seat_and_counts()
        {
            var w = new LobbyWaiter();
            Assert.That(w.StatusText, Is.EqualTo("서버 접속 중…"));

            w.Feed(new[] { ServerEvent.Lobby(2, 1, 2) });

            Assert.That(w.Seat, Is.EqualTo(2));
            Assert.That(w.Started, Is.False);
            Assert.That(w.StatusText, Is.EqualTo("다른 플레이어 기다리는 중 1/2"));
        }

        [Test]
        public void Events_after_start_go_to_backlog_in_order()
        {
            var w = new LobbyWaiter();
            var first = ServerEvent.StateUpdate(new PlayerView { Viewer = 2 }, null);
            var second = ServerEvent.ActionRejected(3, "x");

            // 마지막 Lobby 와 매치 이벤트가 한 Drain 묶음에 섞여 와도 하나도 잃지 않는다.
            w.Feed(new List<ServerEvent> { ServerEvent.Lobby(2, 1, 2), ServerEvent.Lobby(2, 2, 2), first, second });

            Assert.That(w.Started, Is.True);
            Assert.That(w.Seat, Is.EqualTo(2));
            Assert.That(w.Backlog, Is.EqualTo(new[] { first, second }));
        }

        [Test]
        public void Fault_before_start_is_reported()
        {
            var w = new LobbyWaiter();
            w.Feed(new[] { ServerEvent.SessionFaulted("서버 연결 끊김") });

            Assert.That(w.FaultReason, Is.EqualTo("서버 연결 끊김"));
            Assert.That(w.Started, Is.False);
        }
    }
}
```

- [ ] **Step 2: 실패 확인 → 구현**

Unity 검증 절차 → 컴파일 실패(`LobbyWaiter` 없음)를 `r.log`에서 확인.

`Assets/_Project/Presentation/Net/LobbyWaiter.cs`:

```csharp
using System.Collections.Generic;
using Tichu.Net;

namespace Tichu.Presentation.Net
{
    /// <summary>
    /// 네트워크 매치의 대기실. 매치가 시작되기 전 이벤트(Lobby·폴트)를 읽어 내 좌석과 인원을
    /// 알아내고, 첫 매치 이벤트부터는 순서대로 Backlog 에 담아 TichuClient 에 넘긴다 —
    /// 마지막 Lobby 와 첫 StateUpdate 가 한 Drain 묶음에 섞여 와도 잃지 않기 위해서다.
    /// 유니티 타입 무참조(EditMode 단위 테스트).
    /// </summary>
    public sealed class LobbyWaiter
    {
        public int Seat { get; private set; } = -1;
        public int Connected { get; private set; }
        public int Required { get; private set; }
        public bool Started { get; private set; }
        public string FaultReason { get; private set; }
        public List<ServerEvent> Backlog { get; } = new List<ServerEvent>();

        public string StatusText => Required > 0
            ? $"다른 플레이어 기다리는 중 {Connected}/{Required}"
            : "서버 접속 중…";

        public void Feed(IReadOnlyList<ServerEvent> events)
        {
            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                if (Started) { Backlog.Add(ev); continue; }

                switch (ev.Kind)
                {
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

Unity 검증 절차 → `LobbyWaiterTests` 3/3 포함 `failed="0"`.

- [ ] **Step 3: 셸 데이터 — 주소·알림**

`GameLaunchArgs.cs`의 `Difficulty` 필드 아래에:
```csharp
        /// <summary>네트워크 매치 서버 주소. null 이면 로컬 AI 대전(M2).</summary>
        public System.Uri ServerAddress;
```

`MatchSettings.cs`의 `Difficulty` 아래에:
```csharp
        /// <summary>친구방(개발용 NetJoin)에서 고른 서버. AI 대전을 시작하면 null 로 되돌린다.</summary>
        public System.Uri ServerAddress;

        /// <summary>메인 허브로 돌아갔을 때 한 번 띄울 알림(접속 실패·연결 끊김). 띄운 뒤 null.</summary>
        public string PendingNotice;
```

- [ ] **Step 4: `GameSessionPresenter` — 주소 운반 · 폴트 알림**

`LoadTableAsync`의 `rb.Begin(...)` 두 줄을 다음으로:
```csharp
            rb.Begin(new GameLaunchArgs { Difficulty = _settings.Difficulty, ServerAddress = _settings.ServerAddress },
                OnMatchEnded,
                reason => { _settings.PendingNotice = reason; _flow.Send(AppFlowEvent.ReturnToHub); });   // 폴트·접속 실패 → 메뉴 + 알림
```

- [ ] **Step 5: `MenuShellPresenter` — 알림 표시 · AI 대전 시 주소 초기화**

`StartAt(Difficulty d)`의 첫 줄 앞에 `_settings.ServerAddress = null;   // AI 대전은 로컬 세션` 추가.

`Show(ScreenState s)`의 `ToggleBgm(s);` 앞에:
```csharp
            if (s == ScreenState.MainHub && _settings.PendingNotice != null)
            {
                ShowToast(_settings.PendingNotice);
                _settings.PendingNotice = null;
            }
```

- [ ] **Step 6: `RoundBootstrap` — 네트워크 분기**

`Assets/_Project/Presentation/RoundBootstrap.cs`:
- 필드 `private Action _onFault;` → `private Action<string> _onFault;`
- `Begin` 시그니처 `Action onFault` → `Action<string> onFault`, 요약 주석의 `onFault` 설명을 "세션 폴트·접속 실패 시 사유와 함께(null = 사용자 취소, 알림 없음)"로.
- `Begin` 끝의 `RunMatchAsync(vm).Forget();`를:
  ```csharp
            if (args.ServerAddress != null) RunNetworkMatchAsync(vm, canvas).Forget();
            else RunMatchAsync(vm).Forget();
  ```
- `RunMatchAsync`의 `try` 블록 안 루프와 결과 처리(현재 `while (...) { client.Pump(); ... }` 부터 `else if (summary.HasValue) _onComplete?.Invoke(summary.Value);` 까지)를 `await PumpUntilEndAsync(client, () => summary, ct);` 한 줄로 바꾸고, 공용 헬퍼를 추가:
  ```csharp
        /// <summary>매치가 끝나거나 세션이 폴트날 때까지 펌프하고 결과를 셸에 알린다.</summary>
        private async UniTask PumpUntilEndAsync(TichuClient client, Func<MatchSummary?> summary, System.Threading.CancellationToken ct)
        {
            while (!summary().HasValue && !client.Faulted && !ct.IsCancellationRequested)
            {
                client.Pump();
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            if (client.Faulted)
            {
                Debug.LogError($"[RoundBootstrap] 세션 폴트로 매치를 중단한다: {client.LastRejection}");
                _onFault?.Invoke(client.LastRejection);
            }
            else if (summary().HasValue)
                _onComplete?.Invoke(summary().Value);
        }
  ```
- 네트워크 경로 추가:
  ```csharp
        /// <summary>
        /// 서버에 접속해 대기실에서 좌석을 받은 뒤 같은 펌프로 매치를 돈다. 로컬 세션·AI 는 없다 —
        /// 권위와 AI 페이싱은 서버가 쥔다(빨리감기 무효는 의도된 동작).
        /// </summary>
        private async UniTaskVoid RunNetworkMatchAsync(TableViewModel vm, Canvas canvas)
        {
            var ct = this.GetCancellationTokenOnDestroy();
            var overlay = CreateLobbyOverlay(canvas, out var status, out var cancel);
            cancel.onClick.AddListener(() => _onFault?.Invoke(null));   // 사용자 취소 → 알림 없이 메뉴로

            WebSocketClientTransport transport;
            try
            {
                transport = await WebSocketClientTransport.ConnectAsync(_args.ServerAddress, TimeSpan.FromSeconds(5), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                Debug.LogWarning($"[RoundBootstrap] 접속 실패 {_args.ServerAddress}: {e.Message}");
                _onFault?.Invoke("서버에 연결할 수 없습니다");
                return;
            }

            using (transport)
            {
                try
                {
                    var lobby = new LobbyWaiter();
                    while (!lobby.Started && lobby.FaultReason == null)
                    {
                        lobby.Feed(transport.Drain());
                        status.text = lobby.StatusText;
                        await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    }
                    if (lobby.FaultReason != null) { _onFault?.Invoke(lobby.FaultReason); return; }

                    Destroy(overlay);
                    MatchSummary? summary = null;
                    using var client = new TichuClient(transport, vm, lobby.Seat, s => summary = s, lobby.Backlog);
                    await PumpUntilEndAsync(client, () => summary, ct);
                }
                catch (OperationCanceledException) { /* 씬 종료: 정상 */ }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        /// <summary>대기실 오버레이(상태 문구 + 취소). 테이블 위 반투명 판.</summary>
        private static GameObject CreateLobbyOverlay(Canvas canvas, out Text status, out Button cancel)
        {
            var root = new GameObject("LobbyOverlay", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(canvas.transform, false);
            var rt = (RectTransform)root.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            root.GetComponent<Image>().color = new Color(0.04f, 0.10f, 0.06f, 0.92f);

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var textGo = new GameObject("Status", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(root.transform, false);
            status = textGo.GetComponent<Text>();
            status.font = font; status.fontSize = 48; status.alignment = TextAnchor.MiddleCenter;
            status.color = new Color(0.92f, 0.94f, 0.98f); status.text = "서버 접속 중…";
            var trt = status.rectTransform;
            trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(1200f, 120f); trt.anchoredPosition = new Vector2(0f, 60f);

            var btnGo = new GameObject("Btn_취소", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(root.transform, false);
            var brt = (RectTransform)btnGo.transform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(360f, 96f); brt.anchoredPosition = new Vector2(0f, -100f);
            btnGo.GetComponent<Image>().color = new Color(0.20f, 0.24f, 0.34f, 1f);
            cancel = btnGo.GetComponent<Button>();

            var lblGo = new GameObject("L", typeof(RectTransform), typeof(Text));
            lblGo.transform.SetParent(btnGo.transform, false);
            var lbl = lblGo.GetComponent<Text>();
            lbl.font = font; lbl.fontSize = 36; lbl.alignment = TextAnchor.MiddleCenter;
            lbl.color = new Color(0.92f, 0.94f, 0.98f); lbl.text = "취소";
            var lrt = lbl.rectTransform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;

            return root;
        }
  ```
- `RunMatchAsync` 안의 `catch`·`finally`(세션 Stop/Join)는 그대로 둔다.
- `Begin` 안의 `var canvas = CreateCanvas();`는 이미 지역 변수이므로 그대로 넘긴다.

- [ ] **Step 7: Unity 확인**

Unity 검증 절차 → `failed="0"`. 이 단계에는 화면 진입점(`NetJoin`)이 아직 없으므로 네트워크 경로는 Task 9 이후 실기 검증에서 실제로 탄다. 컴파일과 기존 회귀만 확인한다.

- [ ] **Step 8: Commit**

```bash
git add Assets/_Project/Presentation
git commit -m "feat(presentation): 네트워크 매치 경로 — 접속 · 대기실 · 폴트 알림

ServerAddress 가 있으면 로컬 세션 대신 WebSocket 으로 접속하고 대기실(LobbyWaiter)에서
좌석을 받아 같은 펌프로 돈다. 취소 버튼, 접속 실패·끊김은 메인 허브 토스트로 알린다.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: 개발용 진입 화면 `NetJoin`

**Files:**
- Modify: `Assets/_Project/Presentation/Shell/AppFlowState.cs`, `AppFlowEvent.cs`, `AppFlowReducer.cs`
- Modify: `Assets/_Project/Presentation/Shell/Views/MenuShellView.cs`
- Modify: `Assets/_Project/Presentation/Shell/MenuShellPresenter.cs`
- Test: `Assets/_Project/Presentation/Tests/AppFlowReducerTests.cs`

**Interfaces:**
- Produces: `ScreenState.NetJoin` · `AppFlowEvent.OpenNetJoin` · `AppFlowEvent.StartNetMatch` · `MenuShellView.AddInputField(ScreenState panel, string initial, string placeholder) → InputField`

- [ ] **Step 1: 실패 테스트**

`AppFlowReducerTests.Valid_transitions`의 `[TestCase]` 목록 끝에 추가:
```csharp
        [TestCase(ScreenState.ModeSelect, AppFlowEvent.OpenNetJoin,    ScreenState.NetJoin)]
        [TestCase(ScreenState.NetJoin,    AppFlowEvent.StartNetMatch,  ScreenState.InGame)]
        [TestCase(ScreenState.NetJoin,    AppFlowEvent.Back,           ScreenState.ModeSelect)]
```
`Inapplicable_events_keep_current_state` 목록 끝에 추가:
```csharp
        [TestCase(ScreenState.MainHub, AppFlowEvent.StartNetMatch)]
        [TestCase(ScreenState.NetJoin, AppFlowEvent.StartAiMatch)]
```

Unity 검증 절차 → 컴파일 실패(`NetJoin` 없음) 확인.

- [ ] **Step 2: 상태·이벤트·전이**

`AppFlowState.cs` enum의 `Settings,` 다음 줄에 `NetJoin,`.
`AppFlowEvent.cs` enum의 `ReturnToHub,` 다음 줄에 `OpenNetJoin,` 과 `StartNetMatch,`.
`AppFlowReducer.cs`의 `(ScreenState.ModeSelect, AppFlowEvent.Back) => ScreenState.MainHub,` 줄 아래에:
```csharp
            (ScreenState.ModeSelect,       AppFlowEvent.OpenNetJoin)          => ScreenState.NetJoin,   // M2 개발용 친구방
            (ScreenState.NetJoin,          AppFlowEvent.StartNetMatch)        => ScreenState.InGame,
            (ScreenState.NetJoin,          AppFlowEvent.Back)                 => ScreenState.ModeSelect,
```

- [ ] **Step 3: `MenuShellView` — 패널·입력칸**

`MenuStates` 배열 끝에 `ScreenState.NetJoin` 추가.
`PanelColor` switch에 `ScreenState.NetJoin => new Color(0.08f, 0.12f, 0.18f, 1f),` 추가.
`PanelTitle` switch에 `ScreenState.NetJoin => "친구방 (개발용)",` 추가.
`AddSlider` 아래에 메서드 추가:

```csharp
        /// <summary>패널에 한 줄 입력칸을 추가한다(코드 빌드, 레거시 InputField — 안드로이드 소프트 키보드 지원).</summary>
        public InputField AddInputField(ScreenState panel, string initial, string placeholder)
        {
            var go = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(_buttonRoots[panel], false);
            go.GetComponent<Image>().color = new Color(0.92f, 0.94f, 0.98f, 1f);
            var le = go.AddComponent<LayoutElement>(); le.preferredWidth = 620; le.preferredHeight = 90;

            var text = NewText("Text", go.transform, "", 34);
            text.color = new Color(0.08f, 0.10f, 0.14f);
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            Inset(text.rectTransform);

            var ph = NewText("Placeholder", go.transform, placeholder, 30);
            ph.color = new Color(0.45f, 0.48f, 0.55f);
            ph.alignment = TextAnchor.MiddleLeft;
            ph.fontStyle = FontStyle.Italic;
            Inset(ph.rectTransform);

            var input = go.GetComponent<InputField>();
            input.textComponent = text;
            input.placeholder = ph;
            input.lineType = InputField.LineType.SingleLine;
            input.text = initial;
            return input;
        }

        static void Inset(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(20f, 6f); rt.offsetMax = new Vector2(-20f, -6f);
        }
```

- [ ] **Step 4: `MenuShellPresenter` — 배선**

- `using Tichu.Net;` 추가, 필드 `InputField _addressInput;`와 상수 `const string AddressKey = "net.serverAddress";` 추가(`using UnityEngine.UI;`도 추가).
- `WireButtons()`의 `"친구방"` 줄을 `Wire(ScreenState.ModeSelect, "친구방", OnFriendRoom);`으로 바꾸고, `HowTo` 줄 앞에:
  ```csharp
            _addressInput = _view.AddInputField(ScreenState.NetJoin, PlayerPrefs.GetString(AddressKey, ""), "서버 주소 (예: 192.168.0.5)");
            Wire(ScreenState.NetJoin, "접속", ConnectToServer);
            Wire(ScreenState.NetJoin, "뒤로", () => _flow.Send(AppFlowEvent.Back));
  ```
- 메서드 추가(`StartAt` 아래):
  ```csharp
        // 친구방: 개발 빌드(에디터 포함)에서만 M2 접속 화면을 연다. 릴리스는 기존 스텁 안내 유지.
        void OnFriendRoom()
        {
            if (!Debug.isDebugBuild)
            {
                _flow.Send(AppFlowEvent.SelectFriendRoomStub);
                ShowToast("친구방은 Phase 3에서 제공됩니다");
                return;
            }
            _flow.Send(AppFlowEvent.OpenNetJoin);
        }

        // 주소를 정규화해 기록하고 네트워크 매치를 시작한다(→ InGame). 형식 오류면 접속을 시도하지 않는다.
        void ConnectToServer()
        {
            if (!ServerAddress.TryParse(_addressInput.text, out var uri))
            {
                ShowToast("서버 주소 형식이 올바르지 않습니다");
                return;
            }
            PlayerPrefs.SetString(AddressKey, _addressInput.text.Trim());
            PlayerPrefs.Save();
            _settings.ServerAddress = uri;
            _flow.Send(AppFlowEvent.StartNetMatch);
        }
  ```

- [ ] **Step 5: Unity 확인**

Unity 검증 절차 → 리듀서 신규 5케이스 포함 `failed="0"`. `MenuBgm.PlaysIn`은 `s != InGame`이라 `NetJoin`에서도 BGM이 이어진다(의도).

- [ ] **Step 6: Commit**

```bash
git add Assets/_Project/Presentation
git commit -m "feat(shell): 개발용 친구방 접속 화면 NetJoin

개발 빌드·에디터에서 친구방 → 서버 주소 입력(PlayerPrefs 기억) → 접속.
릴리스 빌드는 기존 'Phase 3에서 제공' 안내 유지. 리듀서 전이 3개.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: 실기 검증 (DoD 2~5) · 실행 안내

**Files:**
- Modify: `README.md` (서버 실행 절 추가 — gh-pages 경고 문단은 건드리지 않는다)

이 태스크는 사람이 기기로 판정한다. 에이전트는 준비·안내·결과 기록을 맡는다.

- [ ] **Step 1: README에 실행 방법 추가**

`README.md`의 적절한 위치(빌드/테스트 절 근처)에:

````markdown
## 친구방 개발 서버 (M2 · 같은 Wi-Fi)

```bash
dotnet run --project core/src/Tichu.Server -- --seats 0,2 --difficulty Hard --target 1000
```

- 콘솔에 나온 대로 클라이언트에서 `ws://<이 PC 의 IP>:7777/ws`(또는 IP만)로 접속한다. 친구방 버튼은 개발 빌드·에디터에서만 접속 화면을 연다.
- `--seats` = 인간 좌석(접속 순서대로 배정, `0,2` = 같은 팀). 빈자리는 서버 PIMC가 채운다.
- 프로세스 하나 = 매치 하나. 매치가 끝나거나 누가 끊기면 서버가 종료된다.
- 처음 실행할 때 Windows 방화벽이 묻는다 → **개인 네트워크 허용**.
````

- [ ] **Step 2: 전체 자동 검증 (DoD 1)**

```bash
dotnet test core/Tichu.sln --nologo
pwsh tools/sync-core-to-unity.ps1 -Check
```
Unity 검증 절차도 한 번 더. 세 결과의 숫자를 기록한다.

- [ ] **Step 3: S23 개발 빌드**

Unity 에디터에서 Android 개발 빌드(Development Build 체크, IL2CPP — 현 프로젝트 설정)를 `Build/TichuMaster-dev-arm64.apk`로 만들어 S23에 설치한다(`adb install -r`). 빌드는 사람이 에디터에서 하거나, 배치 빌드 메서드가 있으면 그걸 쓴다.

- [ ] **Step 4: 사람 실기 체크리스트 — 사용자에게 HTML로 안내**

레포 루트에 `티츄_M2_실기검증.html`(한국어, 체크리스트)을 만들어 브라우저로 연다. 항목:
1. PC IP 확인(`ipconfig` → 무선 LAN IPv4) · 서버 실행: `dotnet run --project core/src/Tichu.Server -- --seats 0,2 --target 200` · 방화벽 허용
2. 에디터 Play → 게임 시작 → 모드 선택 → 친구방 → 주소 입력 → 접속 → "기다리는 중 1/2"
3. S23 → 같은 경로로 접속 → 두 화면 모두 테이블 표시. **S23(좌석 2) 화면에서 자기 손패가 아래, 파트너 = 에디터 플레이어 자리가 위**인지
4. 매치를 끝까지 둔다 → **둘 다 결과 화면**, 점수가 양쪽에서 같고 "우리 팀" 표기가 둘 다 맞는지(같은 팀이므로 같은 승패) **(DoD 2)**
5. 서버를 다시 띄우고 재접속 → 몇 수 진행 중 S23 앱 강제 종료 → **에디터가 메뉴로 돌아가고 "좌석 2 연결 끊김" 토스트** **(DoD 3)**
6. 2~4 동안 S23에서 직렬화 오류 없음(카드·조합 정상 표시, 수를 내면 받아들여짐) **(DoD 4 — IL2CPP)**
7. 서버 없이 AI 대전 한 판 스모크 — 이전과 체감 차이 없음 **(DoD 5)**
8. 접속 화면에서 틀린 주소(`http://x`) → "형식이 올바르지 않습니다", 꺼진 서버 주소 → 5초 안에 "서버에 연결할 수 없습니다" 후 메뉴, 대기 화면 "취소" → 메뉴

- [ ] **Step 5: 결과 기록 · Commit**

사용자 판정을 받아 실패 항목은 원인 진단 → 수정(TDD) → 재검증. 전부 통과하면:

```bash
git add README.md 티츄_M2_실기검증.html
git commit -m "docs(mp): M2 서버 실행 안내 + 실기 검증 체크리스트

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

그다음 superpowers:finishing-a-development-branch로 머지 여부를 사용자와 정한다.
