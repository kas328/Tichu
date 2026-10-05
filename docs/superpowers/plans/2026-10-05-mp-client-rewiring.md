# 멀티플레이 클라이언트 재배선 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 싱글플레이 UI 를 `MatchSession` 권위 세션 + `PlayerView` 프로토콜 위로 옮겨, 테이블 뷰가 `GameState` 를 직접 읽지 않고 좌석 0 시야만 먹게 한다.

**Architecture:** `RoundBootstrap` 이 `LoopbackTransport` + `MatchSession`(좌석 0 = `RemoteAgent`, 1~3 = `PacedPimcAgent`)을 조립한다. 세션은 전용 스레드에서 기존 동기 `GameDriver` 를 돌리고 좌석별 마스킹 뷰를 방송한다. `TichuClient` 가 매 프레임 수신함을 비워 `TableViewModel` 에 밀어넣고, 뷰모델이 내보낸 `ClientAction` 을 전송으로 올린다. 즉 **단일 플레이가 멀티 경로의 1인 인스턴스**가 되고, M2 는 `LoopbackTransport` 자리에 WebSocket 을 끼우는 일만 남는다.

**Tech Stack:** C# 9 / netstandard2.1 (`core/`) · Unity 6000.3.17f1 + UniTask + R3 (Presentation) · NUnit (dotnet 4.2.2 / Unity `com.unity.ext.nunit` 3.5 계열)

**Spec:** `docs/superpowers/specs/2026-10-05-mp-client-rewiring-design.md`

## Global Constraints

- `core/` 는 유니티 무의존 순수 C# 이다: netstandard2.1, C# 9, `Nullable=enable`, `ImplicitUsings=disable`, UnityEngine 참조 0. `core/src/Tichu.Net/` 에 유니티 타입을 들이지 않는다.
- `core/src/**` 와 `core/tests/**` 의 모든 변경은 `Assets/_Project/` 로 **바이트 동일** 미러한다. 변경 후 `pwsh tools/sync-core-to-unity.ps1` 로 복사하고 `pwsh tools/sync-core-to-unity.ps1 -Check` 가 `Mirror is in sync.` 를 출력하는지 확인한다.
- 새 `.cs` 파일은 미러 쪽(`Assets/_Project/Net/`, `Assets/_Project/Tests/EditMode/`)도 **함께 커밋**한다. 유니티가 생성한 `.meta` 파일도 커밋한다.
- **새 테스트는 NUnit 3.5 계열에서도 컴파일되는 단언만 쓴다.** `Does.Not.Contain(object)` 같은 NUnit 4 전용 오버로드 금지 — dotnet 이 그린이어도 유니티 EditMode 어셈블리 전체가 `CS1503` 로 깨지고, 그건 dotnet 으로는 원리적으로 못 잡는다. 부정 포함은 `Assert.That(list.Contains(x), Is.False)` 로 쓴다.
- ⚠️ **유니티 EditMode 에서 `Tichu.Core.Tests` 어셈블리 전체를 실행하지 않는다.** `Sim`/`Simulator` 계열이 10만 판을 돌려 메인스레드를 점유하고 MCP 브리지가 멈춘다. 유니티에서는 항상 클래스 필터(완전 수식명)로 돌린다. dotnet 쪽 전체 실행은 안전하다.
- 오라클 비침범: `AsyncGameDriver`·`IDecisionAgent`·`IHumanInputPort`·`HumanAgent`·`PimcDecisionAgent`·`DecisionRequest` 는 **삭제하지 않는다**(스펙 §9). 사용 중단 주석만 단다. 동기↔비동기 드라이버 동일성 테스트가 거기에 걸려 있다.
- 기존 스타일 유지: 한국어 XML 주석, `private readonly`, 파일당 한 책임, 기존 들여쓰기(4칸).
- 브랜치 `feat/mp-client-rewiring`. 작업 단위로 자주 커밋한다.

---

## 파일 구조

| 파일 | 책임 | 작업 |
|---|---|---|
| `core/src/Tichu.Net/AppliedAction.cs` | StateUpdate 를 유발한 액션의 공개 요약(교환 페이로드 제외) | Task 1 (신규) |
| `core/src/Tichu.Net/Protocol/ServerEvent.cs` | `StateUpdate` 에 `Applied` 동승 | Task 1 (수정) |
| `core/src/Tichu.Net/MatchSession.cs` | `OnApply` 가 액션을 실어 방송 + 라운드 간 멈춤 | Task 1 (수정) |
| `core/src/Tichu.Net/PlayerView.cs` | `TrickView.Top` 을 `Combination?` 하나로 | Task 2 (수정) |
| `core/src/Tichu.Net/ViewProjector.cs` | 위 투영 | Task 2 (수정) |
| `Assets/_Project/Presentation/Agents/PacedPimcAgent.cs` | 동기 `IAgent` + 관전 지연(겹침 보존) | Task 3 (신규) |
| `Assets/_Project/Presentation/Net/TichuClient.cs` | 수신함 폴링 → 뷰모델, 뷰모델 → 전송 | Task 4 (신규) |
| `Assets/_Project/Presentation/ViewModel/TableViewModel.cs` | `PlayerView` 투영 + `ClientAction` 발행 | Task 4 (전환) |
| `Assets/_Project/Presentation/Views/RuntimeTableView.cs` | 타입 교체(`TrickView`/`Awaiting`/`AppliedAction`) | Task 4 (수정) |
| `Assets/_Project/Presentation/RoundBootstrap.cs` | 세션 조립 + 프레임 펌프 | Task 4 (수정) |
| `Assets/_Project/Presentation/Audio/SfxMap.cs` | `AppliedAction` 오버로드 | Task 4 (수정) |

### 왜 Task 4 가 하나의 큰 태스크인가

Presentation 어셈블리는 한 덩어리로 컴파일된다. `TableViewModel` 의 `CurrentTrick`·`Played` 는 **타입이 바뀌므로**(이름 충돌 없이 추가할 수 없다) 뷰모델만 바꾸면 `RuntimeTableView` 가 깨지고, 뷰까지 바꾸면 `RoundBootstrap` 이 깨진다. 임시 이름을 두고 나중에 개명하는 쪽은 리뷰 대상에 순수 churn 을 더한다. 절반만 재배선된 UI 는 리뷰가 의미 있게 승인/거부할 수 없는 상태이므로, **"UI 가 프로토콜을 먹는다"를 한 단위로** 묶는다. 태스크를 더 쪼개려 하지 말 것 — 빨간 어셈블리가 나온다.

---

### Task 1: 프로토콜 보강 — `AppliedAction` + 라운드 간 멈춤

**Files:**
- Create: `core/src/Tichu.Net/AppliedAction.cs` → 미러 `Assets/_Project/Net/AppliedAction.cs`
- Modify: `core/src/Tichu.Net/Protocol/ServerEvent.cs` (`StateUpdate` 팩토리 + `Applied` 속성)
- Modify: `core/src/Tichu.Net/MatchSession.cs` (`OnApply`·`Broadcast`·생성자 `interRoundPause`)
- Test: `core/tests/Tichu.Core.Tests/AppliedActionTests.cs` → 미러 `Assets/_Project/Tests/EditMode/AppliedActionTests.cs`

**Interfaces:**
- Consumes: 기존 `MatchSession(IAgent?[], ITichuTransport, ulong, int, TimeSpan?, Action<RoundOutcome>?)`
- Produces:
  - `Tichu.Net.AppliedAction` — `GameActionKind Kind`, `int Seat`, `List<Card>? Cards`, `int RecipientSeat`(기본 -1), `static AppliedAction From(GameAction)`
  - `ServerEvent.Applied` — `AppliedAction?`. `StateUpdate` 이외엔 null
  - `ServerEvent.StateUpdate(PlayerView view, Awaiting? awaiting, AppliedAction? applied = null)`
  - `MatchSession` 생성자 끝에 `TimeSpan? interRoundPause = null`(기본 `TimeSpan.Zero`)

**왜 필요한가:** 뷰는 "무슨 일이 일어났는지"로 로그·효과음·개(Dog) 에코를 만든다. 뷰 두 장을 비교해 역산할 수 없다(패스는 상태를 거의 바꾸지 않고, 같은 카드 집합이 여러 경로로 나올 수 있다). 교환 카드는 싣지 않는다 — 좌석별 마스킹을 액션 요약이 우회해 뚫으면 안 된다.

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`core/tests/Tichu.Core.Tests/AppliedActionTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Game;
using Tichu.GameFlow.Agents;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// StateUpdate 가 자기를 유발한 액션을 싣는지, 그리고 교환 페이로드를 싣지 않는지.
    /// 네 좌석 모두 AI 라 원격 좌석이 없고(OnAwait 미발화) OnApply 경로만 돈다.
    /// </summary>
    [TestFixture]
    public class AppliedActionTests
    {
        private static List<ServerEvent> RunOneRound(out LoopbackTransport transport)
        {
            transport = new LoopbackTransport();
            var agents = new IAgent?[] { new AiAgent(1UL, 0), new AiAgent(1UL, 1), new AiAgent(1UL, 2), new AiAgent(1UL, 3) };
            var session = new MatchSession(agents, transport, masterSeed: 20261005UL, targetScore: 1);
            session.Start();
            Assert.That(session.Join(TimeSpan.FromSeconds(30)), Is.True, "세션이 30초 안에 끝나야 한다");
            return new List<ServerEvent>(transport.Inbox(0));
        }

        [Test]
        public void StateUpdate_carries_the_action_that_caused_it()
        {
            var events = RunOneRound(out _);

            var plays = new List<ServerEvent>();
            foreach (var ev in events)
                if (ev.Kind == ServerEventKind.StateUpdate
                    && ev.Applied != null
                    && ev.Applied.Kind == GameActionKind.Play)
                    plays.Add(ev);

            Assert.That(plays.Count, Is.GreaterThan(0), "한 라운드에 Play 가 한 번도 없을 수 없다");
            foreach (var ev in plays)
            {
                Assert.That(ev.Applied!.Cards, Is.Not.Null, "Play 는 카드를 실어야 한다");
                Assert.That(ev.Applied!.Cards!.Count, Is.GreaterThan(0));
                Assert.That(ev.Applied!.Seat, Is.InRange(0, 3));
            }
        }

        [Test]
        public void Pass_carries_no_cards_but_is_still_reported()
        {
            var events = RunOneRound(out _);

            int passes = 0;
            foreach (var ev in events)
            {
                if (ev.Kind != ServerEventKind.StateUpdate || ev.Applied == null) continue;
                if (ev.Applied.Kind != GameActionKind.Pass) continue;
                passes++;
                Assert.That(ev.Applied.Cards, Is.Null, "Pass 는 카드가 없다");
            }

            Assert.That(passes, Is.GreaterThan(0), "한 라운드에 패스가 한 번도 없을 수 없다");
        }

        [Test]
        public void Exchange_never_carries_card_payload()
        {
            var events = RunOneRound(out _);

            int exchanges = 0;
            foreach (var ev in events)
            {
                if (ev.Kind != ServerEventKind.StateUpdate || ev.Applied == null) continue;
                if (ev.Applied.Kind != GameActionKind.Exchange) continue;
                exchanges++;
                Assert.That(ev.Applied.Cards, Is.Null,
                    "교환 카드를 액션 요약에 실으면 좌석별 마스킹이 우회된다");
            }

            // 경로가 실제로 돌았는지 먼저 확인한다 — 이 단언이 없으면 공허한 가드가 된다.
            Assert.That(exchanges, Is.EqualTo(4), "교환은 좌석별로 네 번 적용된다");
        }

        [Test]
        public void Inter_round_pause_is_cancellable()
        {
            var transport = new LoopbackTransport();
            var agents = new IAgent?[] { new AiAgent(1UL, 0), new AiAgent(1UL, 1), new AiAgent(1UL, 2), new AiAgent(1UL, 3) };
            // targetScore 1000 → 여러 라운드. 멈춤을 길게 둬 첫 라운드 뒤에 반드시 그 안에서 잡힌다.
            var session = new MatchSession(agents, transport, masterSeed: 7UL, targetScore: 1000,
                interRoundPause: TimeSpan.FromSeconds(30));
            session.Start();

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            bool scored = false;
            while (!scored && DateTime.UtcNow < deadline)
            {
                foreach (var ev in transport.Drain(0))
                    if (ev.Kind == ServerEventKind.RoundScored) scored = true;
            }
            Assert.That(scored, Is.True, "첫 라운드가 정산돼야 한다");

            session.Stop();
            // Thread.Sleep 으로 멈추면 30초를 다 기다리므로 여기서 false 가 된다.
            Assert.That(session.Join(TimeSpan.FromSeconds(5)), Is.True,
                "라운드 간 멈춤은 Stop() 으로 깨울 수 있어야 한다");
        }
    }
}
```

`AiAgent` 생성자 인자(좌석 번호)와 `MatchSession` 생성자 매개변수 이름은 코드에서 확인하고 어긋나면 맞춘다. `targetScore: 1` 은 한 라운드로 매치를 끝내려는 것이다.

- [ ] **Step 2: 실패를 확인한다**

```
dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter FullyQualifiedName~AppliedActionTests
```
Expected: 컴파일 실패 — `ServerEvent.Applied` 와 `interRoundPause` 가 없다.

- [ ] **Step 3: `AppliedAction` 을 만든다**

`core/src/Tichu.Net/AppliedAction.cs`:

```csharp
using System.Collections.Generic;
using Tichu.Core.Cards;
using Tichu.Core.Game;

namespace Tichu.Net
{
    /// <summary>
    /// StateUpdate 를 유발한 액션의 공개 요약. 뷰 두 장을 비교해 역산할 수 없는
    /// "무슨 일이 일어났는지" 를 싣는다(패스는 상태를 거의 바꾸지 않고, 같은 카드
    /// 집합이 여러 경로로 나올 수 있다). 로그·효과음·연출이 이걸 먹는다.
    ///
    /// 교환은 카드를 싣지 않는다 — 좌석별 마스킹(기획서 7.5)을 뷰에서 지키면서
    /// 액션 요약으로 우회해 뚫으면 안 된다. 교환이 일어났다는 사실과 좌석만 알린다.
    /// </summary>
    public sealed class AppliedAction
    {
        public GameActionKind Kind { get; set; }
        public int Seat { get; set; }

        /// <summary>Play: 제출된 카드. 그 외 null.</summary>
        public List<Card>? Cards { get; set; }

        /// <summary>GiveDragon: 트릭을 양도받은 좌석. 그 외 -1.</summary>
        public int RecipientSeat { get; set; } = -1;

        /// <summary>권위 액션에서 공개 가능한 부분만 복사한다.</summary>
        public static AppliedAction From(GameAction a)
        {
            var r = new AppliedAction { Kind = a.Kind, Seat = a.Seat, RecipientSeat = -1 };
            switch (a.Kind)
            {
                case GameActionKind.Play:
                    // 값 사본 — 이후 엔진 변경이 이미 보낸 요약에 새지 않는다.
                    r.Cards = a.Cards == null ? null : new List<Card>(a.Cards);
                    break;
                case GameActionKind.GiveDragon:
                    r.RecipientSeat = a.RecipientSeat;
                    break;
            }
            return r;
        }
    }
}
```

- [ ] **Step 4: `ServerEvent` 에 동승시킨다**

`Round` 속성 바로 아래에 추가:

```csharp
        /// <summary>StateUpdate: 이 갱신을 유발한 액션. 라운드 시작·대기 알림엔 null.</summary>
        public AppliedAction? Applied { get; set; }
```

`StateUpdate` 팩토리 교체:

```csharp
        public static ServerEvent StateUpdate(PlayerView view, Awaiting? awaiting, AppliedAction? applied = null)
        {
            view.Awaiting = awaiting;
            return new ServerEvent { Kind = ServerEventKind.StateUpdate, View = view, Applied = applied };
        }
```

- [ ] **Step 5: `MatchSession` 을 배선한다**

`OnApply` 와 `Broadcast` 를 액션까지 받게 바꾼다:

```csharp
        private void OnApply(GameState s, GameAction a) => Broadcast(s, null, AppliedAction.From(a));

        private void Broadcast(GameState s, Awaiting? awaiting, AppliedAction? applied = null)
        {
            CurrentState = s;
            for (int seat = 0; seat < SeatCount; seat++)
                _transport.SendTo(seat, ServerEvent.StateUpdate(ViewProjector.ProjectFor(s, seat), awaiting, applied));
        }
```

`Broadcast(state, null)`(라운드 시작)과 `OnAwait` 는 그대로 — `applied` 가 기본 null 이다.

생성자에 멈춤을 받는다. 매개변수 목록 끝(`onRoundCompleted` 뒤)에 추가:

```csharp
            TimeSpan? interRoundPause = null)
```

필드와 대입:

```csharp
        private readonly TimeSpan _interRoundPause;
```
```csharp
            _interRoundPause = interRoundPause ?? TimeSpan.Zero;
```

`Run()` 의 `if (decision == MatchDecision.Continue) { roundIndex++; continue; }` 를 다음으로 바꾼다:

```csharp
                    if (decision == MatchDecision.Continue)
                    {
                        // 네 클라이언트가 라운드 결과를 읽을 시간. 서버가 쥐는 것이 맞다 —
                        // 다음 라운드가 이미 시작된 뒤엔 클라이언트가 멈출 수 없다.
                        // Stop() 으로 깨울 수 있어야 하므로 Sleep 이 아니라 토큰 대기를 쓴다.
                        if (_interRoundPause > TimeSpan.Zero)
                            _cts.Token.WaitHandle.WaitOne(_interRoundPause);
                        roundIndex++;
                        continue;
                    }
```

- [ ] **Step 6: 테스트 통과를 확인한다**

```
dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter FullyQualifiedName~AppliedActionTests
```
Expected: 4 PASS

- [ ] **Step 7: 기존 전체 스위트 회귀 0 을 확인한다**

```
dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj
```
Expected: 기존 375 + 신규 4 = 379 PASS, 0 FAIL

- [ ] **Step 8: 미러 + 커밋**

```bash
pwsh tools/sync-core-to-unity.ps1
pwsh tools/sync-core-to-unity.ps1 -Check     # "Mirror is in sync." 확인
git add core/src/Tichu.Net core/tests/Tichu.Core.Tests/AppliedActionTests.cs \
        Assets/_Project/Net Assets/_Project/Tests/EditMode/AppliedActionTests.cs
git commit -m "feat(net): StateUpdate 가 유발 액션을 싣고 라운드 간 멈춤을 받는다"
```

---

### Task 2: `TrickView.Top` 을 `Combination?` 하나로

**Files:**
- Modify: `core/src/Tichu.Net/PlayerView.cs` (`TrickView`)
- Modify: `core/src/Tichu.Net/ViewProjector.cs` (`ProjectTrick`)
- Test: `core/tests/Tichu.Core.Tests/ViewProjectorTests.cs` (기존 파일 수정)
- 미러: `Assets/_Project/Net/`, `Assets/_Project/Tests/EditMode/ViewProjectorTests.cs`

**Interfaces:**
- Produces: `TrickView.Top` — `Tichu.Core.Combinations.Combination?`. `TopType`·`TopRank`·`TopLength`·`TopCards` 는 **삭제**된다. `LeadType`·`LeadLength`·`TopOwnerSeat`·`AccumulatedPoints`·`History` 는 그대로.

**왜:** 뷰가 Top 을 조합으로 쓴다 — `CardFormat.TrickSortKey(Combination, Card)` 에 넘기고, `Type == CombinationType.Single` 로 `TrickContext` 를 만들고, `Rank` 를 비교한다. 평탄화한 네 필드를 뷰에서 다시 조합으로 되조립하면 "Top 을 아는 곳"이 두 군데가 된다. `Combination` 은 불변 값이라 뷰에 넘겨도 권위 상태가 새지 않는다(스펙 DoD §2 가 금지하는 건 가변 권위 상태 참조다).

- [ ] **Step 1: 기존 테스트를 새 모양으로 고쳐 실패하게 만든다**

`core/tests/Tichu.Core.Tests/ViewProjectorTests.cs` 에서 `TopType`/`TopRank`/`TopLength`/`TopCards` 를 쓰는 단언을 `Top` 기준으로 바꾸고, 다음 테스트를 추가한다:

```csharp
        [Test]
        public void Trick_top_is_projected_as_a_combination()
        {
            var s = PlayPhaseState(4242UL);            // 이 픽스처에 이미 있는 헬퍼
            AdvancePlay(s, 1);                         // 카드 한 장이 깔리게 한다

            var view = ViewProjector.ProjectFor(s, 0);
            Assert.That(view.CurrentTrick, Is.Not.Null);
            Assert.That(view.CurrentTrick!.Top, Is.Not.Null, "카드가 깔린 트릭은 Top 이 있다");
            Assert.That(view.CurrentTrick.Top!.Cards.Count, Is.EqualTo(s.CurrentTrick!.Top!.Cards.Count));
            Assert.That(view.CurrentTrick.Top.Type, Is.EqualTo(s.CurrentTrick.Top.Type));
            Assert.That(view.CurrentTrick.Top.Rank, Is.EqualTo(s.CurrentTrick.Top.Rank));
        }

        [Test]
        public void Empty_trick_has_null_top()
        {
            var s = GameEngine.NewRound(4242UL);
            var view = ViewProjector.ProjectFor(s, 0);
            // 라운드 시작 시점엔 트릭이 없거나(null) 비어 있다(Top == null) — 둘 다 "깔린 카드 없음".
            Assert.That(view.CurrentTrick == null || view.CurrentTrick.Top == null, Is.True);
        }
```

⚠️ 이 픽스처에는 이미 `PlayPhaseState(ulong seed)` 와 `AdvancePlay(GameState, int actions)` 가 있다(확인함). **새 헬퍼를 만들지 말고 그 둘을 쓴다.** `AdvancePlay` 의 둘째 인자 의미를 파일에서 확인해 트릭에 카드가 깔린 상태가 되게 맞춘다.

- [ ] **Step 2: 실패를 확인한다**

```
dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter FullyQualifiedName~ViewProjectorTests
```
Expected: 컴파일 실패 — `TrickView.Top` 이 없다.

- [ ] **Step 3: `TrickView` 를 바꾼다**

`core/src/Tichu.Net/PlayerView.cs` 의 `TrickView` 를 다음으로 교체:

```csharp
    /// <summary>진행 중 트릭의 공개 상태. 전원에게 동일하다.</summary>
    public sealed class TrickView
    {
        public CombinationType LeadType { get; set; }
        public int LeadLength { get; set; }

        /// <summary>
        /// 현재 격파 대상 조합. 깔린 카드가 없으면 null.
        /// 불변 값이라 그대로 싣는다 — 클라이언트가 정렬·비교에 쓰려면 Type·Rank·Cards 가
        /// 한 덩어리로 필요하고, 평탄화해 보내면 되조립 책임이 클라이언트로 번진다.
        /// </summary>
        public Combination? Top { get; set; }

        public int TopOwnerSeat { get; set; }
        public int AccumulatedPoints { get; set; }
        public List<PlayView> History { get; set; } = new List<PlayView>();
    }
```

`Card` 를 더 쓰지 않게 되면 파일 상단 `using` 은 그대로 둔다(`PlayView`·`PlayerView` 가 여전히 쓴다).

- [ ] **Step 4: `ViewProjector.ProjectTrick` 을 바꾼다**

```csharp
        private static TrickView ProjectTrick(Trick t)
        {
            var tv = new TrickView
            {
                LeadType = t.LeadType,
                LeadLength = t.LeadLength,
                Top = t.Top,                 // 불변 값 — 깊은 복사 불필요
                TopOwnerSeat = t.TopOwnerSeat,
                AccumulatedPoints = t.AccumulatedPoints,
            };

            tv.History = new List<PlayView>(t.History.Count);
            foreach (var p in t.History)
            {
                var pv = new PlayView
                {
                    Seat = p.Seat,
                    IsPass = p.Combination == null,
                    IsBombInterrupt = p.IsBombInterrupt,
                };
                if (p.Combination != null) pv.Cards = new List<Card>(p.Combination.Cards);
                tv.History.Add(pv);
            }

            return tv;
        }
```

`Combination` 은 불변임을 확인했다 — `Type`·`Cards`·`Length`·`Rank`·`PointsInPlay` 가 전부 get-only 이고 `Cards` 는 `IReadOnlyList<Card>` 로 노출된다. 그래서 깊은 복사 없이 그대로 싣는 것이 `ViewProjector` 의 "값 스냅샷" 불변식을 깨지 않는다.

- [ ] **Step 5: 테스트 통과 + 전체 스위트**

```
dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj
```
Expected: 전부 PASS. 마스킹 테스트가 깨지면 `Top` 에 상대 손패 정보가 섞인 것이므로 즉시 멈추고 보고한다.

- [ ] **Step 6: 미러 + 커밋**

```bash
pwsh tools/sync-core-to-unity.ps1 && pwsh tools/sync-core-to-unity.ps1 -Check
git add core Assets/_Project/Net Assets/_Project/Tests/EditMode/ViewProjectorTests.cs
git commit -m "refactor(net): TrickView 가 Top 을 조합 하나로 싣는다"
```

---

### Task 3: `PacedPimcAgent` — 동기 AI + 관전 지연

**Files:**
- Create: `Assets/_Project/Presentation/Agents/PacedPimcAgent.cs`
- Test: `Assets/_Project/Presentation/Tests/PacedPimcAgentTests.cs`

**Interfaces:**
- Consumes: `Tichu.GameFlow.Agents.IAgent`, `PimcAgent`, `PolicyConfig`, `DecisionContext`, `TurnDecision`, `ExchangeChoice`
- Produces: `Tichu.Presentation.PacedPimcAgent : IAgent` — `PacedPimcAgent(ulong roundSeed, int seat, PolicyConfig config, int budgetMs, int delayMs, System.Func<bool>? fastForward = null)`

**⚠️ 유니티 전용이다.** `PimcAgent`·`Pimc`·`PolicyConfig`·`Determinizer` 는 `core/` 에 없고 `Assets/_Project/GameFlow/` 에만 있다(미러가 단방향이라 유니티가 추가 파일을 갖는다). 이 파일은 **미러하지 않는다.** 서버가 빈자리 AI 를 페이싱하려면 PIMC 의 core 승격이 선행돼야 하고, 그건 M5 항목이다.

**⚠️ 겹침을 반드시 보존한다.** 기존 `PimcDecisionAgent` 는 탐색을 `UniTask.RunOnThreadPool` 로 띄워 관전 지연과 **겹친다** — `per-move ≈ max(지연, 연산)`. 난이도 티어(Normal 16 / Hard 20 / Expert 24, 예산 900 통일)가 "탐색이 900ms 지연과 겹쳐 900 이내는 체감 지연 0" 이라는 전제로 설계됐다. 겹침을 잃으면 매 턴이 `지연 + 연산` 이 되어 최대 2배가 되고 그 작업이 무효가 된다. 세션이 **자기 전용 스레드**를 소유하므로 스레드풀 핸드오프의 동기(메인스레드 비차단)는 사라진다 — **연산을 먼저 하고 남은 시간만 잔다.**

**⚠️ 지연 배치를 1:1 로 미러한다.** `PimcDecisionAgent` 는 턴 결정과 **실제로 폭탄을 던질 때만** 지연한다. 큰 티츄·교환·작은 티츄·용 양도는 즉시 반환한다. 폭탄 창은 매 트릭 세 좌석을 폴링하므로 무조건 지연을 걸면 트릭마다 3배 지연이 붙는다.

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Assets/_Project/Presentation/Tests/PacedPimcAgentTests.cs`:

```csharp
using System.Diagnostics;
using NUnit.Framework;
using Tichu.Core.Game;
using Tichu.GameFlow.Agents;

namespace Tichu.Presentation.Tests
{
    /// <summary>
    /// 동기 페이싱 어댑터. 두 가지를 지킨다.
    ///  · 지연 배치 — 턴 결정만 느리고 셋업 결정은 즉시(트릭당 3배 지연 방지).
    ///  · 겹침     — per-move ≈ max(지연, 연산). 잠들고 계산하면 합이 된다.
    /// </summary>
    [TestFixture]
    public class PacedPimcAgentTests
    {
        private const int Delay = 300;

        private static DecisionContext Ctx(GameState s, int seat) => new DecisionContext(s, seat);

        /// <summary>탐색 OFF(Easy) 설정 — 연산이 0 에 가까워 지연만 측정된다.</summary>
        private static PolicyConfig NoSearch => new PolicyConfig(0, 0, 0.0);

        private static long Millis(System.Action a)
        {
            var sw = Stopwatch.StartNew();
            a();
            return sw.ElapsedMilliseconds;
        }

        [Test]
        public void Setup_decisions_are_not_delayed()
        {
            var s = GameEngine.NewRound(31337UL);
            var agent = new PacedPimcAgent(31337UL, 1, NoSearch, budgetMs: 0, delayMs: Delay);

            Assert.That(Millis(() => agent.CallGrandTichu(Ctx(s, 1))), Is.LessThan(Delay / 2),
                "큰 티츄는 즉시 — 지연을 공용 헬퍼에 넣으면 여기서 걸린다");
            Assert.That(Millis(() => agent.ChooseExchange(Ctx(s, 1))), Is.LessThan(Delay / 2),
                "교환은 즉시");
            Assert.That(Millis(() => agent.CallTichu(Ctx(s, 1))), Is.LessThan(Delay / 2),
                "작은 티츄는 즉시");
            Assert.That(Millis(() => agent.ChooseDragonRecipient(Ctx(s, 1))), Is.LessThan(Delay / 2),
                "용 양도는 즉시");
        }

        [Test]
        public void Declined_bomb_is_not_delayed()
        {
            // 폭탄 창은 매 트릭 세 좌석을 폴링한다. 안 던질 때 지연이 걸리면 트릭마다 3배가 된다.
            var s = GameEngine.NewRound(31337UL);
            var agent = new PacedPimcAgent(31337UL, 1, NoSearch, budgetMs: 0, delayMs: Delay);

            long ms = Millis(() => agent.DecideBomb(Ctx(s, 1)));
            Assert.That(ms, Is.LessThan(Delay / 2), "폭탄을 던지지 않으면 즉시 반환");
        }

        [Test]
        public void Turn_decision_is_delayed()
        {
            var s = PlayPhaseState(31337UL);
            var agent = new PacedPimcAgent(31337UL, s.Turn, NoSearch, budgetMs: 0, delayMs: Delay);

            long ms = Millis(() => agent.DecideTurn(Ctx(s, s.Turn)));
            Assert.That(ms, Is.GreaterThanOrEqualTo(Delay - 50), "턴 결정엔 관전 지연이 걸린다");
        }

        [Test]
        public void Search_overlaps_the_delay()
        {
            var s = PlayPhaseState(31337UL);
            int seat = s.Turn;
            // 세계 수를 크게 둬 예산이 반드시 구속되게 만든다 → 연산 ≈ budget.
            var heavy = new PolicyConfig(256, 4, 0.05);
            const int Budget = 500;

            long computeOnly = Millis(() =>
                new PacedPimcAgent(31337UL, seat, heavy, Budget, delayMs: 0).DecideTurn(Ctx(s, seat)));

            // 전제 확인: 연산이 실제로 무겁지 않으면 이 테스트는 판별력이 없다.
            Assume.That(computeOnly, Is.GreaterThan(Budget / 2),
                $"예산이 구속되지 않았다(연산 {computeOnly}ms) — 겹침을 측정할 수 없다");

            long paced = Millis(() =>
                new PacedPimcAgent(31337UL, seat, heavy, Budget, delayMs: Budget).DecideTurn(Ctx(s, seat)));

            // 겹침: ≈ max(지연, 연산) ≈ Budget.  순차: ≈ 지연 + 연산 ≈ 2×Budget.
            Assert.That(paced, Is.LessThan(Budget + computeOnly / 2),
                $"연산이 지연과 겹쳐야 한다(연산 {computeOnly}ms, 페이싱 {paced}ms)");
        }

        /// <summary>
        /// 카드가 깔린 Play 페이즈 상태를 하나 잡아 온다. GameDriver 는 한 단계씩 미는
        /// 공개 진입점이 없으므로(RunRound 뿐), onApply 훅에서 첫 Play 상태를 복제한다.
        /// </summary>
        private static GameState PlayPhaseState(ulong seed)
        {
            var agents = new IAgent[]
            {
                new AiAgent(seed, 0), new AiAgent(seed, 1), new AiAgent(seed, 2), new AiAgent(seed, 3),
            };
            GameState captured = null;
            new GameDriver(agents).RunRound(GameEngine.NewRound(seed), (st, act) =>
            {
                if (captured == null
                    && st.Phase == RoundPhase.Play
                    && st.CurrentTrick != null
                    && st.CurrentTrick.Top != null)
                    captured = st.Clone();
            });
            Assert.That(captured, Is.Not.Null, "Play 페이즈 상태를 잡지 못했다");
            return captured;
        }
    }
}
```

`GameDriver` 의 공개 API 는 생성자와 `RunRound(GameState, Action<GameState,GameAction>? onApply = null, ...)` 뿐이고 `ApplyOneStep` 같은 단계 진입점은 **없다**(확인함). `GameState.Clone()` 은 있다. 위 헬퍼가 그 사실에 맞춘 형태다.

⚠️ `GameEngine.NewRound` 는 `Phase = GrandTichuDecision` 상태를 돌려준다 — 즉 즉시성 단언 네 개가 쓰는 상태는 교환·용 양도 페이즈가 아니다. 이 네 단언은 **반환값이 아니라 소요 시간만** 본다. 그중 하나라도 예외를 던지면 예외를 삼키지 말고, 그 결정에 맞는 페이즈 상태를 `PlayPhaseState` 와 같은 방식(onApply 캡처)으로 잡아 넘긴다.

- [ ] **Step 2: 실패를 확인한다**

유니티 EditMode 로 이 픽스처만 돌린다. UnityMCP 가 붙어 있으면:
```
run_tests(mode="EditMode", test_names=["Tichu.Presentation.Tests.PacedPimcAgentTests"])
```
붙지 않으면 유니티 에디터를 닫고 배치 모드 CLI 폴백(메모리 `unity-mcp-session-reload`)을 쓴다. ⚠️ `run_tests` 전에 PlayMode 는 정지시킨다.
Expected: 컴파일 실패 — `PacedPimcAgent` 가 없다.

- [ ] **Step 3: 구현한다**

`Assets/_Project/Presentation/Agents/PacedPimcAgent.cs`:

```csharp
using System;
using System.Diagnostics;
using System.Threading;
using Tichu.Core.Combinations;
using Tichu.GameFlow.Agents;

namespace Tichu.Presentation
{
    /// <summary>
    /// 동기 다세계 <see cref="PimcAgent"/> 를 세션 스레드용 <see cref="IAgent"/> 로 감싸는
    /// 페이싱 어댑터. <see cref="PimcDecisionAgent"/>(비동기 경로)의 동기 쌍둥이이며 지연
    /// 배치를 1:1 로 맞춘다 — 턴 결정과 **실제로 던지는** 폭탄만 지연하고, 셋업 결정은
    /// 즉시 반환한다(폭탄 창은 매 트릭 세 좌석을 폴링하므로 무조건 지연은 3배가 된다).
    ///
    /// 겹침을 보존한다: per-move ≈ max(지연, 연산). 비동기 어댑터는 이를 스레드풀
    /// 핸드오프로 얻지만, 여기서는 세션이 자기 전용 스레드를 소유해 메인스레드를 막을
    /// 걱정이 없으므로 **연산을 먼저 하고 남은 시간만 잔다.** 추가 스레드가 없고, 소유
    /// 스레드가 직접 계산하므로 방어적 상태 복사도 필요 없다.
    ///
    /// PIMC 가 아직 core 에 없어(유니티 쪽 GameFlow 전용) 이 파일은 미러되지 않는다.
    /// 서버가 빈자리 AI 를 페이싱하는 일은 PIMC 의 core 승격(M5) 뒤의 몫이다.
    /// </summary>
    public sealed class PacedPimcAgent : IAgent
    {
        private readonly PimcAgent _pimc;
        private readonly PolicyConfig _config;
        private readonly int _budgetMs;
        private readonly int _delayMs;
        private readonly Func<bool> _fastForward;

        public PacedPimcAgent(ulong roundSeed, int seat, PolicyConfig config,
            int budgetMs, int delayMs, Func<bool> fastForward = null)
        {
            _pimc = new PimcAgent(roundSeed, seat, config);
            _config = config;
            _budgetMs = budgetMs;
            _delayMs = delayMs;
            _fastForward = fastForward;
        }

        public bool CallGrandTichu(in DecisionContext ctx) => _pimc.CallGrandTichu(ctx);

        public ExchangeChoice ChooseExchange(in DecisionContext ctx) => _pimc.ChooseExchange(ctx);

        public bool CallTichu(in DecisionContext ctx) => _pimc.CallTichu(ctx);

        public int ChooseDragonRecipient(in DecisionContext ctx) => _pimc.ChooseDragonRecipient(ctx);

        public TurnDecision DecideTurn(in DecisionContext ctx)
        {
            var sw = Stopwatch.StartNew();

            TurnDecision d;
            if (_config.Worlds <= 0)
            {
                d = _pimc.DecideTurn(ctx);          // 탐색 OFF(Easy) — 예산이 의미 없다.
            }
            else
            {
                using (var budget = new CancellationTokenSource(_budgetMs))
                    d = _pimc.DecideTurnAnytime(ctx, budget.Token, CancellationToken.None);
            }

            SleepRemaining(sw);                      // 연산과 겹친 만큼 깎아서 잔다.
            return d;
        }

        public Combination DecideBomb(in DecisionContext ctx)
        {
            var bomb = _pimc.DecideBomb(ctx);
            if (bomb != null)
            {
                var sw = Stopwatch.StartNew();
                SleepRemaining(sw);                  // 실제로 던질 때만 — 거절은 즉시.
            }
            return bomb;
        }

        /// <summary>관전 지연에서 이미 흐른 연산 시간을 빼고 남은 만큼만 잔다.</summary>
        private void SleepRemaining(Stopwatch sw)
        {
            if (_fastForward != null && _fastForward()) return;
            int rest = _delayMs - (int)sw.ElapsedMilliseconds;
            if (rest > 0) Thread.Sleep(rest);
        }
    }
}
```

⚠️ `IAgent` 의 정확한 시그니처(`in DecisionContext` 유무, `Combination?` 널 허용 표기)를 `Assets/_Project/GameFlow/Agents/IAgent.cs` 에서 확인해 **그대로** 맞춘다. 유니티 쪽은 `#nullable` 이 켜져 있지 않을 수 있으므로 `Combination?` 대신 `Combination` 이 맞을 수 있다 — 기존 `AiAgent`/`RandomAgent` 의 선언을 베낀다.

- [ ] **Step 4: 통과를 확인한다**

```
run_tests(mode="EditMode", test_names=["Tichu.Presentation.Tests.PacedPimcAgentTests"])
```
Expected: 4 PASS (겹침 테스트는 전제가 안 서면 Inconclusive — 그 경우 세계 수를 1024 로 올려 다시 돌리고, 그래도 안 서면 보고서에 적는다)

- [ ] **Step 5: 커밋**

```bash
git add Assets/_Project/Presentation/Agents/PacedPimcAgent.cs* \
        Assets/_Project/Presentation/Tests/PacedPimcAgentTests.cs*
git commit -m "feat(presentation): 세션 스레드용 동기 PIMC 페이싱 어댑터"
```

---

### Task 4: 재배선 — 뷰모델·클라이언트·뷰·부트스트랩

**Files:**
- Modify: `Assets/_Project/Presentation/Tichu.Presentation.asmdef` (+`Tichu.Net`)
- Modify: `Assets/_Project/Presentation/Tests/Tichu.Presentation.Tests.asmdef` (+`Tichu.Net`)
- Rewrite: `Assets/_Project/Presentation/ViewModel/TableViewModel.cs`
- Create: `Assets/_Project/Presentation/Net/TichuClient.cs`
- Modify: `Assets/_Project/Presentation/Views/RuntimeTableView.cs` (아래 13곳)
- Modify: `Assets/_Project/Presentation/Views/ITableView.cs` (구독 계약 주석)
- Modify: `Assets/_Project/Presentation/Audio/SfxMap.cs` (`AppliedAction` 오버로드)
- Modify: `Assets/_Project/Presentation/RoundBootstrap.cs` (`RunMatchAsync`)
- Test: `Assets/_Project/Presentation/Tests/TableViewModelTests.cs` (재작성), `TichuClientTests.cs`(신규), `ITableViewContractTests.cs`(1곳 수정)

**Interfaces:**
- Consumes: Task 1 의 `AppliedAction`·`ServerEvent.Applied`·`MatchSession(..., interRoundPause)`, Task 2 의 `TrickView.Top`, Task 3 의 `PacedPimcAgent`
- Produces:
  - `TableViewModel` — `void Apply(PlayerView view)` · `ReactiveProperty<TrickView?> CurrentTrick` · `ReactiveProperty<Awaiting?> Prompt` · `Observable<AppliedAction> Played` · `Observable<ClientAction> Submitted` · `bool SubmitGrandTichu(bool)` · `bool SubmitExchange(ExchangeChoice)` · `bool SubmitPlay(IReadOnlyList<Card>, int? wish = null)` · `bool SubmitPass()` · `bool SubmitDragonRecipient(int)` · `bool DeclareSmallTichu()` · `void OnRoundScored(RoundResult)` · `void ClearPlays()` · `bool HasPendingBomb`
  - `TichuClient` — `TichuClient(LoopbackTransport transport, TableViewModel vm, int mySeat, Action<MatchSummary> onMatchEnded)` · `void Pump()` · `string LastRejection`

#### 설계 메모 (구현자가 알아야 하는 것)

**1. 대기·취소 기계가 전부 사라진다.** 여섯 개 `UniTaskCompletionSource`, 여섯 개 `CancelXxx`, `_pendingCtx`, `_hasCachedTurn`/`_cachedTurn`, `TakePendingTichuSeat`, `TakePendingBomb`, `ReserveBomb` 가 모두 서버의 `RemoteAgent` 우편함으로 옮겨 갔다. 클라이언트는 **보내고 잊는다.** 서버가 거부하면 `ActionRejected` 가 온다.

**2. 작은 티츄는 서버가 묻지 않는다.** `StepKind` 에 작은 티츄가 없고(`GrandTichu, Exchange, Play, DragonGift, Scoring`) `RemoteAgent.CallTichu` 는 `TakeReservedTichu()` 로 즉시 반환한다. 즉 클라이언트는 버튼을 누를 때 `ClientAction.CallTichu` 를 쏘면 끝이다 — 질문에 답하는 경로가 없다. `DecisionKind.Tichu` 분기는 **죽은 코드가 된다.**

**3. 차례밖 폭탄도 질문이 아니다.** `RemoteAgent.DecideBomb` 은 항상 null 이고, 폭탄은 "차례밖에 보낸 `Play`" 로만 들어간다. 세션의 `TakePendingBomb` 이 매 Play 반복 머리에서 꺼내 합법이면 적용한다. `DecisionKind.Bomb` 분기도 **죽은 코드가 된다.**

**4. 알고 받아들이는 동작 차이 — 폭탄 선점.** 지금은 `BombReserved` 신호가 진행 중인 AI 결정을 **취소**해 폭탄이 즉시 끼어든다. 세션 경로에는 그 취소 통로가 없다(세션 스레드가 그 결정을 직접 돌고 있다). 폭탄은 **다음 Play 반복 머리**에서 적용되므로 최대 AI 한 수만큼 늦는다. 멀티플레이에서는 이미 커밋된 남의 수를 되돌리지 않는 쪽이 오히려 맞고, 결정 중단 토큰은 M5(턴 시계·인터럽트) 몫이다. `BombReserved` 관측값은 이 변경으로 고아가 되므로 **뷰모델에서 삭제한다**(`AsyncGameDriver` 의 매개변수는 그대로 둔다 — 사용 중단 경로다).

**5. 누적 점수와 매치 종료는 서버가 말한다.** `PlayerView.TeamAScore/TeamBScore` 는 **라운드** 점수다. 매치 누적은 클라이언트가 `RoundScored` 를 더해 만들고(세션의 자체 누적과 같은 산술), 종료는 `MatchEnded` 를 받아 `onMatchEnded` 로 올린다. `MatchRunner.Decide` 를 클라이언트에서 부르던 중복 권위 판정이 사라진다.

**6. 라운드 경계 감지.** `RoundScored` 다음에 오는 첫 `StateUpdate` 가 새 라운드의 시작이다(`MatchSession.Run` 의 순서: `RoundScored` → `Decide` → 멈춤 → `NewRound` → `Broadcast`). 클라이언트가 `bool _roundScored` 를 들고 그때 `RoundResult=null` + `ClearPlays()` 를 호출한다. 라운드 인덱스를 프로토콜에 추가할 필요가 없다.

**7. 프롬프트가 깜빡이지 않는 이유.** `OnApply` 방송은 `Awaiting = null` 이다. 하지만 세션 스레드는 사람에게 물을 때 `Await` 에서 **블로킹**하므로, 내가 질문을 받은 뒤 다른 좌석의 액션이 적용되는 일이 없다. `OnAwait` 는 항상 세션이 멈추기 직전의 마지막 이벤트다. 따라서 `Prompt = (aw != null && aw.Seat == mySeat) ? aw : null` 이 안전하다.

**8. 교환 제출 현황.** `AppliedAction(Exchange, seat)` 이 네 번 오므로 세면 된다(스펙 §7 이 `InternalsVisibleTo` 없이 해결). **이 증분에서 표시 UI 는 만들지 않는다** — 데이터가 열렸다는 것만 기록한다.

- [ ] **Step 1: 어셈블리 참조를 추가한다**

`Assets/_Project/Presentation/Tichu.Presentation.asmdef` 의 `references` 에 `"Tichu.Net"` 을 `"Tichu.GameFlow"` 다음에 추가. 같은 작업을 `Assets/_Project/Presentation/Tests/Tichu.Presentation.Tests.asmdef` 에도 한다.

⚠️ 새/변경 `.cs` 와 `.asmdef` 는 `execute_code` 로 `AssetDatabase.ImportAsset` + `Refresh(ForceUpdate)` 를 돌려야 유니티가 집는다(`refresh_unity` 만으로는 임포트가 누락된다 — 메모리 기록).

- [ ] **Step 2: 뷰모델 테스트를 새 API 로 재작성한다(실패하게)**

`Assets/_Project/Presentation/Tests/TableViewModelTests.cs` 를 다음으로 교체:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.Net;
using Tichu.Presentation.ViewModel;

namespace Tichu.Presentation.Tests
{
    /// <summary>
    /// 뷰모델은 서버가 보낸 PlayerView 만 먹고, 사람의 입력을 ClientAction 으로 낸다.
    /// 권위 상태(GameState)를 들고 있지 않다.
    /// </summary>
    [TestFixture]
    public class TableViewModelTests
    {
        private static PlayerView ViewOf(GameState s, int seat) => ViewProjector.ProjectFor(s, seat);

        [Test]
        public void Apply_projects_player_view()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);

            vm.Apply(ViewOf(s, 0));

            Assert.That(vm.Phase.CurrentValue, Is.EqualTo(s.Phase));
            Assert.That(vm.MyHand.CurrentValue.Count, Is.EqualTo(s.Seats[0].Hand.Count));
            for (int i = 0; i < 4; i++)
                Assert.That(vm.HandCount(i).CurrentValue, Is.EqualTo(s.Seats[i].Hand.Count));
        }

        [Test]
        public void Apply_never_exposes_another_seats_hand()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);

            vm.Apply(ViewOf(s, 0));

            // 좌석 1 의 손패 카드가 내 손패 투영에 섞이면 안 된다.
            foreach (var card in s.Seats[1].Hand)
                Assert.That(vm.MyHand.CurrentValue.Contains(card), Is.False,
                    $"좌석 1 의 {card} 가 내 손패에 보인다");
        }

        [Test]
        public void Prompt_is_set_only_for_my_seat()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);

            var mine = ViewOf(s, 0);
            mine.Awaiting = new Awaiting { Kind = StepKind.GrandTichu, Seat = 0 };
            vm.Apply(mine);
            Assert.That(vm.Prompt.CurrentValue, Is.Not.Null);

            var theirs = ViewOf(s, 0);
            theirs.Awaiting = new Awaiting { Kind = StepKind.GrandTichu, Seat = 2 };
            vm.Apply(theirs);
            Assert.That(vm.Prompt.CurrentValue, Is.Null, "남의 차례는 프롬프트가 아니다");
        }

        [Test]
        public void Submitting_a_play_emits_a_client_action_with_increasing_seq()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            var view = ViewOf(s, 0);
            view.Awaiting = new Awaiting
            {
                Kind = StepKind.Play,
                Seat = 0,
                CanPass = true,
                LegalMoves = new List<Tichu.Core.Combinations.Combination>(),
            };
            vm.Apply(view);

            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));

            Assert.That(vm.SubmitPass(), Is.True);
            Assert.That(vm.SubmitPlay(new List<Card> { vm.MyHand.CurrentValue[0] }), Is.True);

            Assert.That(sent.Count, Is.EqualTo(2));
            Assert.That(sent[0].Kind, Is.EqualTo(ClientActionKind.Pass));
            Assert.That(sent[0].Seat, Is.EqualTo(0));
            Assert.That(sent[1].Kind, Is.EqualTo(ClientActionKind.Play));
            Assert.That(sent[1].ActionSeq, Is.GreaterThan(sent[0].ActionSeq),
                "ActionSeq 는 좌석별 단조 증가라야 서버가 재생을 막을 수 있다");
        }

        [Test]
        public void Pass_is_refused_when_the_server_says_it_cannot_pass()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            var view = ViewOf(s, 0);
            view.Awaiting = new Awaiting { Kind = StepKind.Play, Seat = 0, CanPass = false };
            vm.Apply(view);

            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));

            Assert.That(vm.SubmitPass(), Is.False, "서버가 금지한 패스는 내보내지 않는다");
            Assert.That(sent.Count, Is.EqualTo(0));
        }

        [Test]
        public void Nothing_is_submitted_when_no_prompt_is_mine()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            vm.Apply(ViewOf(s, 0));            // Awaiting == null

            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));

            Assert.That(vm.SubmitPass(), Is.False);
            Assert.That(vm.SubmitDragonRecipient(1), Is.False);
            Assert.That(sent.Count, Is.EqualTo(0));
        }

        [Test]
        public void Small_tichu_is_fire_and_forget()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            vm.Apply(ViewOf(s, 0));            // 프롬프트 없음 — 그래도 보낼 수 있어야 한다

            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));

            Assert.That(vm.DeclareSmallTichu(), Is.True,
                "작은 티츄는 서버가 묻지 않는다 — 언제든 예약으로 보낸다");
            Assert.That(sent.Count, Is.EqualTo(1));
            Assert.That(sent[0].Kind, Is.EqualTo(ClientActionKind.CallTichu));
            Assert.That(vm.TichuAvailable.CurrentValue, Is.False, "보낸 뒤엔 버튼을 숨긴다");
        }
    }
}
```

`using var` 가 유니티 C# 버전에서 안 되면 `using (var sub = ...) { }` 블록으로 바꾼다.

- [ ] **Step 3: `TableViewModel` 을 교체한다**

`Assets/_Project/Presentation/ViewModel/TableViewModel.cs` 전체를 다음으로 바꾼다:

```csharp
using System;
using System.Collections.Generic;
using R3;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;
using Tichu.Net;

namespace Tichu.Presentation.ViewModel
{
    /// <summary>
    /// 서버가 보낸 <see cref="PlayerView"/> 를 R3 ReactiveProperty 로 투영하고, 사람의
    /// 입력을 <see cref="ClientAction"/> 으로 내보낸다. 권위 상태(GameState)를 들고 있지
    /// 않다 — 여기 있는 값은 전부 서버가 내 좌석 시야로 마스킹해 보낸 것이다.
    ///
    /// 입력은 "보내고 잊는다". 수락·거부는 서버가 ActionRejected 로 알려 주며, 여기서
    /// TCS 로 기다리지 않는다 — 그 대기·취소 기계가 서버의 RemoteAgent 우편함으로
    /// 옮겨 갔기 때문이다. Submit* 의 bool 은 "지금 Awaiting 이 이 입력을 허용하는가"
    /// 만 뜻한다(로컬 선검사). false 면 아무것도 내보내지 않는다.
    /// </summary>
    public sealed class TableViewModel
    {
        private const int FullHand = 14;

        private readonly int _mySeat;
        private long _actionSeq;

        // ── 서버 시야 투영 ───────────────────────────────────────────────────

        /// <summary>현재 라운드 페이즈.</summary>
        public ReactiveProperty<RoundPhase> Phase { get; } = new ReactiveProperty<RoundPhase>();

        /// <summary>현재 차례 좌석(Play 페이즈에서만 유효, 그 외 -1).</summary>
        public ReactiveProperty<int> CurrentTurn { get; } = new ReactiveProperty<int>(-1);

        /// <summary>내 손패.</summary>
        public ReactiveProperty<IReadOnlyList<Card>> MyHand { get; }
            = new ReactiveProperty<IReadOnlyList<Card>>(new List<Card>());

        /// <summary>진행 중 트릭의 공개 상태.</summary>
        public ReactiveProperty<TrickView> CurrentTrick { get; } = new ReactiveProperty<TrickView>();

        /// <summary>마지막으로 완료된 라운드 결산. 라운드 시작 전 null.</summary>
        public ReactiveProperty<RoundResult> RoundResult { get; } = new ReactiveProperty<RoundResult>();

        /// <summary>서버가 지금 **나에게** 묻고 있는 것. 내 차례가 아니면 null.</summary>
        public ReactiveProperty<Awaiting> Prompt { get; } = new ReactiveProperty<Awaiting>();

        /// <summary>현재 활성 마작 소원 랭크(없으면 null).</summary>
        public ReactiveProperty<int?> Wish { get; } = new ReactiveProperty<int?>();

        /// <summary>매치 누적 팀 점수. RoundScored 를 더해 만든다.</summary>
        public ReactiveProperty<int> CumulativeA { get; } = new ReactiveProperty<int>(0);
        public ReactiveProperty<int> CumulativeB { get; } = new ReactiveProperty<int>(0);

        /// <summary>작은 티츄 선언 가능 여부(Play·내 손패 14장·미콜·미전송).</summary>
        public ReactiveProperty<bool> TichuAvailable { get; } = new ReactiveProperty<bool>(false);

        private readonly ReactiveProperty<int>[] _handCounts = new ReactiveProperty<int>[4];
        private readonly ReactiveProperty<TichuCall>[] _calls = new ReactiveProperty<TichuCall>[4];

        /// <summary>좌석 i 의 손패 수.</summary>
        public ReactiveProperty<int> HandCount(int seat) => _handCounts[seat];

        /// <summary>좌석 i 의 티츄 콜 상태.</summary>
        public ReactiveProperty<TichuCall> SeatCall(int seat) => _calls[seat];

        // ── 스트림 ───────────────────────────────────────────────────────────

        private readonly Subject<AppliedAction> _played = new Subject<AppliedAction>();
        /// <summary>서버가 적용한 액션(로그·효과음·연출이 먹는다).</summary>
        public Observable<AppliedAction> Played => _played;

        private readonly Subject<R3.Unit> _cleared = new Subject<R3.Unit>();
        /// <summary>새 라운드 시작(로그 비우기).</summary>
        public Observable<R3.Unit> PlaysCleared => _cleared;

        private readonly Subject<ClientAction> _submitted = new Subject<ClientAction>();
        /// <summary>내가 서버로 올릴 액션. TichuClient 가 전송에 밀어 넣는다.</summary>
        public Observable<ClientAction> Submitted => _submitted;

        // ── 로컬 전용 상태 ───────────────────────────────────────────────────

        // 빠른 진행(스킵). 세션 스레드의 AI 가 읽으므로 volatile 로 둔다.
        private volatile bool _fastForward;

        /// <summary>true 면 AI 관전 지연을 건너뛴다. 내가 아웃된 뒤 사용, 새 라운드에 해제.</summary>
        public bool FastForward
        {
            get { return _fastForward; }
            set { _fastForward = value; }
        }

        // 작은 티츄를 보냈다(서버 적용 전이라도 버튼을 숨긴다).
        private bool _tichuSent;
        // 차례밖 폭탄을 보냈다(뷰가 "발동!" 표시). 내 Play 가 적용되면 해제.
        private bool _bombSent;

        /// <summary>차례밖 폭탄 전송 상태(뷰 표시용).</summary>
        public bool HasPendingBomb { get { return _bombSent; } }

        public TableViewModel(int mySeat)
        {
            _mySeat = mySeat;
            for (int i = 0; i < 4; i++)
            {
                _handCounts[i] = new ReactiveProperty<int>(0);
                _calls[i] = new ReactiveProperty<TichuCall>(TichuCall.None);
            }
        }

        // ── 수신 ─────────────────────────────────────────────────────────────

        /// <summary>서버 StateUpdate 의 뷰를 투영한다.</summary>
        public void Apply(PlayerView v)
        {
            Phase.Value = v.Phase;
            Wish.Value = v.Wish;

            // 트릭은 매번 새 객체라 참조 비교로 통지된다(강제 재통지 불필요 —
            // 제자리 변경되는 GameState 를 직접 보던 시절의 우회가 사라졌다).
            CurrentTrick.Value = v.CurrentTrick;

            for (int i = 0; i < 4; i++)
            {
                _handCounts[i].Value = v.HandCounts[i];
                _calls[i].Value = v.Calls[i];
            }

            TichuAvailable.Value = v.Phase == RoundPhase.Play
                && v.Calls[_mySeat] == TichuCall.None
                && v.HandCounts[_mySeat] == FullHand
                && !_tichuSent;

            CurrentTurn.Value = v.Phase == RoundPhase.Play ? v.Turn : -1;

            // 내게 온 질문만 프롬프트다. 남의 차례 알림은 축약판이라 합법수가 없다.
            Prompt.Value = (v.Awaiting != null && v.Awaiting.Seat == _mySeat) ? v.Awaiting : null;

            // MyHand 는 마지막 — 손패 렌더가 최신 트릭/차례를 참조한다.
            MyHand.Value = new List<Card>(v.MyHand);
        }

        /// <summary>서버가 적용한 액션을 로그·사운드로 흘린다.</summary>
        public void Record(AppliedAction a)
        {
            if (a.Kind == GameActionKind.Play && a.Seat == _mySeat) _bombSent = false;

            if (a.Kind == GameActionKind.Play
                || a.Kind == GameActionKind.Pass
                || a.Kind == GameActionKind.GiveDragon)
                _played.OnNext(a);
        }

        /// <summary>라운드 정산 수신 — 누적에 더하고 결과를 띄운다.</summary>
        public void OnRoundScored(RoundResult r)
        {
            CumulativeA.Value += r.TeamATotal;
            CumulativeB.Value += r.TeamBTotal;
            RoundResult.Value = r;
        }

        /// <summary>새 라운드 시작: 로그·로컬 예약 상태를 비운다.</summary>
        public void ClearPlays()
        {
            _tichuSent = false;
            _bombSent = false;
            RoundResult.Value = null;
            FastForward = false;
            _cleared.OnNext(default);
        }

        // ── 송신 (로컬 선검사 후 발행) ───────────────────────────────────────

        /// <summary>큰 티츄 선언/패스. 큰 티츄 질문을 받은 상태여야 한다.</summary>
        public bool SubmitGrandTichu(bool call)
        {
            if (!Asking(StepKind.GrandTichu)) return false;
            Send(call
                ? ClientAction.CallGrandTichu(_mySeat, NextSeq())
                : ClientAction.DeclineGrandTichu(_mySeat, NextSeq()));
            return true;
        }

        /// <summary>교환 3장. 서로 다르고 모두 내 손패에 있어야 한다.</summary>
        public bool SubmitExchange(ExchangeChoice choice)
        {
            if (!Asking(StepKind.Exchange)) return false;
            if (!IsDistinctAndInHand(choice.ToLeft, choice.ToPartner, choice.ToRight, MyHand.CurrentValue))
                return false;
            Send(ClientAction.Exchange(_mySeat, NextSeq(), choice.ToLeft, choice.ToPartner, choice.ToRight));
            return true;
        }

        /// <summary>패스. 서버가 CanPass 라고 알린 상태여야 한다.</summary>
        public bool SubmitPass()
        {
            var aw = Prompt.CurrentValue;
            if (aw == null || aw.Kind != StepKind.Play || !aw.CanPass) return false;
            Send(ClientAction.Pass(_mySeat, NextSeq()));
            return true;
        }

        /// <summary>
        /// 카드를 낸다. 내 차례면 합법수 목록으로 선검사하고, 차례밖이면 선검사 없이
        /// 보낸다 — 차례밖 Play 는 서버가 폭탄 예약으로 받아 합법일 때 적용한다.
        /// </summary>
        public bool SubmitPlay(IReadOnlyList<Card> cards, int? wish = null)
        {
            if (cards == null || cards.Count == 0) return false;

            var aw = Prompt.CurrentValue;
            bool myTurn = aw != null && aw.Kind == StepKind.Play;

            var combo = CombinationRecognizer.Recognize(ToArray(cards), TrickContext.Lead);
            if (myTurn)
            {
                if (aw.LegalMoves == null || !IsMoveLegal(combo, aw.LegalMoves)) return false;
            }
            else
            {
                if (combo.Type == CombinationType.Invalid || !combo.IsBomb) return false;
                _bombSent = true;
            }

            // Rank 는 의도 확인용이다(모호성 해소가 아니다 — 기획서 11.1).
            Send(ClientAction.Play(_mySeat, NextSeq(), new List<Card>(cards), combo.Rank, wish));
            return true;
        }

        /// <summary>용 양도 대상(상대 좌석).</summary>
        public bool SubmitDragonRecipient(int seat)
        {
            if (!Asking(StepKind.DragonGift)) return false;
            if (seat < 0 || seat > 3 || (seat % 2) == (_mySeat % 2)) return false;
            Send(ClientAction.GiveDragon(_mySeat, NextSeq(), seat));
            return true;
        }

        /// <summary>
        /// 작은 티츄를 선언한다. 서버는 이걸 질문으로 묻지 않고 예약으로 받으므로
        /// 프롬프트 없이 언제든 보낼 수 있다(늦으면 서버가 조용히 버린다 — M5 에서 통보).
        /// </summary>
        public bool DeclareSmallTichu()
        {
            if (_tichuSent) return false;
            _tichuSent = true;
            TichuAvailable.Value = false;
            Send(ClientAction.CallTichu(_mySeat, NextSeq()));
            return true;
        }

        // ── 내부 ─────────────────────────────────────────────────────────────

        private bool Asking(StepKind kind)
        {
            var aw = Prompt.CurrentValue;
            return aw != null && aw.Kind == kind;
        }

        private long NextSeq() { return ++_actionSeq; }

        private void Send(ClientAction a) { _submitted.OnNext(a); }

        private static Card[] ToArray(IReadOnlyList<Card> cards)
        {
            var arr = new Card[cards.Count];
            for (int i = 0; i < cards.Count; i++) arr[i] = cards[i];
            return arr;
        }

        // 합법성: 카드 정확 일치 대신 (Type, Rank) 등가 — 같은 랭크 다른 무늬도 합법이다
        // (GenerateSingles 가 랭크당 1장만 내므로 정확 일치는 다른 무늬를 거부한다).
        private static bool IsMoveLegal(Combination move, IReadOnlyList<Combination> legalMoves)
        {
            for (int i = 0; i < legalMoves.Count; i++)
                if (legalMoves[i].Type == move.Type && legalMoves[i].Rank == move.Rank)
                    return true;
            return false;
        }

        /// <summary>교환 카드 3장이 손패 안에 있고 서로 다른지 확인한다.</summary>
        private static bool IsDistinctAndInHand(Card l, Card p, Card r, IReadOnlyList<Card> hand)
        {
            if (l.Equals(p) || l.Equals(r) || p.Equals(r)) return false;
            var remaining = new List<Card>(hand.Count);
            for (int i = 0; i < hand.Count; i++) remaining.Add(hand[i]);
            return remaining.Remove(l) && remaining.Remove(p) && remaining.Remove(r);
        }
    }
}
```

⚠️ 유니티 쪽 Presentation 은 `#nullable` 이 꺼져 있다 — 위처럼 `TrickView`/`Awaiting`/`RoundResult` 를 **널 허용 표기 없이** 쓴다(기존 파일이 `Trick?` 를 썼다면 그 파일에 `#nullable enable` 이 있는지 확인하고 맞춘다). `CombinationRecognizer.Recognize` 가 `Combination.Invalid` 를 돌려주는 실패 표현인지(예외가 아닌지) 확인한다.

- [ ] **Step 4: 뷰모델 테스트 통과를 확인한다**

```
run_tests(mode="EditMode", test_names=["Tichu.Presentation.Tests.TableViewModelTests"])
```
Expected: 7 PASS. 이 시점에 `RuntimeTableView`·`RoundBootstrap` 은 아직 컴파일되지 않으므로 어셈블리가 빨갈 수 있다 — Step 5~8 까지 한 번에 간 뒤 돌려도 된다.

- [ ] **Step 5: `TichuClient` 를 만든다**

`Assets/_Project/Presentation/Net/TichuClient.cs`:

```csharp
using System;
using R3;
using Tichu.Net;
using Tichu.Presentation.Shell;
using Tichu.Presentation.ViewModel;

namespace Tichu.Presentation.Net
{
    /// <summary>
    /// 전송 수신함과 뷰모델 사이의 펌프. 매 프레임 Pump() 가 호출되며, 서버 이벤트를
    /// 뷰모델에 밀어넣고 뷰모델이 낸 ClientAction 을 전송에 올린다.
    ///
    /// 유니티 타입에 의존하지 않는다 — 프레임 구동은 호출자(RoundBootstrap)의 몫이고
    /// 여기서는 순서만 책임진다. 그래서 헤드리스로 전 경로를 테스트할 수 있다.
    /// </summary>
    public sealed class TichuClient : IDisposable
    {
        private readonly LoopbackTransport _transport;
        private readonly TableViewModel _vm;
        private readonly int _mySeat;
        private readonly Action<MatchSummary> _onMatchEnded;
        private readonly IDisposable _outbound;

        // RoundScored 다음에 오는 첫 StateUpdate 가 새 라운드의 시작이다
        // (세션 순서: RoundScored → Decide → 멈춤 → NewRound → Broadcast).
        private bool _roundScored;

        /// <summary>마지막 거부 사유(뷰 표시·디버깅용). 없으면 null.</summary>
        public string LastRejection { get; private set; }

        /// <summary>매치 종료 이벤트를 받았는가.</summary>
        public bool MatchEnded { get; private set; }

        public TichuClient(LoopbackTransport transport, TableViewModel vm, int mySeat,
            Action<MatchSummary> onMatchEnded = null)
        {
            _transport = transport;
            _vm = vm;
            _mySeat = mySeat;
            _onMatchEnded = onMatchEnded;
            _outbound = _vm.Submitted.Subscribe(_transport.Submit);
        }

        /// <summary>수신함을 비우고 순서대로 적용한다. 메인스레드에서 호출한다.</summary>
        public void Pump()
        {
            var events = _transport.Drain(_mySeat);
            for (int i = 0; i < events.Count; i++) Handle(events[i]);
        }

        private void Handle(ServerEvent ev)
        {
            switch (ev.Kind)
            {
                case ServerEventKind.StateUpdate:
                    if (_roundScored)
                    {
                        _roundScored = false;
                        _vm.ClearPlays();           // 새 라운드: 결과·로그·로컬 예약 비우기
                    }
                    if (ev.View != null) _vm.Apply(ev.View);
                    if (ev.Applied != null) _vm.Record(ev.Applied);
                    break;

                case ServerEventKind.RoundScored:
                    if (ev.Round != null) _vm.OnRoundScored(ev.Round);
                    _roundScored = true;
                    break;

                case ServerEventKind.MatchEnded:
                    MatchEnded = true;
                    if (_onMatchEnded != null)
                        _onMatchEnded(new MatchSummary(ev.WinningTeam, ev.TeamA, ev.TeamB));
                    break;

                case ServerEventKind.ActionRejected:
                    // 서버가 내 액션을 되돌렸다. 상태는 StateUpdate 가 진실이라 되돌릴 것이
                    // 없고, 사유만 남긴다(사용자 표면화는 M5 — 거부된 예약 관측 가능화와 함께).
                    LastRejection = ev.Reason;
                    break;

                case ServerEventKind.SessionFaulted:
                    LastRejection = ev.Reason;
                    break;
            }
        }

        public void Dispose() { _outbound.Dispose(); }
    }
}
```

⚠️ `MatchSummary` 의 생성자 인자 순서(`winner, teamA, teamB`)와 네임스페이스(`Tichu.Presentation.Shell`)를 `RoundBootstrap` 의 기존 호출부에서 확인해 맞춘다. `ev.Applied` 를 `_vm.Apply` **뒤에** 흘리는 순서가 중요하다 — 로그·사운드가 최신 뷰 위에서 돌아야 한다.

- [ ] **Step 6: 클라이언트 왕복 테스트를 쓴다**

`Assets/_Project/Presentation/Tests/TichuClientTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Tichu.GameFlow.Agents;
using Tichu.Net;
using Tichu.Presentation.Net;
using Tichu.Presentation.Shell;
using Tichu.Presentation.ViewModel;

namespace Tichu.Presentation.Tests
{
    /// <summary>
    /// 전체 경로: MatchSession(권위) → LoopbackTransport → TichuClient → TableViewModel.
    /// 좌석 0 은 원격이고 "사람 대신" 서버가 보낸 합법수 중 첫 수를 자동 제출한다.
    /// </summary>
    [TestFixture]
    public class TichuClientTests
    {
        private const int MySeat = 0;

        [Test]
        public void Client_drives_a_full_match_through_the_protocol()
        {
            var transport = new LoopbackTransport();
            var vm = new TableViewModel(MySeat);
            MatchSummary summary = null;
            using (var client = new TichuClient(transport, vm, MySeat, s => summary = s))
            {
                var agents = new IAgent[]
                {
                    null,                                   // 좌석 0 = 원격(이 테스트가 대신 낸다)
                    new AiAgent(1UL, 1), new AiAgent(1UL, 2), new AiAgent(1UL, 3),
                };
                var session = new MatchSession(agents, transport, masterSeed: 9090UL, targetScore: 1);
                session.Start();

                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
                while (summary == null && DateTime.UtcNow < deadline)
                {
                    client.Pump();
                    AnswerIfAsked(vm);
                }

                session.Stop();
                session.Join(TimeSpan.FromSeconds(5));
            }

            Assert.That(summary, Is.Not.Null, "매치가 끝나고 요약이 올라와야 한다");
            Assert.That(vm.CumulativeA.CurrentValue + vm.CumulativeB.CurrentValue, Is.Not.EqualTo(0),
                "누적 점수가 RoundScored 로 채워져야 한다");
        }

        [Test]
        public void View_model_never_sees_another_seats_hand_over_the_whole_match()
        {
            var transport = new LoopbackTransport();
            var vm = new TableViewModel(MySeat);
            using (var client = new TichuClient(transport, vm, MySeat))
            {
                var agents = new IAgent[] { null, new AiAgent(1UL, 1), new AiAgent(1UL, 2), new AiAgent(1UL, 3) };
                var session = new MatchSession(agents, transport, masterSeed: 1234UL, targetScore: 1);
                session.Start();

                int maxHand = 0;
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
                while (!client.MatchEnded && DateTime.UtcNow < deadline)
                {
                    client.Pump();
                    AnswerIfAsked(vm);
                    if (vm.MyHand.CurrentValue.Count > maxHand) maxHand = vm.MyHand.CurrentValue.Count;
                }

                session.Stop();
                session.Join(TimeSpan.FromSeconds(5));

                // 내 손패는 어떤 순간에도 14장을 넘을 수 없다 — 넘으면 남의 카드가 섞인 것이다.
                Assert.That(maxHand, Is.LessThanOrEqualTo(14), $"내 손패가 {maxHand}장까지 보였다");
                Assert.That(maxHand, Is.GreaterThan(0), "손패를 한 번도 못 받았다 — 경로가 돌지 않았다");
            }
        }

        /// <summary>프롬프트가 내게 오면 합법수 중 첫 수(없으면 패스)를 낸다.</summary>
        private static void AnswerIfAsked(TableViewModel vm)
        {
            var aw = vm.Prompt.CurrentValue;
            if (aw == null) return;

            switch (aw.Kind)
            {
                case Tichu.GameFlow.StepKind.GrandTichu:
                    vm.SubmitGrandTichu(false);
                    break;
                case Tichu.GameFlow.StepKind.Exchange:
                    var h = vm.MyHand.CurrentValue;
                    if (h.Count >= 3)
                        vm.SubmitExchange(new ExchangeChoice(h[0], h[1], h[2]));
                    break;
                case Tichu.GameFlow.StepKind.Play:
                    if (aw.LegalMoves != null && aw.LegalMoves.Count > 0)
                        vm.SubmitPlay(new List<Tichu.Core.Cards.Card>(aw.LegalMoves[0].Cards));
                    else if (aw.CanPass)
                        vm.SubmitPass();
                    break;
                case Tichu.GameFlow.StepKind.DragonGift:
                    vm.SubmitDragonRecipient(1);
                    break;
            }
        }
    }
}
```

⚠️ 이 테스트는 바쁜 폴링 루프다(세션이 다른 스레드에서 돈다). 유니티 EditMode 에서 60초 상한 안에 끝나야 한다 — `targetScore: 1` 로 한 라운드만 돌린다. 끝나지 않으면 AI 가 사람 좌석을 기다리는 지점이 있다는 뜻이므로 **멈추고 보고한다**(무한 루프를 늘리지 말 것).

- [ ] **Step 7: `RuntimeTableView` 의 타입을 교체한다**

아래 13곳을 고친다. 다른 줄은 건드리지 않는다.

1. `using` 에 `Tichu.Net;` 추가.
2. **63행** `private DecisionRequest _activeReq;` → `private Awaiting _activeAw;`
3. **300~302행** `_vm.Played` 구독 세 개는 그대로 — 람다 인자 타입이 `AppliedAction` 으로 바뀐다. `AddPlayEntry`·`ShowDogIfPlayed`·`SfxMap.For` 의 매개변수를 `AppliedAction` 으로 바꾼다.
4. **304행** `_vm.CurrentTrick.Subscribe(RenderTrick)` — `RenderTrick(Trick? trick)` → `RenderTrick(TrickView trick)`. 본문의 `trick?.Top`·`trick.Top.Cards`·`trick.Top.Type`·`trick.TopOwnerSeat` 는 **그대로 동작한다**(Task 2 가 `Top` 을 조합으로 실었다).
5. **321행** `_vm.PendingDecision.Subscribe(RenderPrompt)` → `_vm.Prompt.Subscribe(RenderPrompt)`
6. **375~376행**
   ```csharp
   private bool TurnLike => _activeAw != null && _activeAw.Kind == StepKind.Play;
   private bool Exchanging => _activeAw != null && _activeAw.Kind == StepKind.Exchange;
   ```
   (`DecisionKind.Bomb` 은 사라진다 — 서버가 폭탄을 묻지 않는다.)
7. **381행** `_vm.CurrentTrick.CurrentValue?.Top != null` — 그대로.
8. **543행** `RenderPrompt(DecisionRequest? req)` → `RenderPrompt(Awaiting aw)`, 본문 첫 줄 `_activeAw = aw;`
9. **565~600행** `switch (req.Kind)` → `switch (aw.Kind)` 이고 분기를 `StepKind` 로 바꾼다:
   - `DecisionKind.GrandTichu` → `StepKind.GrandTichu` (내용 동일)
   - `DecisionKind.Tichu` 분기 **삭제** — 서버가 묻지 않는다. 작은 티츄는 상시 버튼(193행 `DeclareSmallTichu`)만 남는다.
   - `DecisionKind.Turn` → `StepKind.Play`. `bool canPass = aw.CanPass;`, 패스 버튼은 `() => _vm.SubmitPass()`.
   - `DecisionKind.Bomb` 분기 **삭제** — 차례밖 폭탄은 `RenderBombInterrupt` 경로만 쓴다.
   - `DecisionKind.DragonRecipient` → `StepKind.DragonGift`. 좌석은 내 좌석에서 유도한다:
     ```csharp
     // 게임 LeftSeat=시각 오른쪽, RightSeat=시각 왼쪽. 버튼 위치=실제 대상 일치.
     int rightPlayer = (aw.Seat + 1) % 4, leftPlayer = (aw.Seat + 3) % 4;
     ```
   - `DecisionKind.Exchange` → `StepKind.Exchange` (내용 동일)
10. **649행** `_vm.SubmitTurnDecision(TurnDecision.Play(move, wish))` → `_vm.SubmitPlay(move.Cards, wish)`
11. **663행** `_vm.SubmitTurnDecision(TurnDecision.Play(sel))` → `_vm.SubmitPlay(sel.Cards)`
12. **670행** `_vm.SubmitBomb(sel)` → `_vm.SubmitPlay(sel.Cards)` — 차례밖 폭탄도 같은 입구다.
13. **701행** `_vm.ReserveBomb(bomb)` → `_vm.SubmitPlay(bomb.Cards)`
14. **711~722행** — 권위 상태 참조를 걷어내는 핵심:
    ```csharp
    var trick = _vm.CurrentTrick.CurrentValue;
    TrickContext tc;
    if (trick == null || trick.Top == null) tc = TrickContext.Lead;
    else { bool single = trick.Top.Type == CombinationType.Single; tc = new TrickContext(false, single, single ? trick.Top.Rank : 0); }
    ```
    그리고 `var lm = _activeAw.LegalMoves;`
15. `FormatAction` 의 매개변수를 `AppliedAction` 으로 바꾼다. `a.Kind`·`a.Seat`·`a.Cards` 는 이름이 같다. `GiveDragon` 분기가 수령 좌석을 쓰면 `a.RecipientSeat` 로 바꾼다. `Exchange` 로 떨어지는 `"?"` 폴스루는 그대로.

번호가 밀렸으면 **줄 번호가 아니라 코드 내용으로** 찾는다.

- [ ] **Step 8: `SfxMap` 에 오버로드를 추가한다**

기존 `For(GameAction)` 은 **지우지 않는다**(사용 중단 경로의 `AsyncGameDriver` 테스트가 쓴다).

두 오버로드의 본문이 같으므로 **공용 헬퍼로 뽑는다**(그대로 복제하면 리뷰 루브릭이 중복으로 잡는다). 기존 `For(GameAction)` 의 본문을 헬퍼 호출로 바꾸고 새 오버로드를 더한다:

```csharp
        public static SfxId For(GameAction a) => Classify(a.Kind, a.Cards);

        /// <summary>서버가 적용한 액션 → SfxId. For(GameAction) 과 같은 규칙이다.</summary>
        public static SfxId For(AppliedAction a) => Classify(a.Kind, a.Cards);

        /// <summary>종류와 카드만으로 판정한다 — 두 입구가 같은 규칙을 공유하는 지점.</summary>
        private static SfxId Classify(GameActionKind kind, IReadOnlyList<Card> cards)
        {
            switch (kind)
            {
                case GameActionKind.Pass:       return SfxId.Pass;
                case GameActionKind.GiveDragon: return SfxId.GiveDragon;
                case GameActionKind.Play:
                    // null/빈 가드 후에만 Recognize(NRE 방지).
                    if (cards == null || cards.Count == 0) return SfxId.CardPlay;
                    var c = CombinationRecognizer.Recognize(ToArray(cards), TrickContext.Lead);
                    return c.IsBomb ? SfxId.Bomb : SfxId.CardPlay;
                default:                        return SfxId.None;
            }
        }

        private static Card[] ToArray(IReadOnlyList<Card> cards)
        {
            var arr = new Card[cards.Count];
            for (int i = 0; i < cards.Count; i++) arr[i] = cards[i];
            return arr;
        }
```

`using System.Collections.Generic;` 와 `using Tichu.Core.Cards;` 가 필요하면 추가한다. `GameAction.Cards` 는 `IReadOnlyList<Card>?`, `AppliedAction.Cards` 는 `List<Card>?` 라 둘 다 헬퍼 인자에 그대로 들어간다. 기존 `.ToArray()`(LINQ)를 쓰고 있었으므로 `using System.Linq;` 가 남아 쓰이지 않게 되면 **내 변경이 고아로 만든 것**이라 지운다.

`using Tichu.Net;` 를 추가한다. `SfxMapTests` 에 `AppliedAction` 입력으로 Pass/Play/Bomb/GiveDragon 네 케이스를 복제해 넣는다(기존 `GameAction` 케이스는 유지).

- [ ] **Step 9: `ITableView` 구독 계약 주석을 갱신한다**

`PendingDecision` → `Prompt`, `BombReserved` 삭제를 반영한다. 계약 목록이 실제 스트림과 어긋나면 그 주석이 거짓말이 된다.

`ITableViewContractTests.cs` 의 `vm.ApplySnapshot(state)` 한 줄을 다음으로 바꾼다:

```csharp
                var state = GameEngine.NewRound(12345UL);
                Assert.DoesNotThrow(() => vm.Apply(ViewProjector.ProjectFor(state, 0)),
                    "서버 뷰를 투영하는 것이 바인딩된 뷰를 통과해야 한다");
```
`using Tichu.Net;` 추가.

- [ ] **Step 10: `RoundBootstrap` 을 세션 조립으로 바꾼다**

`Begin` 의 `RunMatchAsync(vm).Forget();` 는 그대로. `RunMatchAsync` 전체를 다음으로 교체한다:

```csharp
        /// <summary>
        /// 권위 세션을 띄우고 매 프레임 수신함을 비운다. 좌석 0 은 원격(= 이 클라이언트),
        /// 1~3 은 세션 스레드에서 도는 동기 PIMC 다. 라운드 루프·누적 점수·종료 판정은
        /// 모두 세션이 쥔다 — 클라이언트가 중복 판정하지 않는다.
        /// </summary>
        private async UniTaskVoid RunMatchAsync(TableViewModel vm)
        {
            var ct = this.GetCancellationTokenOnDestroy();
            ulong masterSeed = _args.RandomDeal ? unchecked((ulong)DateTime.UtcNow.Ticks) : _args.Seed;

            var cfg = Tichu.GameFlow.Agents.PolicyConfig.For(_args.Difficulty);
            int budgetMs = BudgetMsFor(_args.Difficulty);

            // AI 에이전트는 매치 내내 재사용한다. PimcAgent 의 라운드 상태(_passed)는 매
            // 라운드 ChooseExchange 가 덮어쓰고 그 전에 결정화가 없으므로 안전하며, _rng 는
            // 하나의 결정적 스트림으로 이어진다.
            var agents = new Tichu.GameFlow.Agents.IAgent[4];
            for (int seat = 0; seat < 4; seat++)
            {
                if (seat == _args.MySeat) { agents[seat] = null; continue; }   // 원격 = 나
                agents[seat] = new PacedPimcAgent(
                    masterSeed, seat, cfg, budgetMs, _args.AiDelayMs, () => vm.FastForward);
            }

            var transport = new LoopbackTransport();
            MatchSummary summary = null;
            using var client = new Net.TichuClient(transport, vm, _args.MySeat, s => summary = s);

            var session = new MatchSession(
                agents, transport, masterSeed,
                targetScore: _args.TargetScore,
                interRoundPause: TimeSpan.FromSeconds(3.5));   // 결과를 보여 줄 시간
            session.Start();

            try
            {
                while (summary == null && !ct.IsCancellationRequested)
                {
                    client.Pump();
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                if (summary != null) _onComplete?.Invoke(summary);
            }
            catch (OperationCanceledException) { /* 씬 종료: 정상 */ }
            catch (Exception e) { Debug.LogException(e); }
            finally
            {
                session.Stop();
                session.Join(TimeSpan.FromSeconds(2));
            }
        }
```

`using Tichu.Net;` 를 추가하고, 더 쓰지 않는 `using`(있다면)은 **내 변경이 고아로 만든 것만** 지운다. `using var` 가 안 되면 `try/finally` 로 `client.Dispose()` 를 호출한다. `GameLaunchArgs` 에 `MySeat` 이 0 이 아닐 수 있으니 좌석 0 을 하드코딩하지 않는다.

- [ ] **Step 11: 컴파일 + Presentation 전체 EditMode**

```
run_tests(mode="EditMode", test_names=["Tichu.Presentation.Tests"])
```
Expected: 기존 Presentation 테스트 전부 + 신규 PASS. ⚠️ `Tichu.Core.Tests` 어셈블리는 전체 실행하지 않는다.

깨지는 테스트가 있으면 그 테스트가 **구 API 를 쓰는지**(고쳐야 함) **진짜 회귀인지**(멈추고 보고) 먼저 가른다.

- [ ] **Step 12: 커밋**

```bash
git add Assets/_Project/Presentation
git commit -m "feat(presentation): 테이블 UI 를 세션 프로토콜 위로 재배선"
```

---

### Task 5: 사용 중단 표시 + 전체 검증 + 플레이테스트 게이트

**Files:**
- Modify: `Assets/_Project/Presentation/IHumanInputPort.cs`, `Agents/HumanAgent.cs`, `Agents/PimcDecisionAgent.cs`, `ViewModel/DecisionRequest.cs` (주석만)
- Modify: `README.md` (아키텍처 한 단락)

- [ ] **Step 1: 사용 중단을 주석으로 표시한다**

네 파일의 클래스 XML 주석 맨 앞에 다음을 넣는다(삭제하지 않는다 — 스펙 §9):

```csharp
    /// ⚠️ 라이브 경로가 아니다. 테이블 UI 는 2026-10-05 부터 MatchSession + TichuClient 를
    /// 쓴다. 이 타입은 동기↔비동기 드라이버 동일성 오라클(AsyncGameDriverTests)을 위해
    /// 남아 있다. 삭제는 그 오라클을 대체한 뒤에 별도로 한다.
```

`[Obsolete]` 속성은 **쓰지 않는다** — 오라클 테스트가 경고를 에러로 승격하는 설정에서 깨질 수 있고, 이 증분의 목적이 아니다.

- [ ] **Step 2: 미러 게이트**

```bash
pwsh tools/sync-core-to-unity.ps1 -Check
```
Expected: `Mirror is in sync.` (Task 3~5 는 유니티 전용 파일만 건드렸으므로 Task 1~2 의 미러가 그대로여야 한다)

- [ ] **Step 3: dotnet 전체**

```
dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj
```
Expected: 379 PASS / 0 FAIL

- [ ] **Step 4: 유니티 EditMode**

Presentation 전체 + Net 관련 코어 픽스처를 클래스 필터로 돌린다:
```
run_tests(mode="EditMode", test_names=["Tichu.Presentation.Tests"])
run_tests(mode="EditMode", test_names=["Tichu.Core.Tests.AppliedActionTests","Tichu.Core.Tests.ViewProjectorTests","Tichu.Core.Tests.MatchSessionOracleTests","Tichu.Core.Tests.LoopbackMatchTests","Tichu.Core.Tests.LoopbackTransportTests","Tichu.Core.Tests.RemoteAgentTests","Tichu.Core.Tests.MoveResolverTests"])
```
Expected: 전부 PASS. ⚠️ 어셈블리 전체(`Tichu.Core.Tests`)는 돌리지 않는다. `run_tests` 는 **존재하지 않는 픽스처명에 0건 매칭으로 "Passed" 를 보고하므로**, 각 호출의 실행 건수가 0 이 아닌지 확인한다(위 이름은 `core/tests/Tichu.Core.Tests/` 실제 파일에서 확인했다).

- [ ] **Step 5: README 아키텍처 갱신**

"싱글플레이는 `AsyncGameDriver` 로 돈다" 류 서술을 "싱글플레이는 루프백 전송 위의 `MatchSession` 1인 인스턴스로 돈다"로 고친다. 사실과 다른 문장만 고치고 주변을 다듬지 않는다.

- [ ] **Step 6: 커밋**

```bash
git add -A
git commit -m "docs: 비동기 경로를 오라클 전용으로 표시 + README 아키텍처 갱신"
```

- [ ] **Step 7: 사람 플레이테스트 게이트 (DoD §4)**

⚠️ **이 단계는 자동화할 수 없다. 사용자에게 넘긴다.** 다음을 확인해 달라고 요청한다:

1. 모드 선택 → 난이도 선택 → 테이블이 뜨고 손패 14장이 보인다.
2. 큰 티츄 프롬프트 → 선언/패스가 먹는다.
3. 교환 3장 배정 → 확정이 먹고, 교환 후 손패가 갱신된다.
4. 내 차례에 카드 선택 → "내기"/"패스"가 먹는다. 불법 선택은 버튼이 꺼져 있다.
5. 마작 리드 → 소원 선택 UI 가 뜨고 선택이 반영된다.
6. 상대 턴 중 폭탄 선택 → "폭탄 내기" → **AI 한 수 뒤에** 폭탄이 적용된다(설계 메모 4 — 의도된 차이).
7. 용 획득 → 왼/오른쪽 버튼이 실제 대상과 맞는다.
8. 작은 티츄 상시 버튼이 14장·미콜일 때만 보이고, 누르면 배지가 뜬다.
9. 라운드 종료 → 결과 배너가 **약 3.5초** 보이고 다음 라운드가 시작된다.
10. 목표 점수 도달 → 결과 화면으로 넘어간다.
11. 체감 속도가 이전과 같다(AI 한 수가 눈에 띄게 느려지지 않았다 — 겹침 보존 확인).

실패 항목이 있으면 그것만 고치고 재확인한다.

---

## Self-Review

**1. 스펙 커버리지**

| 스펙 절 | 태스크 |
|---|---|
| §5.1 `AppliedAction`(교환 페이로드 생략) | Task 1 |
| §5.2 `TrickView.Top` | Task 2 |
| §5.3 `PacedPimcAgent`(정정된 위치·겹침) | Task 3 |
| §6 `TichuClient` 신규 | Task 4 Step 5~6 |
| §6 `TableViewModel` → `Apply(PlayerView)` + `Submitted` | Task 4 Step 3 |
| §6 `RuntimeTableView` 타입 교체 | Task 4 Step 7 |
| §6 `RoundBootstrap` 조립 + 프레임 펌프 | Task 4 Step 10 |
| §7 교환 제출 현황 파킹 해제 | Task 4 설계 메모 8 (데이터 개방만, UI 미제작) |
| §8 테스트 | Task 1·2·3·4 의 테스트 단계 |
| §9 비동기 경로 사용 중단(삭제 금지) | Task 5 Step 1 |
| DoD ① 세션 경로 완주 | Task 4 `TichuClientTests` + Task 5 Step 7 |
| DoD ② UI 에 권위 상태 참조 0 | Task 4 Step 7 항목 14(마지막 `.Context.State` 제거) |
| DoD ③ 회귀 0 | Task 5 Step 3~4 |
| DoD ④ 사람 플레이테스트 | Task 5 Step 7 |

**2. 플레이스홀더 점검** — 추정이 남은 세 곳을 "추정"으로 명시하고 확인 방법을 적었다: `DriveToPlayPhase`(Task 3 Step 1), `IAgent` 널 표기(Task 3 Step 3), `MatchSummary` 생성자 순서(Task 4 Step 5). 발명 금지와 대체 경로를 함께 적었다.

**3. 타입 정합성** — `Prompt`(≠`PendingDecision`), `Record`(≠`RecordPlay`), `SubmitPlay`/`SubmitPass`(≠`SubmitTurnDecision`), `TrickView.Top`, `AppliedAction.RecipientSeat` 를 Task 4 의 모든 참조와 맞췄다. `Awaiting` 은 타입명과 충돌하므로 속성명을 `Prompt` 로 둔다.

**4. 알고 받아들이는 차이 두 개** — ① 차례밖 폭탄이 AI 한 수만큼 늦는다(결정 중단 토큰은 M5). ② AI 라운드별 재시드가 사라진다(`_passed` 는 매 라운드 덮어써지고 `_rng` 는 결정적 스트림이라 안전; 확인함).

---

## 실행 중 삽입된 태스크

### Task 4.5: `MoveResolver` 가 플레이어의 실제 카드를 받아들인다

Task 4 구현자가 범위 밖 회귀를 보고해 컨트롤러 룰링(R6)으로 삽입했다. `MoveResolver.ResolveMove` 가 생성기가 내보낸 카드와의 **정확 일치**를 요구하는데 `LegalMoveGenerator.GenerateSingles` 는 랭크당 한 장만 내보내므로, 사람이 같은 랭크의 다른 무늬를 클릭하면 서버가 거부한다. 구 경로는 `(Type, Rank)` 등가로 통과시키고 플레이어의 실제 카드를 적용했다.

해법은 비교를 `(Rank, Special)` 등가로 바꾸고 조합이 **클라이언트의 카드**를 싣게 하는 것이다. ⚠️ 그 완화는 보유하지 않은 카드를 낼 구멍을 열기 때문에(7♠ 만 들고 7♥ 를 보내도 랭크 다중집합은 같다) `ResolveMove` 안에서 손패 보유를 직접 검증해야 한다 — 엔진의 `HandContainsAll` 에 맡기면 드라이버가 통보 없이 버려 `RemoteAgent` 가 검증 관문이라는 M1 전제가 깨진다.

브리프: `.superpowers/sdd/2026-10-05-mp-client-rewiring/task-4.5-brief.md` (전체 코드·테스트·판별력 포함)
