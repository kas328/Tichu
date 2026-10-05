# 멀티플레이 M1 — 프로토콜 · 권위 세션 · 로컬 루프백 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 티츄 친구방 멀티플레이가 들어갈 자리를 `core/` 안에 만든다 — 좌석별 시야 마스킹, 권위 세션, 원격 좌석 에이전트, 전송 이음새와 로컬 루프백까지. 네트워크 0줄, 서버 0대, Unity 코드 0줄.

**Architecture:** 기존 동기 `GameDriver`가 권위 세션의 게임 루프가 된다. 원격 플레이어는 "네트워크에서 액션이 도착할 때까지 블로킹하는 `IAgent`"(`RemoteAgent`)로 표현되고, AI 좌석은 기존 에이전트를 그대로 쓴다. 드라이버는 누가 사람이고 누가 AI인지 모른다. 매치마다 전용 스레드 1개를 쓰고 모든 엔진 변이는 그 스레드에서만 일어난다. 매 액션 적용 직후 좌석별로 마스킹된 `PlayerView`를 방송한다.

**Tech Stack:** C# 9 / netstandard2.1 (런타임) · net9.0 + NUnit 4 (테스트) · 신규 어셈블리 `Tichu.Net` · 외부 의존성 0

**Spec:** `docs/superpowers/specs/2026-10-04-mp-m1-session-protocol-design.md`

## Global Constraints

- 런타임 프로젝트(`core/src/**`): `TargetFramework=netstandard2.1` · `LangVersion=9.0` · `Nullable=enable` · `ImplicitUsings=disable`. `using` 문을 전부 명시해야 한다.
- 테스트 프로젝트(`core/tests/Tichu.Core.Tests`): `net9.0` · `LangVersion=latest` · `ImplicitUsings=enable` · `NUnit 4.2.2`. `using NUnit.Framework`가 전역 제공되지만 기존 파일들이 명시하므로 같이 명시한다.
- **UnityEngine 타입을 단 하나도 참조하지 않는다.** `Tichu.Net.asmdef`는 `noEngineReferences: true`다.
- **정본은 `core/`**, `Assets/_Project/`는 사본이다. `core/src/**`의 `.cs`를 건드렸으면 `pwsh -File tools/sync-core-to-unity.ps1`로 미러하고 `-Check`가 `Mirror is in sync.`를 출력해야 한다. CI가 매 푸시마다 같은 비교를 한다. (`pwsh`가 없으면 `powershell -ExecutionPolicy Bypass -File ...`)
- 좌석 수는 항상 4. `GameEngine.SeatCount`는 `internal`이라 `Tichu.Net`에서 못 쓴다 — 각 클래스에 `private const int SeatCount = 4;`를 둔다(`GameState`가 쓰는 방식과 같다).
- **타 좌석의 손패 카드 데이터는 어떤 페이로드에도 넣지 않는다.** 가린 ID도 넣지 않는다 — 패킷 스니핑으로 노출되기 때문이다.
- 커밋은 한국어 conventional commit(`feat(mp):` · `chore(mp):`), 본문 끝에 `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.
- 브랜치 `feat/mp-m1-session`(이미 생성됨). 전체 회귀는 `dotnet test core/Tichu.sln -c Release --nologo`.

## 스펙 대비 정제 3건 (계획 수립 중 확정 — 스펙 §11에도 기록)

1. **`ClientAction.Play`에 `Rank`(×2 스케일) 추가.** ⚠️ **이 근거는 Task 5 구현 중 거짓으로 판명됐다** — `CombinationRecognizer.RecognizeStraight`가 끝 확장 시 상단을 우선하므로 `3,4,5,6,봉황`은 7-high 하나만 생성되고, `Recognize`는 카드 다중집합의 순수 함수라 두 합법수가 같은 카드 집합을 가질 수 없다(생성 24,000회·합법수 40,191개 실측에서 동일-다중집합 쌍 0건). 모호성 해소 분기는 제거됐고 `Rank`는 **선택적 의도 검증**(낡은 클라이언트 뷰 탐지)으로 남았다. 정정 경위는 스펙 §11.1 참조.
2. **`Awaiting`에 `LegalMoves`·`CanPass` 추가, 단 대기 좌석에게만.** 클라이언트는 `PlayerView`만 들고 있어 `LegalMoveGenerator`(전체 `GameState` 필요)를 돌릴 수 없다. 합법수는 **자기 손패와 공개 트릭 상태에서만** 유도되므로 은닉 정보가 아니다. 다른 좌석에게는 `Kind`·`Seat`만 남긴 축약판을 보낸다(차례 표시용). `LegalMoves`가 돌려주는 `Combination`은 보관해도 안전하다 — `LegalMoveGenerator.cs:218-219` 주석대로 `Recognize()`가 카드를 새 배열로 복사하므로 `ThreadStatic` 스크래치 버퍼를 참조하지 않는다.
3. **`Awaiting` 발행 시점은 `RemoteAgent`가 대기에 들어가는 순간.** 드라이버의 `grandNext`/`exchangeNext` 카운터는 세션이 볼 수 없고 `RoundSetup`은 `internal`이라 "지금 누구를 기다리는지"를 상태에서 역산할 수 없다. 묻는 주체인 에이전트가 직접 알리는 것이 정확하다.

---

### Task 1: `Tichu.Net` 어셈블리 골격과 배선

**Files:**
- Create: `core/src/Tichu.Net/Tichu.Net.csproj`
- Create: `core/src/Tichu.Net/AssemblyMarker.cs`
- Create: `Assets/_Project/Net/Tichu.Net.asmdef`
- Modify: `core/Tichu.sln` · `core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj` (둘 다 dotnet CLI로)
- Modify: `Assets/_Project/Tests/EditMode/Tichu.Core.Tests.asmdef` · `tools/sync-core-to-unity.ps1` · `.github/workflows/core-tests.yml`
- Test: `core/tests/Tichu.Core.Tests/NetScaffoldTests.cs`

**Interfaces:**
- Consumes: 없음(첫 태스크).
- Produces: `Tichu.Net.AssemblyMarker.Name == "Tichu.Net"`. 이후 모든 런타임 코드는 `core/src/Tichu.Net/` 아래, 테스트는 `core/tests/Tichu.Core.Tests/` 아래에 들어간다.

- [ ] **Step 1: 실패하는 테스트 작성**

`core/tests/Tichu.Core.Tests/NetScaffoldTests.cs` (기존 `GameFlowScaffoldTests.cs`와 같은 패턴):

```csharp
using NUnit.Framework;

namespace Tichu.Core.Tests
{
    [TestFixture]
    public class NetScaffoldTests
    {
        [Test]
        public void Assembly_marker_is_wired()
        {
            Assert.That(Tichu.Net.AssemblyMarker.Name, Is.EqualTo("Tichu.Net"));
        }
    }
}
```

- [ ] **Step 2: 컴파일 실패 확인**

Run: `dotnet test core/Tichu.sln -c Release --nologo --filter "FullyQualifiedName~NetScaffoldTests"`
Expected: 빌드 실패 — `CS0234: The type or namespace name 'Net' does not exist in the namespace 'Tichu'`

- [ ] **Step 3: 프로젝트와 마커 생성**

`core/src/Tichu.Net/Tichu.Net.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <RootNamespace>Tichu.Net</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Tichu.Core\Tichu.Core.csproj" />
    <ProjectReference Include="..\Tichu.GameFlow\Tichu.GameFlow.csproj" />
  </ItemGroup>
</Project>
```

`core/src/Tichu.Net/AssemblyMarker.cs`:

```csharp
namespace Tichu.Net { public static class AssemblyMarker { public const string Name = "Tichu.Net"; } }
```

- [ ] **Step 4: 솔루션·테스트 프로젝트에 연결**

`.sln`의 GUID를 손으로 쓰지 말고 CLI를 쓴다:

```bash
dotnet sln core/Tichu.sln add core/src/Tichu.Net/Tichu.Net.csproj
dotnet add core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj reference core/src/Tichu.Net/Tichu.Net.csproj
```

- [ ] **Step 5: 테스트 통과 확인**

Run: `dotnet test core/Tichu.sln -c Release --nologo --filter "FullyQualifiedName~NetScaffoldTests"`
Expected: PASS (1 test)

- [ ] **Step 6: 유니티 미러 쪽 어셈블리 정의**

`Assets/_Project/Net/Tichu.Net.asmdef`:

```json
{
    "name": "Tichu.Net",
    "rootNamespace": "Tichu.Net",
    "references": [
        "Tichu.Core",
        "Tichu.GameFlow"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": true
}
```

`Assets/_Project/Tests/EditMode/Tichu.Core.Tests.asmdef`의 `references` 배열을 다음으로 바꾼다(미러된 테스트가 유니티 EditMode에서도 컴파일되게):

```json
    "references": [
        "Tichu.Core",
        "Tichu.GameFlow",
        "Tichu.Net",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
```

`.meta` 파일은 유니티 에디터가 임포트할 때 생성한다 — 손으로 만들지 않는다. 이 태스크의 검증은 전부 `dotnet`에서 끝나고, 유니티 EditMode 확인은 에디터를 열 수 있을 때 한다.

- [ ] **Step 7: 미러 스크립트와 CI에 새 경로 등록**

`tools/sync-core-to-unity.ps1`의 `$mappings` 배열에서 `Tichu.GameFlow` 항목 **뒤**, 테스트 항목 **앞**에 추가:

```powershell
    @{
        Src     = Path @("core", "src", "Tichu.Net")
        Dest    = Path @("Assets", "_Project", "Net")
        Exclude = @()
    },
```

`.github/workflows/core-tests.yml`의 `mirror` 잡에서 `for pair in ...` 한 줄을 다음으로 바꾼다:

```yaml
          for pair in "core/src/Tichu.Core:Assets/_Project/Core" "core/src/Tichu.GameFlow:Assets/_Project/GameFlow" "core/src/Tichu.Net:Assets/_Project/Net"; do
```

- [ ] **Step 8: 미러 실행 + 동기 확인 + 전체 회귀**

```bash
pwsh -File tools/sync-core-to-unity.ps1
pwsh -File tools/sync-core-to-unity.ps1 -Check
dotnet test core/Tichu.sln -c Release --nologo
```
Expected: `[COPIED] AssemblyMarker.cs` → `Mirror is in sync.` → 전체 PASS(기존 수 + 1)

- [ ] **Step 9: 커밋**

```bash
git add core/src/Tichu.Net core/Tichu.sln core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj core/tests/Tichu.Core.Tests/NetScaffoldTests.cs Assets/_Project/Net Assets/_Project/Tests/EditMode/Tichu.Core.Tests.asmdef tools/sync-core-to-unity.ps1 .github/workflows/core-tests.yml
git commit -m "chore(mp): Tichu.Net 어셈블리 골격 + 미러·CI 배선

프로토콜·권위 세션·전송 이음새가 들어갈 새 어셈블리. 유니티 무참조라
서버(dotnet)와 클라이언트(Unity)가 같은 코드를 쓴다.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: `PlayerView`와 `ViewProjector` — 좌석별 시야 마스킹

**Files:**
- Create: `core/src/Tichu.Net/PlayerView.cs` · `core/src/Tichu.Net/ViewProjector.cs`
- Test: `core/tests/Tichu.Core.Tests/ViewProjectorTests.cs`

**Interfaces:**
- Consumes: Task 1의 `Tichu.Net` 어셈블리.
- Produces:
  - `Tichu.Net.PlayerView` — 아래 코드의 프로퍼티 전체(`Viewer` · `Phase` · `Turn` · `Wish` · `DragonGiftWinner` · `MyHand` · `HandCounts` · `Calls` · `IsOut` · `FinishOrder` · `WonCards` · `CurrentTrick` · `CompletedTricks` · `TeamAScore` · `TeamBScore` · `TargetScore`).
  - `Tichu.Net.TrickView` · `Tichu.Net.PlayView` · `Tichu.Net.TrickSummary`.
  - `Tichu.Net.ViewProjector.ProjectFor(Tichu.Core.Game.GameState s, int viewer) → PlayerView`
  - `PlayerView.Awaiting` 프로퍼티는 **Task 5**에서 추가한다. 이 태스크에서는 만들지 않는다.

- [ ] **Step 1: 실패하는 테스트 작성**

`core/tests/Tichu.Core.Tests/ViewProjectorTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Game;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// ViewProjector — 좌석별 시야 투영. 핵심 불변식은 "타 좌석 손패 카드가 뷰 어디에도
    /// 없다"이며, 가린 ID조차 넣지 않는다(패킷 스니핑 방지, 기획서 7.5).
    /// </summary>
    public class ViewProjectorTests
    {
        /// <summary>큰 티츄 4연속 패스 + 교환 4연속 제출로 Play 페이즈 상태를 만든다.</summary>
        private static GameState PlayPhaseState(ulong seed)
        {
            var s = GameEngine.NewRound(seed);
            for (int seat = 0; seat < 4; seat++)
                Assert.That(GameEngine.Apply(s, GameAction.DeclineGrandTichu(seat)).Ok, Is.True,
                    $"테스트 전제: seat {seat} 큰 티츄 패스 적용");

            Assert.That(s.Phase, Is.EqualTo(RoundPhase.Exchange), "테스트 전제: 교환 페이즈 진입");
            for (int seat = 0; seat < 4; seat++)
            {
                var h = s.Seats[seat].Hand;
                Assert.That(GameEngine.Apply(s, GameAction.Exchange(seat,
                    new[] { h[0] }, new[] { h[1] }, new[] { h[2] })).Ok, Is.True,
                    $"테스트 전제: seat {seat} 교환 제출 적용");
            }

            Assert.That(s.Phase, Is.EqualTo(RoundPhase.Play), "테스트 전제: 플레이 페이즈 진입");
            return s;
        }

        /// <summary>뷰에 등장하는 모든 카드를 모은다.</summary>
        private static List<Card> AllCardsIn(PlayerView v)
        {
            var all = new List<Card>(v.MyHand);
            for (int i = 0; i < 4; i++) all.AddRange(v.WonCards[i]);
            if (v.CurrentTrick != null)
            {
                all.AddRange(v.CurrentTrick.TopCards);
                foreach (var p in v.CurrentTrick.History) all.AddRange(p.Cards);
            }
            return all;
        }

        [Test]
        public void No_other_seat_hand_card_appears_anywhere_in_the_view()
        {
            var s = PlayPhaseState(42UL);

            for (int viewer = 0; viewer < 4; viewer++)
            {
                var visible = AllCardsIn(ViewProjector.ProjectFor(s, viewer));
                for (int other = 0; other < 4; other++)
                {
                    if (other == viewer) continue;
                    foreach (var card in s.Seats[other].Hand)
                        Assert.That(visible, Does.Not.Contain(card),
                            $"viewer {viewer} 가 seat {other} 의 손패 {card} 를 볼 수 있다");
                }
            }
        }

        [Test]
        public void Own_hand_is_fully_visible_and_counts_are_public()
        {
            var s = PlayPhaseState(7UL);
            var v = ViewProjector.ProjectFor(s, 1);

            Assert.That(v.Viewer, Is.EqualTo(1));
            Assert.That(v.MyHand, Is.EquivalentTo(s.Seats[1].Hand));
            for (int i = 0; i < 4; i++)
                Assert.That(v.HandCounts[i], Is.EqualTo(s.Seats[i].Hand.Count), $"seat {i} 장수");
        }

        [Test]
        public void Projection_is_a_value_snapshot_not_a_live_reference()
        {
            var s = PlayPhaseState(9UL);
            var v = ViewProjector.ProjectFor(s, 0);

            int handCountBefore = v.MyHand.Count;
            int turnBefore = v.Turn;

            s.Seats[0].Hand.Clear();
            s.Turn = (s.Turn + 1) % 4;

            Assert.That(v.MyHand.Count, Is.EqualTo(handCountBefore), "투영 후 손패 변경이 뷰에 샜다");
            Assert.That(v.Turn, Is.EqualTo(turnBefore), "투영 후 차례 변경이 뷰에 샜다");
        }

        [Test]
        public void Public_facts_are_carried_through()
        {
            var s = PlayPhaseState(11UL);
            s.Wish = 7;
            s.Seats[2].Call = TichuCall.GrandTichu;
            s.Seats[3].IsOut = true;
            s.Seats[3].FinishOrder = 1;
            s.Scores.TeamA = 120;
            s.Scores.TeamB = -20;

            var v = ViewProjector.ProjectFor(s, 0);

            Assert.That(v.Wish, Is.EqualTo(7));
            Assert.That(v.Calls[2], Is.EqualTo(TichuCall.GrandTichu));
            Assert.That(v.IsOut[3], Is.True);
            Assert.That(v.FinishOrder[3], Is.EqualTo(1));
            Assert.That(v.TeamAScore, Is.EqualTo(120));
            Assert.That(v.TeamBScore, Is.EqualTo(-20));
            Assert.That(v.TargetScore, Is.EqualTo(s.Scores.TargetScore));
            Assert.That(v.Phase, Is.EqualTo(RoundPhase.Play));
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test core/Tichu.sln -c Release --nologo --filter "FullyQualifiedName~ViewProjectorTests"`
Expected: 빌드 실패 — `CS0246: The type or namespace name 'PlayerView' could not be found`

- [ ] **Step 3: `PlayerView` 작성**

`core/src/Tichu.Net/PlayerView.cs`:

```csharp
using System.Collections.Generic;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;

namespace Tichu.Net
{
    /// <summary>회수된 트릭의 공개 요약. 카드 데이터는 WonCards 로 이미 공개된다.</summary>
    public sealed class TrickSummary
    {
        public int TopOwnerSeat { get; set; }
        public int AccumulatedPoints { get; set; }
        public bool WonByDragon { get; set; }
        public int? DragonGiftRecipient { get; set; }
    }

    /// <summary>트릭 내 한 번의 제출. IsPass 면 Cards 는 비어 있다.</summary>
    public sealed class PlayView
    {
        public int Seat { get; set; }
        public bool IsPass { get; set; }
        public bool IsBombInterrupt { get; set; }
        public List<Card> Cards { get; set; } = new List<Card>();
    }

    /// <summary>진행 중 트릭의 공개 상태. 전원에게 동일하다.</summary>
    public sealed class TrickView
    {
        public CombinationType LeadType { get; set; }
        public int LeadLength { get; set; }
        public CombinationType TopType { get; set; }
        public int TopRank { get; set; }
        public int TopLength { get; set; }
        public List<Card> TopCards { get; set; } = new List<Card>();
        public int TopOwnerSeat { get; set; }
        public int AccumulatedPoints { get; set; }
        public List<PlayView> History { get; set; } = new List<PlayView>();
    }

    /// <summary>
    /// 한 좌석에게 보내는 게임 상태. 기획서 7.5 시야 마스킹 표에 1:1 대응한다.
    /// 자기 손패만 전체 공개하고 상대는 장수만 싣는다 — 타 좌석 카드 데이터는
    /// 가린 형태로도 포함하지 않는다. GameState 를 참조로 들고 있지 않은 값 스냅샷이다.
    /// </summary>
    public sealed class PlayerView
    {
        /// <summary>이 뷰의 주인 좌석.</summary>
        public int Viewer { get; set; }

        public RoundPhase Phase { get; set; }
        public int Turn { get; set; }
        public int? Wish { get; set; }

        /// <summary>용 양도를 지정해야 하는 좌석. 대기 중이 아니면 null.</summary>
        public int? DragonGiftWinner { get; set; }

        /// <summary>자기 손패 — 전체.</summary>
        public List<Card> MyHand { get; set; } = new List<Card>();

        /// <summary>전 좌석 손패 장수. 상대에 대해 공개되는 유일한 손패 정보다.</summary>
        public int[] HandCounts { get; set; } = new int[4];

        public TichuCall[] Calls { get; set; } = new TichuCall[4];
        public bool[] IsOut { get; set; } = new bool[4];
        public int[] FinishOrder { get; set; } = new int[4];

        /// <summary>전 좌석 획득 더미 — 이미 공개된 카드라 전체를 싣는다(카드 카운팅 근거).</summary>
        public List<Card>[] WonCards { get; set; } = new List<Card>[4];

        public TrickView? CurrentTrick { get; set; }
        public List<TrickSummary> CompletedTricks { get; set; } = new List<TrickSummary>();

        public int TeamAScore { get; set; }
        public int TeamBScore { get; set; }
        public int TargetScore { get; set; }
    }
}
```

- [ ] **Step 4: `ViewProjector` 작성**

`core/src/Tichu.Net/ViewProjector.cs`:

```csharp
using System.Collections.Generic;
using Tichu.Core.Cards;
using Tichu.Core.Game;

namespace Tichu.Net
{
    /// <summary>
    /// 권위 GameState 를 한 좌석의 시야로 투영한다. 서버만 호출한다.
    /// 반환값은 깊은 복사된 값 스냅샷이라, 이후 서버 상태 변경이 이미 보낸 뷰에 새지 않는다
    /// (로컬 루프백에서 참조 공유로 마스킹이 무력화되는 경로를 막는다).
    /// </summary>
    public static class ViewProjector
    {
        private const int SeatCount = 4;

        public static PlayerView ProjectFor(GameState s, int viewer)
        {
            var v = new PlayerView
            {
                Viewer = viewer,
                Phase = s.Phase,
                Turn = s.Turn,
                Wish = s.Wish,
                TeamAScore = s.Scores.TeamA,
                TeamBScore = s.Scores.TeamB,
                TargetScore = s.Scores.TargetScore,
                DragonGiftWinner = s.TryGetPendingDragonGift(out int w) ? w : (int?)null,
                MyHand = new List<Card>(s.Seats[viewer].Hand),
            };

            for (int i = 0; i < SeatCount; i++)
            {
                var seat = s.Seats[i];
                v.HandCounts[i] = seat.Hand.Count;
                v.Calls[i] = seat.Call;
                v.IsOut[i] = seat.IsOut;
                v.FinishOrder[i] = seat.FinishOrder;
                v.WonCards[i] = new List<Card>(seat.WonCards);
            }

            v.CurrentTrick = s.CurrentTrick == null ? null : ProjectTrick(s.CurrentTrick);

            v.CompletedTricks = new List<TrickSummary>(s.CompletedTricks.Count);
            foreach (var t in s.CompletedTricks)
                v.CompletedTricks.Add(new TrickSummary
                {
                    TopOwnerSeat = t.TopOwnerSeat,
                    AccumulatedPoints = t.AccumulatedPoints,
                    WonByDragon = t.WonByDragon,
                    DragonGiftRecipient = t.DragonGiftRecipient,
                });

            return v;
        }

        private static TrickView ProjectTrick(Trick t)
        {
            var tv = new TrickView
            {
                LeadType = t.LeadType,
                LeadLength = t.LeadLength,
                TopOwnerSeat = t.TopOwnerSeat,
                AccumulatedPoints = t.AccumulatedPoints,
            };

            if (t.Top != null)
            {
                tv.TopType = t.Top.Type;
                tv.TopRank = t.Top.Rank;
                tv.TopLength = t.Top.Length;
                tv.TopCards = new List<Card>(t.Top.Cards);
            }

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
    }
}
```

- [ ] **Step 5: 테스트 통과 확인**

Run: `dotnet test core/Tichu.sln -c Release --nologo --filter "FullyQualifiedName~ViewProjectorTests"`
Expected: PASS (4 tests)

- [ ] **Step 6: 미러 + 전체 회귀 + 커밋**

```bash
pwsh -File tools/sync-core-to-unity.ps1 && pwsh -File tools/sync-core-to-unity.ps1 -Check
dotnet test core/Tichu.sln -c Release --nologo
git add core/src/Tichu.Net Assets/_Project/Net core/tests/Tichu.Core.Tests/ViewProjectorTests.cs
git commit -m "feat(mp): PlayerView · ViewProjector — 좌석별 시야 마스킹

기획서 7.5 표에 1:1 대응하는 투영. 자기 손패만 전체, 상대는 장수만
싣고 타 좌석 카드 데이터는 가린 형태로도 넣지 않는다.

깊은 복사된 값 스냅샷이라 투영 이후 서버 상태 변경이 이미 보낸 뷰에
새지 않는다 — 루프백에서 참조 공유로 마스킹이 무력화되는 경로를
테스트로 막았다.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: 프로토콜 DTO와 전송 이음새

**Files:**
- Create: `core/src/Tichu.Net/Protocol/ClientAction.cs` · `core/src/Tichu.Net/Protocol/ServerEvent.cs` · `core/src/Tichu.Net/ITichuTransport.cs` · `core/src/Tichu.Net/LoopbackTransport.cs`
- Test: `core/tests/Tichu.Core.Tests/LoopbackTransportTests.cs`

**Interfaces:**
- Consumes: Task 2의 `PlayerView`.
- Produces:
  - `Tichu.Net.ClientActionKind { CallGrandTichu, DeclineGrandTichu, Exchange, CallTichu, Play, Pass, GiveDragon }`
  - `Tichu.Net.ClientAction` — 프로퍼티 `Kind` · `Seat`(int) · `ActionSeq`(long) · `Cards`(`List<Card>?`) · `Rank`(int) · `Wish`(int?) · `ToLeft`/`ToPartner`/`ToRight`(Card) · `RecipientSeat`(int). 정적 팩토리: `CallGrandTichu(int,long)` · `DeclineGrandTichu(int,long)` · `Exchange(int,long,Card,Card,Card)` · `CallTichu(int,long)` · `Play(int,long,List<Card>,int,int?)` · `Pass(int,long)` · `GiveDragon(int,long,int)`.
  - `Tichu.Net.ServerEventKind { StateUpdate, ActionRejected, RoundScored, MatchEnded, SessionFaulted }`
  - `Tichu.Net.ServerEvent` — 프로퍼티 `Kind` · `View`(`PlayerView?`) · `Reason`(string?) · `ActionSeq`(long) · `Round`(`RoundResult?`) · `WinningTeam`/`TeamA`/`TeamB`(int). 정적 팩토리: `StateUpdate(PlayerView)` · `ActionRejected(long,string)` · `RoundScored(RoundResult)` · `MatchEnded(int,int,int)` · `SessionFaulted(string)`.
  - `Tichu.Net.ITichuTransport { void SendTo(int seat, ServerEvent ev); void OnClientAction(System.Action<ClientAction> handler); }`
  - `Tichu.Net.LoopbackTransport : ITichuTransport` — 추가로 `IReadOnlyList<ServerEvent> Inbox(int seat)`(검증용 스냅샷) · `IReadOnlyList<ServerEvent> Drain(int seat)`(폴링용, 수신함을 비운다) · `void Submit(ClientAction a)`.

- [ ] **Step 1: 실패하는 테스트 작성**

`core/tests/Tichu.Core.Tests/LoopbackTransportTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// LoopbackTransport — 같은 프로세스 전송. 좌석별 수신함이 섞이지 않는 것과
    /// 클라이언트 제출이 세션 핸들러에 닿는 것을 검증한다.
    /// </summary>
    public class LoopbackTransportTests
    {
        [Test]
        public void SendTo_delivers_only_to_the_target_seat()
        {
            var t = new LoopbackTransport();
            t.SendTo(2, ServerEvent.ActionRejected(7L, "nope"));

            Assert.That(t.Inbox(2).Count, Is.EqualTo(1));
            Assert.That(t.Inbox(2)[0].Kind, Is.EqualTo(ServerEventKind.ActionRejected));
            Assert.That(t.Inbox(2)[0].Reason, Is.EqualTo("nope"));
            Assert.That(t.Inbox(2)[0].ActionSeq, Is.EqualTo(7L));

            foreach (int other in new[] { 0, 1, 3 })
                Assert.That(t.Inbox(other).Count, Is.EqualTo(0), $"seat {other} 수신함이 오염됐다");
        }

        [Test]
        public void Submit_reaches_the_registered_handler()
        {
            var t = new LoopbackTransport();
            var seen = new List<ClientAction>();
            t.OnClientAction(a => seen.Add(a));

            t.Submit(ClientAction.Pass(seat: 1, actionSeq: 1L));

            Assert.That(seen.Count, Is.EqualTo(1));
            Assert.That(seen[0].Kind, Is.EqualTo(ClientActionKind.Pass));
            Assert.That(seen[0].Seat, Is.EqualTo(1));
            Assert.That(seen[0].ActionSeq, Is.EqualTo(1L));
        }

        [Test]
        public void Drain_takes_events_once_and_empties_the_inbox()
        {
            var t = new LoopbackTransport();
            t.SendTo(0, ServerEvent.ActionRejected(1L, "a"));
            t.SendTo(0, ServerEvent.ActionRejected(2L, "b"));

            Assert.That(t.Drain(0).Count, Is.EqualTo(2));
            Assert.That(t.Drain(0).Count, Is.EqualTo(0), "두 번째 Drain 은 비어야 한다");
            Assert.That(t.Inbox(0).Count, Is.EqualTo(0));
        }

        [Test]
        public void Submit_without_handler_is_a_no_op()
        {
            var t = new LoopbackTransport();
            Assert.DoesNotThrow(() => t.Submit(ClientAction.Pass(seat: 0, actionSeq: 1L)));
        }

        [Test]
        public void Play_action_carries_cards_and_rank()
        {
            var cards = new List<Card> { Card.Normal(5, Suit.Jade) };
            var a = ClientAction.Play(seat: 3, actionSeq: 4L, cards: cards, rank: 10, wish: null);

            Assert.That(a.Kind, Is.EqualTo(ClientActionKind.Play));
            Assert.That(a.Cards, Is.EquivalentTo(cards));
            Assert.That(a.Rank, Is.EqualTo(10));
            Assert.That(a.Wish, Is.Null);
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test core/Tichu.sln -c Release --nologo --filter "FullyQualifiedName~LoopbackTransportTests"`
Expected: 빌드 실패 — `CS0246: The type or namespace name 'LoopbackTransport' could not be found`

- [ ] **Step 3: `ClientAction` 작성**

`core/src/Tichu.Net/Protocol/ClientAction.cs`:

```csharp
using System.Collections.Generic;
using Tichu.Core.Cards;

namespace Tichu.Net
{
    /// <summary>클라이언트 → 서버 액션 종류. Tichu.Core.Game.GameActionKind 와 1:1 대응한다.</summary>
    public enum ClientActionKind
    {
        CallGrandTichu,
        DeclineGrandTichu,
        Exchange,
        CallTichu,
        Play,
        Pass,
        GiveDragon
    }

    /// <summary>
    /// 클라이언트가 서버에 보내는 요청. 서버는 이걸 그대로 적용하지 않고 합법성을
    /// 재검증한 뒤 Tichu.Core.Game.GameAction 으로 변환해 적용한다.
    ///
    /// ActionSeq 는 좌석별 단조 증가 번호다. 중복·역행은 상태를 바꾸지 않고 거부한다
    /// (리플레이 방지). 중복 수신 시 현재 상태를 재전송하는 처리는 M2 몫이다.
    /// </summary>
    public sealed class ClientAction
    {
        public ClientActionKind Kind { get; set; }
        public int Seat { get; set; }
        public long ActionSeq { get; set; }

        /// <summary>Play: 제출 카드.</summary>
        public List<Card>? Cards { get; set; }

        /// <summary>
        /// Play: 클라이언트가 의도한 조합의 ×2 스케일 Rank. 0 = 미지정.
        /// 카드 집합이 일치하는 합법수의 Rank 와 다르면 클라이언트 뷰가 낡았다는 뜻이므로
        /// 적용하지 않는다 — 선택적 의도 검증이며, 모호성 해소용이 아니다(스펙 §11.1).
        /// </summary>
        public int Rank { get; set; }

        /// <summary>Play: 마작 리드 시 지정하는 소원 랭크.</summary>
        public int? Wish { get; set; }

        /// <summary>Exchange: 왼쪽/파트너/오른쪽에 보낼 카드.</summary>
        public Card ToLeft { get; set; }
        public Card ToPartner { get; set; }
        public Card ToRight { get; set; }

        /// <summary>GiveDragon: 트릭 점수를 양도받을 상대 좌석.</summary>
        public int RecipientSeat { get; set; }

        public static ClientAction CallGrandTichu(int seat, long actionSeq) =>
            new ClientAction { Kind = ClientActionKind.CallGrandTichu, Seat = seat, ActionSeq = actionSeq };

        public static ClientAction DeclineGrandTichu(int seat, long actionSeq) =>
            new ClientAction { Kind = ClientActionKind.DeclineGrandTichu, Seat = seat, ActionSeq = actionSeq };

        public static ClientAction Exchange(int seat, long actionSeq, Card toLeft, Card toPartner, Card toRight) =>
            new ClientAction
            {
                Kind = ClientActionKind.Exchange, Seat = seat, ActionSeq = actionSeq,
                ToLeft = toLeft, ToPartner = toPartner, ToRight = toRight
            };

        public static ClientAction CallTichu(int seat, long actionSeq) =>
            new ClientAction { Kind = ClientActionKind.CallTichu, Seat = seat, ActionSeq = actionSeq };

        public static ClientAction Play(int seat, long actionSeq, List<Card> cards, int rank, int? wish) =>
            new ClientAction
            {
                Kind = ClientActionKind.Play, Seat = seat, ActionSeq = actionSeq,
                Cards = cards, Rank = rank, Wish = wish
            };

        public static ClientAction Pass(int seat, long actionSeq) =>
            new ClientAction { Kind = ClientActionKind.Pass, Seat = seat, ActionSeq = actionSeq };

        public static ClientAction GiveDragon(int seat, long actionSeq, int recipientSeat) =>
            new ClientAction
            {
                Kind = ClientActionKind.GiveDragon, Seat = seat, ActionSeq = actionSeq,
                RecipientSeat = recipientSeat
            };
    }
}
```

- [ ] **Step 4: `ServerEvent` 작성**

`core/src/Tichu.Net/Protocol/ServerEvent.cs`:

```csharp
using Tichu.Core.Game;

namespace Tichu.Net
{
    /// <summary>서버 → 클라이언트 이벤트 종류.</summary>
    public enum ServerEventKind
    {
        StateUpdate,
        ActionRejected,
        RoundScored,
        MatchEnded,
        SessionFaulted
    }

    /// <summary>
    /// 서버가 좌석에 보내는 이벤트.
    /// ActionRejected 는 발신자에게만 보낸다 — 브로드캐스트하면 상대의 시도 자체가
    /// 정보가 되므로 기획서 7.7 이 금지한다.
    /// </summary>
    public sealed class ServerEvent
    {
        public ServerEventKind Kind { get; set; }

        /// <summary>StateUpdate: 수신 좌석의 시야.</summary>
        public PlayerView? View { get; set; }

        /// <summary>ActionRejected / SessionFaulted: 사유.</summary>
        public string? Reason { get; set; }

        /// <summary>ActionRejected: 거부된 액션의 ActionSeq.</summary>
        public long ActionSeq { get; set; }

        /// <summary>RoundScored: 라운드 정산 결과.</summary>
        public RoundResult? Round { get; set; }

        /// <summary>MatchEnded: 승리 팀(0=A, 1=B)과 최종 누적 점수.</summary>
        public int WinningTeam { get; set; }
        public int TeamA { get; set; }
        public int TeamB { get; set; }

        public static ServerEvent StateUpdate(PlayerView view) =>
            new ServerEvent { Kind = ServerEventKind.StateUpdate, View = view };

        public static ServerEvent ActionRejected(long actionSeq, string reason) =>
            new ServerEvent { Kind = ServerEventKind.ActionRejected, ActionSeq = actionSeq, Reason = reason };

        public static ServerEvent RoundScored(RoundResult round) =>
            new ServerEvent { Kind = ServerEventKind.RoundScored, Round = round };

        public static ServerEvent MatchEnded(int winningTeam, int teamA, int teamB) =>
            new ServerEvent
            {
                Kind = ServerEventKind.MatchEnded,
                WinningTeam = winningTeam, TeamA = teamA, TeamB = teamB
            };

        public static ServerEvent SessionFaulted(string reason) =>
            new ServerEvent { Kind = ServerEventKind.SessionFaulted, Reason = reason };
    }
}
```

- [ ] **Step 5: 전송 이음새와 루프백 구현 작성**

`core/src/Tichu.Net/ITichuTransport.cs`:

```csharp
using System;

namespace Tichu.Net
{
    /// <summary>
    /// 권위 세션이 보는 전송 계층. 세션은 이 인터페이스만 알고, 뒤에
    /// 로컬 루프백 / WebSocket 서버(M2) 가 갈아끼워진다.
    /// </summary>
    public interface ITichuTransport
    {
        /// <summary>한 좌석에게만 이벤트를 보낸다.</summary>
        void SendTo(int seat, ServerEvent ev);

        /// <summary>클라이언트 액션이 도착했을 때 호출될 핸들러를 등록한다.</summary>
        void OnClientAction(Action<ClientAction> handler);
    }
}
```

`core/src/Tichu.Net/LoopbackTransport.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace Tichu.Net
{
    /// <summary>
    /// 같은 프로세스 전송. 좌석별 수신함에 이벤트를 쌓아 두고 Submit 으로 액션을 세션에 넘긴다.
    /// SendTo 는 세션 스레드에서, Submit/Inbox 는 호출자 스레드에서 올 수 있어 잠금을 쓴다.
    /// </summary>
    public sealed class LoopbackTransport : ITichuTransport
    {
        private const int SeatCount = 4;

        private readonly object _gate = new object();
        private readonly List<ServerEvent>[] _inbox;
        private Action<ClientAction>? _handler;

        public LoopbackTransport()
        {
            _inbox = new List<ServerEvent>[SeatCount];
            for (int i = 0; i < SeatCount; i++) _inbox[i] = new List<ServerEvent>();
        }

        /// <inheritdoc/>
        public void SendTo(int seat, ServerEvent ev)
        {
            lock (_gate) _inbox[seat].Add(ev);
        }

        /// <inheritdoc/>
        public void OnClientAction(Action<ClientAction> handler)
        {
            lock (_gate) _handler = handler;
        }

        /// <summary>좌석 수신함의 스냅샷(호출 시점까지 받은 이벤트 복사본). 검증용.</summary>
        public IReadOnlyList<ServerEvent> Inbox(int seat)
        {
            lock (_gate) return new List<ServerEvent>(_inbox[seat]);
        }

        /// <summary>
        /// 아직 가져가지 않은 이벤트를 꺼내고 수신함을 비운다. 폴링하는 클라이언트는
        /// 이걸 써야 한다 — Inbox 는 매번 전체를 복사하므로 반복 폴링이 2차식이 된다.
        /// </summary>
        public IReadOnlyList<ServerEvent> Drain(int seat)
        {
            lock (_gate)
            {
                var taken = new List<ServerEvent>(_inbox[seat]);
                _inbox[seat].Clear();
                return taken;
            }
        }

        /// <summary>클라이언트가 액션을 제출한다. 핸들러가 없으면 아무 일도 하지 않는다.</summary>
        public void Submit(ClientAction a)
        {
            Action<ClientAction>? h;
            lock (_gate) h = _handler;
            h?.Invoke(a);
        }
    }
}
```

- [ ] **Step 6: 테스트 통과 확인**

Run: `dotnet test core/Tichu.sln -c Release --nologo --filter "FullyQualifiedName~LoopbackTransportTests"`
Expected: PASS (5 tests)

- [ ] **Step 7: 미러 + 전체 회귀 + 커밋**

```bash
pwsh -File tools/sync-core-to-unity.ps1 && pwsh -File tools/sync-core-to-unity.ps1 -Check
dotnet test core/Tichu.sln -c Release --nologo
git add core/src/Tichu.Net Assets/_Project/Net core/tests/Tichu.Core.Tests/LoopbackTransportTests.cs
git commit -m "feat(mp): 프로토콜 DTO + ITichuTransport + 로컬 루프백

기획서 7.2 메시지 카탈로그를 ClientAction 7종 / ServerEvent 5종으로
구현했다. ActionRejected 는 발신자에게만 간다 — 브로드캐스트하면 상대의
시도 자체가 정보가 된다(7.7).

Play 액션은 카드와 함께 ×2 스케일 Rank 를 싣는다. 봉황이 스트레이트의
어느 칸을 메우는지는 카드 집합만으로 결정되지 않기 때문이다.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: `GameDriver`에 선택적 훅 3개

**Files:**
- Modify: `core/src/Tichu.GameFlow/GameDriver.cs`
- Test: `core/tests/Tichu.Core.Tests/GameDriverHookTests.cs`

**Interfaces:**
- Consumes: 없음(`Tichu.GameFlow`만 건드린다).
- Produces: `GameDriver.RunRound(GameState s, System.Action<GameState, GameAction>? onApply = null, System.Func<GameState, int>? takePendingTichu = null, System.Func<GameState, GameAction?>? takePendingBomb = null) → RoundOutcome`. 세 훅의 의미는 `Tichu.Presentation.AsyncGameDriver`의 동명 매개변수와 같다. 전부 `null`이면 기존 동작과 완전히 같다.

- [ ] **Step 1: 실패하는 테스트 작성**

`core/tests/Tichu.Core.Tests/GameDriverHookTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// GameDriver 선택적 훅 — 비동기 드라이버와 같은 모양의 onApply / 작은 티츄 예약 /
    /// 차례밖 폭탄 예약. 훅을 주지 않으면 기존 동작과 비트 동일해야 한다.
    /// </summary>
    public class GameDriverHookTests
    {
        private static List<Card> Hand(params Card[] cards) => new List<Card>(cards);

        private static IAgent[] FourDefaults() =>
            new IAgent[] { new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent() };

        [Test]
        public void Null_hooks_produce_the_same_round_as_before()
        {
            var a = new GameDriver(FourDefaults()).RunRound(GameEngine.NewRound(42UL));
            var b = new GameDriver(FourDefaults()).RunRound(GameEngine.NewRound(42UL), null, null, null);

            Assert.That(b.State.ComputeHash(), Is.EqualTo(a.State.ComputeHash()), "상태 해시가 달라졌다");
            Assert.That(b.Log.Count, Is.EqualTo(a.Log.Count), "로그 길이가 달라졌다");
            for (int i = 0; i < a.Log.Count; i++)
            {
                Assert.That(b.Log[i].Kind, Is.EqualTo(a.Log[i].Kind), $"로그 {i} 종류");
                Assert.That(b.Log[i].Seat, Is.EqualTo(a.Log[i].Seat), $"로그 {i} 좌석");
            }
        }

        [Test]
        public void OnApply_fires_once_per_logged_action()
        {
            var seen = new List<GameAction>();
            var outcome = new GameDriver(FourDefaults())
                .RunRound(GameEngine.NewRound(42UL), (st, act) => seen.Add(act));

            Assert.That(seen.Count, Is.EqualTo(outcome.Log.Count));
            for (int i = 0; i < seen.Count; i++)
                Assert.That(seen[i], Is.SameAs(outcome.Log[i]), $"로그 {i} 가 onApply 와 다르다");
        }

        [Test]
        public void Pending_tichu_is_applied_as_a_call_before_the_in_turn_action()
        {
            // 작은 티츄는 손패 14장 + 아직 한 장도 내지 않았을 때만 합법이다
            // (GameEngine.ApplyCallTichu: Hand.Count != FullHandCount → reject).
            // 예약을 받을 seat2 에만 14장을 준다.
            var fourteen = new List<Card>();
            for (int rank = 7; rank <= 14; rank++) fourteen.Add(Card.Normal(rank, Suit.Jade));   // 8장
            for (int rank = 7; rank <= 12; rank++) fourteen.Add(Card.Normal(rank, Suit.Sword));  // 6장
            Assert.That(fourteen.Count, Is.EqualTo(14), "테스트 전제: seat2 손패가 14장이어야 함");

            var s = GameFlowHelpers.PlayState(0,
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                fourteen,
                Hand(Card.Normal(6, Suit.Jade)));

            bool handed = false;
            var outcome = new GameDriver(FourDefaults()).RunRound(s, null,
                takePendingTichu: _ => { if (handed) return -1; handed = true; return 2; });

            Assert.That(outcome.State.Seats[2].Call, Is.EqualTo(TichuCall.Tichu));
            Assert.That(outcome.Log[0].Kind, Is.EqualTo(GameActionKind.CallTichu), "예약 콜이 먼저 적용돼야 한다");
            Assert.That(outcome.Log[0].Seat, Is.EqualTo(2));
        }

        [Test]
        public void Pending_bomb_preempts_the_in_turn_action()
        {
            // seat0 이 단독 3 을 리드한 뒤, seat2 가 9 포카드를 예약한다.
            var s = GameFlowHelpers.PlayState(0,
                Hand(Card.Normal(3, Suit.Jade), Card.Normal(2, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Sword),
                     Card.Normal(9, Suit.Pagoda), Card.Normal(9, Suit.Star)),
                Hand(Card.Normal(6, Suit.Jade)));

            Assert.That(GameEngine.Apply(s, GameAction.Play(0, new[] { Card.Normal(3, Suit.Jade) })).Ok,
                Is.True, "테스트 전제: seat0 단독 3 리드 적용");

            var bombCards = new[]
            {
                Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Sword),
                Card.Normal(9, Suit.Pagoda), Card.Normal(9, Suit.Star)
            };
            var bomb = CombinationRecognizer.Recognize(bombCards, TrickContext.Lead);
            Assert.That(bomb.Type, Is.EqualTo(CombinationType.FourBomb), "테스트 전제: 9 포카드로 인식");

            bool handed = false;
            var outcome = new GameDriver(FourDefaults()).RunRound(s, null, null,
                takePendingBomb: _ =>
                {
                    if (handed) return null;
                    handed = true;
                    return GameAction.Play(2, bombCards);
                });

            Assert.That(outcome.Log[0].Kind, Is.EqualTo(GameActionKind.Play));
            Assert.That(outcome.Log[0].Seat, Is.EqualTo(2), "예약 폭탄이 차례를 선점해야 한다");
        }

        [Test]
        public void Rejected_pending_bomb_is_dropped_without_throwing()
        {
            var s = GameFlowHelpers.PlayState(0,
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)),
                Hand(Card.Normal(6, Suit.Jade)));

            // seat2 는 손에 없는 카드를 폭탄으로 예약한다 → 엔진이 거부 → 조용히 버려야 한다.
            var bogus = new[]
            {
                Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Sword),
                Card.Normal(9, Suit.Pagoda), Card.Normal(9, Suit.Star)
            };

            bool handed = false;
            Assert.DoesNotThrow(() =>
                new GameDriver(FourDefaults()).RunRound(s, null, null,
                    takePendingBomb: _ =>
                    {
                        if (handed) return null;
                        handed = true;
                        return GameAction.Play(2, bogus);
                    }));
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test core/Tichu.sln -c Release --nologo --filter "FullyQualifiedName~GameDriverHookTests"`
Expected: 빌드 실패 — `CS1501: No overload for method 'RunRound' takes 4 arguments`

- [ ] **Step 3: 필드 추가**

`core/src/Tichu.GameFlow/GameDriver.cs`의 `private readonly IAgent[] _agents;` 바로 아래에 추가:

```csharp
        private System.Action<GameState, GameAction>? _onApply;          // 매 Apply 후 호출(세션 브로드캐스트용; null=기존 동작)
        private System.Func<GameState, int>? _takePendingTichu;          // 예약된 작은 티츄 좌석(-1=없음)
        private System.Func<GameState, GameAction?>? _takePendingBomb;   // 예약된 차례밖 폭탄(null=없음)
```

- [ ] **Step 4: `RunRound` 시그니처 확장**

`public RoundOutcome RunRound(GameState s)` 로 시작하는 줄과 그 바로 아래 `var log = ...` 줄 사이를 다음으로 교체한다(`int grandNext = 0, exchangeNext = 0;` 이후는 손대지 않는다):

```csharp
        /// <summary>주어진 상태에서 한 라운드를 Scoring 까지 구동한 뒤 정산해 결과를 반환한다.</summary>
        /// <param name="onApply">매 액션 적용 직후 호출. 권위 세션이 좌석별 뷰를 방송하는 지점.</param>
        /// <param name="takePendingTichu">예약된 작은 티츄 좌석을 꺼낸다(-1=없음).</param>
        /// <param name="takePendingBomb">예약된 차례밖 폭탄을 꺼낸다(null=없음). 엔진이 거부하면 조용히 버린다.</param>
        public RoundOutcome RunRound(
            GameState s,
            System.Action<GameState, GameAction>? onApply = null,
            System.Func<GameState, int>? takePendingTichu = null,
            System.Func<GameState, GameAction?>? takePendingBomb = null)
        {
            _onApply = onApply;
            _takePendingTichu = takePendingTichu;
            _takePendingBomb = takePendingBomb;

            var log = new List<GameAction>();
```

- [ ] **Step 5: `DrivePlay` 선두에 예약 처리 단계 삽입**

`DrivePlay` 의 `// 1) 용 양도 먼저.` 주석 **바로 앞**에 삽입한다(`AsyncGameDriver.DrivePlayAsync` 의 0)·0b) 와 같은 모양):

```csharp
            // 0) 작은 티츄 인터럽트: 예약된 콜을 먼저 적용(차례 무관, 첫 패 전이면 합법).
            if (_takePendingTichu != null)
            {
                int ts = _takePendingTichu(s);
                if (ts >= 0) { Apply(s, GameAction.CallTichu(ts), log); return; }
            }

            // 0b) 차례밖 폭탄 인터럽트: 예약된 폭탄을 적용 시도. 합법성은 엔진이 판정하며,
            //     거부(현재 Top 미격파 등)되면 throw 하지 않고 조용히 버린다(인간 입력이므로).
            if (_takePendingBomb != null)
            {
                var bombAction = _takePendingBomb(s);
                if (bombAction != null)
                {
                    var res = GameEngine.Apply(s, bombAction);
                    if (res.Ok) { log.Add(bombAction); _onApply?.Invoke(s, bombAction); return; }
                }
            }
```

- [ ] **Step 6: `Apply` 를 인스턴스 메서드로 바꾸고 훅 호출**

`private static void Apply(GameState s, GameAction a, List<GameAction> log)` 로 시작하는 메서드 전체를 다음으로 교체:

```csharp
        /// <summary>액션을 적용한다. 거부되면 throw(불법 상태 미발생 증명), 성공하면 로그에 기록.</summary>
        private void Apply(GameState s, GameAction a, List<GameAction> log)
        {
            var res = GameEngine.Apply(s, a);
            if (!res.Ok)
                throw new System.InvalidOperationException($"illegal action {a.Kind} seat {a.Seat}: {res.RejectReason}");
            log.Add(a);
            _onApply?.Invoke(s, a); // 세션: 매 적용마다 좌석별 뷰 방송. 기존 호출부는 null 이라 무영향.
        }
```

- [ ] **Step 7: 테스트 통과 확인**

Run: `dotnet test core/Tichu.sln -c Release --nologo --filter "FullyQualifiedName~GameDriverHookTests"`
Expected: PASS (5 tests)

- [ ] **Step 8: 기존 드라이버 회귀 확인**

Run: `dotnet test core/Tichu.sln -c Release --nologo`
Expected: 전부 PASS. 특히 `GameFlowDriverTests` · `GameFlowScenarioTests` · `GameFlowFullGameTests` · `GameFlowMatchTests` 가 그대로 그린이어야 한다 — 훅 기본값이 `null`이라 동작이 안 바뀌었다는 증거다.

- [ ] **Step 9: 미러 + 커밋**

```bash
pwsh -File tools/sync-core-to-unity.ps1 && pwsh -File tools/sync-core-to-unity.ps1 -Check
git add core/src/Tichu.GameFlow/GameDriver.cs Assets/_Project/GameFlow/GameDriver.cs core/tests/Tichu.Core.Tests/GameDriverHookTests.cs
git commit -m "feat(mp): GameDriver 에 선택적 훅 3개 — onApply · 티츄/폭탄 예약

AsyncGameDriver 가 이미 가진 것과 같은 모양이고 기본값이 null 이라 기존
호출부와 동작이 완전히 같다(기존 드라이버 테스트가 그 증거).

폭탄을 예약 경로로 두는 이유: 드라이버의 폭탄 창은 좌석을 순회하며
각자에게 묻는데, 사람에게 이걸 동기로 물으면 매 트릭마다 전원이
대기한다. 예약은 기획서 7.4 의 '동시 도착 폭탄은 수신 순서로 직렬화'와
그대로 맞는다.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: `MoveResolver`와 `RemoteAgent` — 원격 좌석

**Files:**
- Create: `core/src/Tichu.Net/MoveResolver.cs` · `core/src/Tichu.Net/Awaiting.cs` · `core/src/Tichu.Net/RemoteAgent.cs`
- Modify: `core/src/Tichu.Net/PlayerView.cs` (`Awaiting` 프로퍼티 추가) · `core/src/Tichu.Net/Protocol/ServerEvent.cs` (`StateUpdate` 2인자 오버로드)
- Test: `core/tests/Tichu.Core.Tests/MoveResolverTests.cs` · `core/tests/Tichu.Core.Tests/RemoteAgentTests.cs`

**Interfaces:**
- Consumes: Task 2 `PlayerView`/`ViewProjector`, Task 3 `ClientAction`/`ServerEvent`.
- Produces:
  - `Tichu.Net.MoveResolver.ResolveMove(GameState s, int seat, ClientAction a) → Combination?` — 합법수 중 카드 집합(+`Rank`가 0이 아니면 Rank까지) 일치가 **정확히 하나**일 때만 그 조합, 0개(불법)나 2개 이상(모호)이면 `null`.
  - `Tichu.Net.Awaiting` — `Kind`(`Tichu.GameFlow.StepKind`) · `Seat`(int) · `DeadlineUtc`(`System.DateTime?`) · `LegalMoves`(`System.Collections.Generic.IReadOnlyList<Combination>?`) · `CanPass`(bool) · `Redacted()` → 같은 `Kind`/`Seat`이고 `LegalMoves=null`·`CanPass=false`인 새 인스턴스.
  - `PlayerView.Awaiting` (`Awaiting?`).
  - `ServerEvent.StateUpdate(PlayerView view, Awaiting? awaiting)` — `view.Awaiting`에 대입하고 `StateUpdate` 이벤트를 만든다. 기존 1인자 오버로드는 유지한다.
  - `Tichu.Net.RemoteAgent : Tichu.GameFlow.Agents.IAgent` — 생성자 `RemoteAgent(int seat, System.Action<int, ServerEvent> send, System.Action<int, Awaiting> onAwait, System.Func<long> nextArrival, System.TimeSpan timeout, System.Threading.CancellationToken ct)`; 공개 메서드 `void Submit(ClientAction a)` · `ClientAction? PeekReservedBomb()` · `long ReservedBombArrival()` · `void ConsumeReservedBomb()` · `bool TakeReservedTichu()`.

- [ ] **Step 1: `MoveResolver` 실패 테스트 작성**

`core/tests/Tichu.Core.Tests/MoveResolverTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// MoveResolver — 클라이언트가 보낸 카드 집합을 서버의 합법수 중 하나로 확정한다.
    /// 봉황 스트레이트는 카드 집합이 같아도 Rank 가 다른 두 합법수를 만들 수 있어
    /// Rank 없이는 모호하다.
    /// </summary>
    public class MoveResolverTests
    {
        private static List<Card> Hand(params Card[] cards) => new List<Card>(cards);

        private static GameState LeadState(params IReadOnlyList<Card>[] hands) =>
            GameFlowHelpers.PlayState(0, hands);

        [Test]
        public void Unique_card_set_resolves_without_rank()
        {
            var s = LeadState(
                Hand(Card.Normal(5, Suit.Jade), Card.Normal(9, Suit.Sword)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(6, Suit.Jade)),
                Hand(Card.Normal(7, Suit.Jade)));

            var a = ClientAction.Play(0, 1L, new List<Card> { Card.Normal(5, Suit.Jade) }, rank: 0, wish: null);
            var move = MoveResolver.ResolveMove(s, 0, a);

            Assert.That(move, Is.Not.Null);
            Assert.That(move!.Type, Is.EqualTo(CombinationType.Single));
            Assert.That(move.Cards, Is.EquivalentTo(new[] { Card.Normal(5, Suit.Jade) }));
        }

        [Test]
        public void Cards_not_in_hand_resolve_to_null()
        {
            var s = LeadState(
                Hand(Card.Normal(5, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(6, Suit.Jade)),
                Hand(Card.Normal(7, Suit.Jade)));

            var a = ClientAction.Play(0, 1L, new List<Card> { Card.Normal(13, Suit.Star) }, rank: 0, wish: null);

            Assert.That(MoveResolver.ResolveMove(s, 0, a), Is.Null);
        }

        [Test]
        public void Phoenix_straight_is_ambiguous_without_rank_and_resolves_with_it()
        {
            // 3,4,5,6,봉황 → [2,6] 과 [3,7] 두 스트레이트가 같은 카드 집합으로 생성된다.
            var phoenixHand = Hand(
                Card.Normal(3, Suit.Jade), Card.Normal(4, Suit.Jade),
                Card.Normal(5, Suit.Jade), Card.Normal(6, Suit.Jade), Card.Phoenix);

            var s = LeadState(phoenixHand,
                Hand(Card.Normal(8, Suit.Jade)),
                Hand(Card.Normal(9, Suit.Jade)),
                Hand(Card.Normal(10, Suit.Jade)));

            var moves = LegalMoveGenerator.LegalMoves(s, 0);
            var sameSet = new List<Combination>();
            foreach (var m in moves)
                if (m.Cards.Count == 5 && m.Type == CombinationType.Straight)
                    sameSet.Add(m);

            Assert.That(sameSet.Count, Is.GreaterThanOrEqualTo(2),
                "테스트 전제: 같은 5장으로 Rank 가 다른 스트레이트가 둘 이상 생성돼야 함");

            var cards = new List<Card>(phoenixHand);
            var ambiguous = ClientAction.Play(0, 1L, cards, rank: 0, wish: null);
            Assert.That(MoveResolver.ResolveMove(s, 0, ambiguous), Is.Null, "Rank 없으면 모호 → null");

            var pinned = ClientAction.Play(0, 2L, cards, rank: sameSet[0].Rank, wish: null);
            var resolved = MoveResolver.ResolveMove(s, 0, pinned);
            Assert.That(resolved, Is.Not.Null);
            Assert.That(resolved!.Rank, Is.EqualTo(sameSet[0].Rank));
        }

        [Test]
        public void Null_cards_resolve_to_null()
        {
            var s = LeadState(
                Hand(Card.Normal(5, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(6, Suit.Jade)),
                Hand(Card.Normal(7, Suit.Jade)));

            var a = new ClientAction { Kind = ClientActionKind.Play, Seat = 0, ActionSeq = 1L, Cards = null };

            Assert.That(MoveResolver.ResolveMove(s, 0, a), Is.Null);
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test core/Tichu.sln -c Release --nologo --filter "FullyQualifiedName~MoveResolverTests"`
Expected: 빌드 실패 — `CS0103: The name 'MoveResolver' does not exist`

- [ ] **Step 3: `MoveResolver` 구현**

`core/src/Tichu.Net/MoveResolver.cs`:

```csharp
using System.Collections.Generic;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;

namespace Tichu.Net
{
    /// <summary>
    /// 클라이언트가 보낸 카드 집합을 서버의 합법수 하나로 확정한다.
    /// 합법수 목록에서 찾는 방식이라 봉황 대체·소원 강제·폭탄 인터럽트 같은 규칙이
    /// 자동으로 지켜지고, 조합 인식도 엔진과 항상 일치한다.
    /// </summary>
    public static class MoveResolver
    {
        /// <summary>
        /// 일치하는 합법수가 정확히 하나면 그것, 0개(불법)거나 2개 이상(모호)이면 null.
        /// Rank 가 0이 아니면 Rank 까지 일치해야 한다 — 봉황이 스트레이트의 어느 칸을
        /// 메우는지는 카드 집합만으로 결정되지 않기 때문이다.
        /// </summary>
        public static Combination? ResolveMove(GameState s, int seat, ClientAction a)
        {
            if (a.Cards == null || a.Cards.Count == 0) return null;

            var moves = LegalMoveGenerator.LegalMoves(s, seat);
            Combination? match = null;
            int matchCount = 0;

            for (int i = 0; i < moves.Count; i++)
            {
                var m = moves[i];
                if (!SameCardMultiset(m.Cards, a.Cards)) continue;
                if (a.Rank != 0 && m.Rank != a.Rank) continue;
                match = m;
                matchCount++;
                if (matchCount > 1) return null; // 모호 — 클라이언트가 Rank 를 지정해야 한다
            }

            return matchCount == 1 ? match : null;
        }

        /// <summary>두 카드 모음이 같은 다중집합인가(순서 무관, 중복 개수 포함).</summary>
        private static bool SameCardMultiset(IReadOnlyList<Card> x, IReadOnlyList<Card> y)
        {
            if (x.Count != y.Count) return false;

            var used = new bool[y.Count];
            for (int i = 0; i < x.Count; i++)
            {
                bool found = false;
                for (int j = 0; j < y.Count; j++)
                {
                    if (used[j] || !y[j].Equals(x[i])) continue;
                    used[j] = true;
                    found = true;
                    break;
                }
                if (!found) return false;
            }
            return true;
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test core/Tichu.sln -c Release --nologo --filter "FullyQualifiedName~MoveResolverTests"`
Expected: PASS (4 tests)

- [ ] **Step 5: `RemoteAgent` 실패 테스트 작성**

`core/tests/Tichu.Core.Tests/RemoteAgentTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// RemoteAgent — 원격 좌석. 액션이 도착할 때까지 블로킹하고, 묻기 전에 온 액션도
    /// 보관하며(교환 동시 제출), 불법 입력은 상태를 바꾸지 않고 거부한 뒤 다시 기다린다.
    /// </summary>
    public class RemoteAgentTests
    {
        private static List<Card> Hand(params Card[] cards) => new List<Card>(cards);

        private sealed class Harness
        {
            public readonly List<ServerEvent> Sent = new List<ServerEvent>();
            public readonly List<Awaiting> Awaits = new List<Awaiting>();
            public readonly CancellationTokenSource Cts = new CancellationTokenSource();
            private long _arrival;

            public RemoteAgent Create(int seat, TimeSpan? timeout = null) => new RemoteAgent(
                seat,
                (s, ev) => { lock (Sent) Sent.Add(ev); },
                (s, aw) => { lock (Awaits) Awaits.Add(aw); },
                () => Interlocked.Increment(ref _arrival),
                timeout ?? TimeSpan.FromSeconds(5),
                Cts.Token);

            public int RejectCount
            {
                get { lock (Sent) { int n = 0; foreach (var e in Sent) if (e.Kind == ServerEventKind.ActionRejected) n++; return n; } }
            }
        }

        private static DecisionContext LeadContext(out GameState state)
        {
            state = GameFlowHelpers.PlayState(0,
                Hand(Card.Normal(5, Suit.Jade), Card.Normal(9, Suit.Sword)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(6, Suit.Jade)),
                Hand(Card.Normal(7, Suit.Jade)));
            return GameFlowHelpers.Context(state, 0);
        }

        [Test]
        public void Action_buffered_before_the_question_is_consumed_without_blocking()
        {
            var h = new Harness();
            var agent = h.Create(0);
            var s = GameEngine.NewRound(42UL);

            // 아직 아무것도 묻지 않은 상태에서 큰 티츄 패스가 먼저 도착한다.
            agent.Submit(ClientAction.DeclineGrandTichu(0, 1L));

            var ctx = new DecisionContext(s, 0);
            Assert.That(agent.CallGrandTichu(ctx), Is.False);
            Assert.That(h.RejectCount, Is.EqualTo(0), "미리 온 액션을 거부해선 안 된다");
        }

        [Test]
        public void Wrong_seat_is_rejected()
        {
            var h = new Harness();
            var agent = h.Create(0);

            agent.Submit(ClientAction.DeclineGrandTichu(seat: 2, actionSeq: 1L));

            Assert.That(h.RejectCount, Is.EqualTo(1));
        }

        [Test]
        public void Duplicate_or_backward_action_seq_is_rejected()
        {
            var h = new Harness();
            var agent = h.Create(0);

            agent.Submit(ClientAction.CallTichu(0, 5L));
            agent.Submit(ClientAction.CallTichu(0, 5L)); // 중복
            agent.Submit(ClientAction.CallTichu(0, 3L)); // 역행

            Assert.That(h.RejectCount, Is.EqualTo(2));
        }

        [Test]
        public void Reserved_tichu_is_taken_once()
        {
            var h = new Harness();
            var agent = h.Create(0);

            agent.Submit(ClientAction.CallTichu(0, 1L));

            Assert.That(agent.TakeReservedTichu(), Is.True);
            Assert.That(agent.TakeReservedTichu(), Is.False, "예약은 한 번만 소비된다");
        }

        [Test]
        public void DecideTurn_returns_the_submitted_legal_move()
        {
            var h = new Harness();
            var agent = h.Create(0);
            var ctx = LeadContext(out _);

            agent.Submit(ClientAction.Play(0, 1L,
                new List<Card> { Card.Normal(5, Suit.Jade) }, rank: 0, wish: null));

            var d = agent.DecideTurn(ctx);

            Assert.That(d.IsPass, Is.False);
            Assert.That(d.Move, Is.Not.Null);
            Assert.That(d.Move!.Cards, Is.EquivalentTo(new[] { Card.Normal(5, Suit.Jade) }));
            Assert.That(h.RejectCount, Is.EqualTo(0));
        }

        [Test]
        public void Illegal_move_is_rejected_and_the_agent_keeps_waiting()
        {
            var h = new Harness();
            var agent = h.Create(0);
            var ctx = LeadContext(out _);

            // 손에 없는 카드를 먼저 보내고, 그 다음 합법수를 보낸다.
            agent.Submit(ClientAction.Play(0, 1L,
                new List<Card> { Card.Normal(13, Suit.Star) }, rank: 0, wish: null));
            agent.Submit(ClientAction.Play(0, 2L,
                new List<Card> { Card.Normal(5, Suit.Jade) }, rank: 0, wish: null));

            var d = agent.DecideTurn(ctx);

            Assert.That(d.Move!.Cards, Is.EquivalentTo(new[] { Card.Normal(5, Suit.Jade) }));
            Assert.That(h.RejectCount, Is.EqualTo(1), "불법 입력 1건만 거부돼야 한다");
        }

        [Test]
        public void Illegal_pass_on_lead_is_rejected()
        {
            var h = new Harness();
            var agent = h.Create(0);
            var ctx = LeadContext(out _);

            agent.Submit(ClientAction.Pass(0, 1L));                      // 리드에서는 패스 불가
            agent.Submit(ClientAction.Play(0, 2L,
                new List<Card> { Card.Normal(5, Suit.Jade) }, rank: 0, wish: null));

            var d = agent.DecideTurn(ctx);

            Assert.That(d.IsPass, Is.False);
            Assert.That(h.RejectCount, Is.EqualTo(1));
        }

        [Test]
        public void Awaiting_is_announced_with_legal_moves_before_blocking()
        {
            var h = new Harness();
            var agent = h.Create(0);
            var ctx = LeadContext(out _);

            agent.Submit(ClientAction.Play(0, 1L,
                new List<Card> { Card.Normal(5, Suit.Jade) }, rank: 0, wish: null));
            agent.DecideTurn(ctx);

            Assert.That(h.Awaits.Count, Is.GreaterThanOrEqualTo(1));
            var aw = h.Awaits[0];
            Assert.That(aw.Kind, Is.EqualTo(StepKind.Play));
            Assert.That(aw.Seat, Is.EqualTo(0));
            Assert.That(aw.LegalMoves, Is.Not.Null.And.Count.GreaterThan(0));
            Assert.That(aw.Redacted().LegalMoves, Is.Null, "축약판엔 합법수가 없어야 한다");
        }

        [Test]
        public void Timeout_throws()
        {
            var h = new Harness();
            var agent = h.Create(0, TimeSpan.FromMilliseconds(50));
            var ctx = LeadContext(out _);

            Assert.Throws<TimeoutException>(() => agent.DecideTurn(ctx));
        }

        [Test]
        public void Cancellation_unblocks_the_wait()
        {
            var h = new Harness();
            var agent = h.Create(0, TimeSpan.FromSeconds(30));
            var ctx = LeadContext(out _);

            var t = new Thread(() =>
            {
                Thread.Sleep(50);
                h.Cts.Cancel();
            }) { IsBackground = true };
            t.Start();

            Assert.Throws<OperationCanceledException>(() => agent.DecideTurn(ctx));
        }
    }
}
```

- [ ] **Step 6: 테스트 실패 확인**

Run: `dotnet test core/Tichu.sln -c Release --nologo --filter "FullyQualifiedName~RemoteAgentTests"`
Expected: 빌드 실패 — `CS0246: ... 'RemoteAgent' could not be found` / `'Awaiting' could not be found`

- [ ] **Step 7: `Awaiting` 작성과 `PlayerView`·`ServerEvent` 확장**

`core/src/Tichu.Net/Awaiting.cs`:

```csharp
using System;
using System.Collections.Generic;
using Tichu.Core.Combinations;
using Tichu.GameFlow;

namespace Tichu.Net
{
    /// <summary>
    /// 지금 서버가 누구에게 무엇을 묻고 있는지. 묻는 주체인 RemoteAgent 가 대기에
    /// 들어가는 순간 발행한다 — 드라이버의 내부 카운터와 RoundSetup(internal) 을
    /// 세션이 볼 수 없어 상태에서 역산할 수 없기 때문이다.
    ///
    /// LegalMoves 와 CanPass 는 대기 좌석에게만 보낸다. 둘 다 그 좌석의 손패와 공개
    /// 트릭 상태에서만 유도되므로 은닉 정보가 아니며, 클라이언트가 전체 GameState 를
    /// 복원하지 않아도 수를 고를 수 있게 해 준다.
    /// </summary>
    public sealed class Awaiting
    {
        public StepKind Kind { get; set; }
        public int Seat { get; set; }

        /// <summary>만료 시각. M1 에서는 항상 null(턴 시계는 M5).</summary>
        public DateTime? DeadlineUtc { get; set; }

        /// <summary>대기 좌석이 지금 낼 수 있는 조합 전체. 다른 좌석에게는 null.</summary>
        public IReadOnlyList<Combination>? LegalMoves { get; set; }

        /// <summary>대기 좌석이 지금 패스할 수 있는가. 다른 좌석에게는 false.</summary>
        public bool CanPass { get; set; }

        /// <summary>다른 좌석에게 보낼 축약판 — 차례 표시에 필요한 것만 남긴다.</summary>
        public Awaiting Redacted() =>
            new Awaiting { Kind = Kind, Seat = Seat, DeadlineUtc = DeadlineUtc };
    }
}
```

`core/src/Tichu.Net/PlayerView.cs` 의 `PlayerView` 클래스 끝(`TargetScore` 프로퍼티 아래)에 추가:

```csharp
        /// <summary>서버가 지금 무엇을 기다리는지. 기다리는 중이 아니면 null.</summary>
        public Awaiting? Awaiting { get; set; }
```

`core/src/Tichu.Net/Protocol/ServerEvent.cs` 의 기존 `StateUpdate(PlayerView view)` 팩토리 **아래**에 오버로드를 추가:

```csharp
        public static ServerEvent StateUpdate(PlayerView view, Awaiting? awaiting)
        {
            view.Awaiting = awaiting;
            return new ServerEvent { Kind = ServerEventKind.StateUpdate, View = view };
        }
```

- [ ] **Step 8: `RemoteAgent` 구현**

`core/src/Tichu.Net/RemoteAgent.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;

namespace Tichu.Net
{
    /// <summary>
    /// 원격 플레이어 좌석. 드라이버가 물으면 해당 종류의 액션이 도착할 때까지 블로킹한다.
    ///
    /// 세 가지 우편함을 쓴다.
    ///  · 대기 슬롯  — 지금 묻고 있는 종류의 액션. 도착하면 즉시 대기를 깨운다.
    ///  · 선행 큐    — 아직 묻지 않은 종류의 액션. 보관해 두면 교환 4인 동시 제출이 자연히 동작한다.
    ///  · 예약       — 작은 티츄 콜과 차례밖 폭탄. 드라이버의 예약 훅으로 빠져나간다.
    ///
    /// 폭탄을 예약으로 두는 이유는 드라이버의 폭탄 창이 좌석을 순회하며 각자에게 묻기
    /// 때문이다 — 사람에게 그걸 동기로 물으면 매 트릭마다 전원이 대기한다.
    ///
    /// 불법 입력은 상태를 바꾸지 않고 발신자에게만 ActionRejected 를 보낸 뒤 계속 기다린다
    /// (기획서 7.7). 즉 이 클래스가 서버측 검증 관문이다.
    /// </summary>
    public sealed class RemoteAgent : IAgent
    {
        private readonly int _seat;
        private readonly Action<int, ServerEvent> _send;
        private readonly Action<int, Awaiting> _onAwait;
        private readonly Func<long> _nextArrival;
        private readonly TimeSpan _timeout;
        private readonly CancellationToken _ct;

        private readonly object _gate = new object();
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
        private readonly List<ClientAction> _early = new List<ClientAction>();

        private StepKind? _awaiting;
        private readonly Queue<ClientAction> _matched = new Queue<ClientAction>();
        private long _lastSeq;
        private bool _tichuReserved;
        private ClientAction? _bombReserved;
        private long _bombArrival;

        public RemoteAgent(
            int seat,
            Action<int, ServerEvent> send,
            Action<int, Awaiting> onAwait,
            Func<long> nextArrival,
            TimeSpan timeout,
            CancellationToken ct)
        {
            _seat = seat;
            _send = send;
            _onAwait = onAwait;
            _nextArrival = nextArrival;
            _timeout = timeout;
            _ct = ct;
        }

        // ── 클라이언트 입구 ──────────────────────────────────────────────────────

        /// <summary>클라이언트가 액션을 제출한다. 전송 스레드에서 호출된다.</summary>
        public void Submit(ClientAction a)
        {
            if (a.Seat != _seat) { Reject(a, "action seat does not match this seat"); return; }

            lock (_gate)
            {
                if (a.ActionSeq <= _lastSeq) { Reject(a, "duplicate or out-of-order actionSeq"); return; }
                _lastSeq = a.ActionSeq;

                if (_awaiting.HasValue && Matches(_awaiting.Value, a.Kind))
                {
                    // 큐로 받는다. 거부 뒤 재제출처럼 한 결정에 여러 액션이 연달아 올 수 있고,
                    // 단일 슬롯이면 먼저 온 것이 덮어써져 거부 통보가 사라진다.
                    _matched.Enqueue(a);
                    _signal.Release();
                    return;
                }

                if (a.Kind == ClientActionKind.CallTichu) { _tichuReserved = true; return; }

                if (a.Kind == ClientActionKind.Play)
                {
                    // 차례밖 폭탄 후보. 자기 차례가 되면 Await 가 여기서 꺼내 쓴다.
                    _bombReserved = a;
                    _bombArrival = _nextArrival();
                    return;
                }

                _early.Add(a);
            }
        }

        // ── 세션이 예약을 꺼내가는 입구 ─────────────────────────────────────────

        /// <summary>예약된 차례밖 폭탄 후보. 없으면 null.</summary>
        public ClientAction? PeekReservedBomb()
        {
            lock (_gate) return _bombReserved;
        }

        /// <summary>예약된 폭탄의 도착 순번. 좌석 간 직렬화에 쓴다.</summary>
        public long ReservedBombArrival()
        {
            lock (_gate) return _bombArrival;
        }

        /// <summary>예약된 폭탄을 소비한다.</summary>
        public void ConsumeReservedBomb()
        {
            lock (_gate) { _bombReserved = null; _bombArrival = 0; }
        }

        /// <summary>예약된 작은 티츄 콜을 한 번만 꺼낸다.</summary>
        public bool TakeReservedTichu()
        {
            lock (_gate)
            {
                if (!_tichuReserved) return false;
                _tichuReserved = false;
                return true;
            }
        }

        // ── IAgent 구현 ─────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public bool CallGrandTichu(in DecisionContext ctx)
        {
            var a = Await(StepKind.GrandTichu, ctx.State, ctx.Seat);
            return a.Kind == ClientActionKind.CallGrandTichu;
        }

        /// <inheritdoc/>
        public ExchangeChoice ChooseExchange(in DecisionContext ctx)
        {
            var state = ctx.State;
            int seat = ctx.Seat;
            while (true)
            {
                var a = Await(StepKind.Exchange, state, seat);
                if (!IsLegalExchange(state, seat, a)) { Reject(a, "exchange cards must be three distinct cards from hand"); continue; }
                return new ExchangeChoice(a.ToLeft, a.ToPartner, a.ToRight);
            }
        }

        /// <inheritdoc/>
        public bool CallTichu(in DecisionContext ctx) => TakeReservedTichu();

        /// <inheritdoc/>
        public TurnDecision DecideTurn(in DecisionContext ctx)
        {
            var state = ctx.State;
            int seat = ctx.Seat;
            while (true)
            {
                var a = Await(StepKind.Play, state, seat);

                if (a.Kind == ClientActionKind.Pass)
                {
                    if (LegalMoveGenerator.CanPass(state, seat)) return TurnDecision.Pass;
                    Reject(a, "cannot pass now");
                    continue;
                }

                var move = MoveResolver.ResolveMove(state, seat, a);
                if (move == null) { Reject(a, "illegal or ambiguous move"); continue; }
                return TurnDecision.Play(move, a.Wish);
            }
        }

        /// <inheritdoc/>
        /// <remarks>항상 null. 폭탄은 예약 경로로만 들어온다 — 그래야 매 트릭 전원 대기가 없다.</remarks>
        public Combination? DecideBomb(in DecisionContext ctx) => null;

        /// <inheritdoc/>
        public int ChooseDragonRecipient(in DecisionContext ctx)
        {
            var state = ctx.State;
            int seat = ctx.Seat;
            while (true)
            {
                var a = Await(StepKind.DragonGift, state, seat);
                int r = a.RecipientSeat;
                bool isOpponent = r >= 0 && r < 4 && (r % 2) != (seat % 2);
                if (!isOpponent) { Reject(a, "dragon recipient must be an opponent"); continue; }
                return r;
            }
        }

        // ── 내부 ────────────────────────────────────────────────────────────────

        /// <summary>해당 종류의 액션이 올 때까지 기다린다. 선행 큐와 폭탄 예약을 먼저 확인한다.</summary>
        private ClientAction Await(StepKind kind, GameState state, int seat)
        {
            // 버퍼에 답이 이미 있든 없든 **항상 먼저 알린다.** 이 방송이 "지금 누구에게
            // 무엇을 묻는지"의 유일한 신호라, 건너뛰면 아무도 차례를 알 수 없게 된다.
            _onAwait(_seat, BuildAwaiting(kind, state, seat));

            lock (_gate)
            {
                for (int i = 0; i < _early.Count; i++)
                {
                    if (!Matches(kind, _early[i].Kind)) continue;
                    var buffered = _early[i];
                    _early.RemoveAt(i);
                    return buffered;
                }

                if (kind == StepKind.Play && _bombReserved != null)
                {
                    var reserved = _bombReserved;
                    _bombReserved = null;
                    _bombArrival = 0;
                    return reserved;
                }

                _awaiting = kind;
            }

            bool got = _signal.Wait(_timeout, _ct);

            lock (_gate)
            {
                _awaiting = null;
                if (!got) throw new TimeoutException($"seat {_seat} did not act within {_timeout}");
                // 남은 항목이 다음 결정으로 넘어갈 수 있지만, 꺼낸 액션은 항상 현재 상태로
                // 재검증되므로(ResolveMove / CanPass / IsLegalExchange) 안전하다.
                return _matched.Dequeue();
            }
        }

        private Awaiting BuildAwaiting(StepKind kind, GameState state, int seat)
        {
            var aw = new Awaiting { Kind = kind, Seat = _seat };
            if (kind == StepKind.Play)
            {
                aw.LegalMoves = LegalMoveGenerator.LegalMoves(state, seat);
                aw.CanPass = LegalMoveGenerator.CanPass(state, seat);
            }
            return aw;
        }

        private static bool Matches(StepKind kind, ClientActionKind action) => kind switch
        {
            StepKind.GrandTichu => action == ClientActionKind.CallGrandTichu
                                || action == ClientActionKind.DeclineGrandTichu,
            StepKind.Exchange   => action == ClientActionKind.Exchange,
            StepKind.Play       => action == ClientActionKind.Play || action == ClientActionKind.Pass,
            StepKind.DragonGift => action == ClientActionKind.GiveDragon,
            _ => false,
        };

        private static bool IsLegalExchange(GameState state, int seat, ClientAction a)
        {
            if (a.ToLeft.Equals(a.ToPartner) || a.ToLeft.Equals(a.ToRight) || a.ToPartner.Equals(a.ToRight))
                return false;

            var hand = new List<Card>(state.Seats[seat].Hand);
            return Take(hand, a.ToLeft) && Take(hand, a.ToPartner) && Take(hand, a.ToRight);
        }

        private static bool Take(List<Card> hand, Card c)
        {
            int i = hand.IndexOf(c);
            if (i < 0) return false;
            hand.RemoveAt(i);
            return true;
        }

        private void Reject(ClientAction a, string reason) =>
            _send(_seat, ServerEvent.ActionRejected(a.ActionSeq, reason));
    }
}
```

- [ ] **Step 9: 테스트 통과 확인**

Run: `dotnet test core/Tichu.sln -c Release --nologo --filter "FullyQualifiedName~RemoteAgentTests"`
Expected: PASS (10 tests)

- [ ] **Step 10: 미러 + 전체 회귀 + 커밋**

```bash
pwsh -File tools/sync-core-to-unity.ps1 && pwsh -File tools/sync-core-to-unity.ps1 -Check
dotnet test core/Tichu.sln -c Release --nologo
git add core/src/Tichu.Net Assets/_Project/Net core/tests/Tichu.Core.Tests/MoveResolverTests.cs core/tests/Tichu.Core.Tests/RemoteAgentTests.cs
git commit -m "feat(mp): MoveResolver · RemoteAgent — 원격 좌석과 서버측 검증

RemoteAgent 는 IAgent 를 구현해 드라이버에게 평범한 에이전트로 보인다.
세 우편함(대기 슬롯 · 선행 큐 · 예약)을 써서, 묻기 전에 온 액션도
보관하므로 교환 4인 동시 제출이 자연히 동작한다.

불법 입력은 상태를 바꾸지 않고 발신자에게만 ActionRejected 를 보낸 뒤
계속 기다린다(기획서 7.7) — 이 클래스가 서버측 검증 관문이다.

MoveResolver 는 합법수 목록에서 카드 집합(+Rank)으로 찾는 방식이라
봉황 대체·소원 강제·폭탄 인터럽트가 자동으로 지켜지고 조합 인식이
엔진과 항상 일치한다.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: `MatchSession` — 권위 세션과 오라클

**Files:**
- Create: `core/src/Tichu.Net/MatchSession.cs`
- Test: `core/tests/Tichu.Core.Tests/MatchSessionOracleTests.cs`

**Interfaces:**
- Consumes: Task 2 `ViewProjector`, Task 3 `ITichuTransport`/`ServerEvent`, Task 4 `GameDriver` 훅, Task 5 `RemoteAgent`/`Awaiting`/`MoveResolver`.
- Produces:
  - `Tichu.Net.MatchSession` — 생성자 `MatchSession(IAgent?[] aiSeats, ITichuTransport transport, ulong masterSeed, int targetScore = 1000, System.TimeSpan? turnTimeout = null, System.Action<RoundOutcome>? onRoundCompleted = null)`. `aiSeats[i] == null` 이면 그 좌석은 원격이고 내부에서 `RemoteAgent`를 만든다.
  - 공개 메서드: `void Start()` · `void Stop()` · `void Join(System.TimeSpan timeout)` · `void Submit(ClientAction a)`.

- [ ] **Step 1: 실패하는 오라클 테스트 작성**

`core/tests/Tichu.Core.Tests/MatchSessionOracleTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Tichu.Core;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// MatchSession 오라클 — AI 4좌석을 세션 경로로 돌린 라운드가 GameDriver 직접
    /// 실행과 상태 해시·액션 로그까지 동일해야 한다. 세션이 게임 규칙에 끼어들지
    /// 않는다는 증명이다.
    /// </summary>
    public class MatchSessionOracleTests
    {
        private static IAgent?[] FourScripted() => new IAgent?[]
        {
            new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent()
        };

        private static RoundOutcome RunFirstRoundThroughSession(ulong masterSeed)
        {
            var transport = new LoopbackTransport();
            RoundOutcome? captured = null;
            using var done = new ManualResetEventSlim(false);

            var session = new MatchSession(
                FourScripted(), transport, masterSeed,
                targetScore: 1,
                onRoundCompleted: o => { if (captured == null) { captured = o; done.Set(); } });

            session.Start();
            Assert.That(done.Wait(TimeSpan.FromSeconds(20)), Is.True, "세션이 첫 라운드를 끝내지 못했다");
            session.Stop();
            session.Join(TimeSpan.FromSeconds(5));

            Assert.That(captured, Is.Not.Null);
            return captured!;
        }

        [TestCase(1UL)]
        [TestCase(42UL)]
        [TestCase(12345UL)]
        public void Session_round_is_bit_identical_to_direct_driver(ulong masterSeed)
        {
            var master = new Rng(masterSeed);
            ulong roundSeed = master.NextULong();

            var direct = new GameDriver(new IAgent[]
            {
                new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent()
            }).RunRound(GameEngine.NewRound(roundSeed));

            var viaSession = RunFirstRoundThroughSession(masterSeed);

            Assert.That(viaSession.State.ComputeHash(), Is.EqualTo(direct.State.ComputeHash()),
                "세션 경로의 상태 해시가 직접 실행과 다르다");
            Assert.That(viaSession.Log.Count, Is.EqualTo(direct.Log.Count), "로그 길이가 다르다");
            for (int i = 0; i < direct.Log.Count; i++)
            {
                Assert.That(viaSession.Log[i].Kind, Is.EqualTo(direct.Log[i].Kind), $"로그 {i} 종류");
                Assert.That(viaSession.Log[i].Seat, Is.EqualTo(direct.Log[i].Seat), $"로그 {i} 좌석");
            }
        }

        [Test]
        public void Every_seat_receives_a_state_update_per_applied_action()
        {
            var transport = new LoopbackTransport();
            RoundOutcome? captured = null;
            using var done = new ManualResetEventSlim(false);

            var session = new MatchSession(
                FourScripted(), transport, 42UL,
                targetScore: 1,
                onRoundCompleted: o => { if (captured == null) { captured = o; done.Set(); } });

            session.Start();
            Assert.That(done.Wait(TimeSpan.FromSeconds(20)), Is.True);
            session.Stop();
            session.Join(TimeSpan.FromSeconds(5));

            for (int seat = 0; seat < 4; seat++)
            {
                int updates = 0;
                foreach (var ev in transport.Inbox(seat))
                    if (ev.Kind == ServerEventKind.StateUpdate) updates++;

                Assert.That(updates, Is.GreaterThanOrEqualTo(captured!.Log.Count),
                    $"seat {seat} 가 받은 StateUpdate 가 적용 액션 수보다 적다");
            }
        }

        [Test]
        public void Masking_holds_in_every_broadcast_state_update()
        {
            var transport = new LoopbackTransport();
            using var done = new ManualResetEventSlim(false);

            var session = new MatchSession(
                FourScripted(), transport, 7UL,
                targetScore: 1,
                onRoundCompleted: _ => done.Set());

            session.Start();
            Assert.That(done.Wait(TimeSpan.FromSeconds(20)), Is.True);
            session.Stop();
            session.Join(TimeSpan.FromSeconds(5));

            for (int seat = 0; seat < 4; seat++)
                foreach (var ev in transport.Inbox(seat))
                {
                    if (ev.Kind != ServerEventKind.StateUpdate) continue;
                    Assert.That(ev.View!.Viewer, Is.EqualTo(seat), "뷰가 다른 좌석에게 갔다");
                    Assert.That(ev.View.MyHand.Count, Is.EqualTo(ev.View.HandCounts[seat]),
                        "자기 손패 장수가 공개 장수와 다르다");
                }
        }

        [Test]
        public void Submit_to_an_ai_seat_is_rejected()
        {
            var transport = new LoopbackTransport();
            var session = new MatchSession(FourScripted(), transport, 42UL, targetScore: 1);

            session.Submit(ClientAction.Pass(seat: 1, actionSeq: 1L));

            var inbox = transport.Inbox(1);
            Assert.That(inbox.Count, Is.EqualTo(1));
            Assert.That(inbox[0].Kind, Is.EqualTo(ServerEventKind.ActionRejected));
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test core/Tichu.sln -c Release --nologo --filter "FullyQualifiedName~MatchSessionOracleTests"`
Expected: 빌드 실패 — `CS0246: The type or namespace name 'MatchSession' could not be found`

- [ ] **Step 3: `MatchSession` 구현**

`core/src/Tichu.Net/MatchSession.cs`:

```csharp
using System;
using System.Threading;
using Tichu.Core;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;

namespace Tichu.Net
{
    /// <summary>
    /// 한 매치의 권위 세션. 전용 스레드 하나에서 기존 동기 GameDriver 를 돌리고,
    /// 모든 엔진 변이는 그 스레드에서만 일어난다. 원격 좌석은 RemoteAgent 로,
    /// 나머지는 주입된 AI 에이전트로 채워진다 — 드라이버는 둘을 구분하지 않는다.
    ///
    /// 매 액션 적용 직후 좌석별로 마스킹된 뷰를 방송한다. "지금 누구에게 묻는지"는
    /// RemoteAgent 가 대기에 들어갈 때 알려 주며(드라이버 내부 카운터를 볼 수 없다),
    /// 대기 좌석에게만 합법수를 함께 싣는다.
    /// </summary>
    public sealed class MatchSession
    {
        private const int SeatCount = 4;

        private readonly ITichuTransport _transport;
        private readonly ulong _masterSeed;
        private readonly int _targetScore;
        private readonly Action<RoundOutcome>? _onRoundCompleted;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private readonly IAgent[] _agents = new IAgent[SeatCount];
        private readonly RemoteAgent?[] _remote = new RemoteAgent?[SeatCount];

        private Thread? _thread;
        private long _arrival;
        private int _teamA;
        private int _teamB;

        /// <param name="aiSeats">좌석별 AI 에이전트. null 인 좌석은 원격 플레이어다.</param>
        /// <param name="turnTimeout">원격 좌석 1회 대기 상한. 기본 무한(만료 정책은 M5).</param>
        /// <param name="onRoundCompleted">라운드 완료 훅. 오라클 검증과 이후 리플레이·로깅이 쓴다.</param>
        public MatchSession(
            IAgent?[] aiSeats,
            ITichuTransport transport,
            ulong masterSeed,
            int targetScore = 1000,
            TimeSpan? turnTimeout = null,
            Action<RoundOutcome>? onRoundCompleted = null)
        {
            if (aiSeats == null || aiSeats.Length != SeatCount)
                throw new ArgumentException("exactly 4 seats required", nameof(aiSeats));

            _transport = transport;
            _masterSeed = masterSeed;
            _targetScore = targetScore;
            _onRoundCompleted = onRoundCompleted;

            var timeout = turnTimeout ?? Timeout.InfiniteTimeSpan;
            for (int seat = 0; seat < SeatCount; seat++)
            {
                if (aiSeats[seat] != null) { _agents[seat] = aiSeats[seat]!; continue; }

                var remote = new RemoteAgent(
                    seat,
                    (s, ev) => _transport.SendTo(s, ev),
                    OnAwait,
                    NextArrival,
                    timeout,
                    _cts.Token);
                _remote[seat] = remote;
                _agents[seat] = remote;
            }

            _transport.OnClientAction(Submit);
        }

        /// <summary>세션 스레드를 띄운다.</summary>
        public void Start()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "TichuMatchSession" };
            _thread.Start();
        }

        /// <summary>세션을 중단한다. 대기 중인 원격 좌석의 블로킹도 풀린다.</summary>
        public void Stop() => _cts.Cancel();

        /// <summary>세션 스레드가 끝날 때까지 기다린다(테스트·종료 처리용).</summary>
        public void Join(TimeSpan timeout) => _thread?.Join(timeout);

        /// <summary>클라이언트 액션을 해당 좌석의 원격 에이전트로 보낸다.</summary>
        public void Submit(ClientAction a)
        {
            if (a.Seat < 0 || a.Seat >= SeatCount)
            {
                _transport.SendTo(0, ServerEvent.ActionRejected(a.ActionSeq, "seat out of range"));
                return;
            }

            var remote = _remote[a.Seat];
            if (remote == null)
            {
                _transport.SendTo(a.Seat, ServerEvent.ActionRejected(a.ActionSeq, "seat is not remote"));
                return;
            }

            remote.Submit(a);
        }

        private long NextArrival() => Interlocked.Increment(ref _arrival);

        private void Run()
        {
            var master = new Rng(_masterSeed);
            var driver = new GameDriver(_agents);

            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    ulong roundSeed = master.NextULong();
                    var state = GameEngine.NewRound(roundSeed);

                    Broadcast(state, null);
                    var outcome = driver.RunRound(state, OnApply, TakePendingTichu, TakePendingBomb);

                    _teamA += outcome.Result.TeamATotal;
                    _teamB += outcome.Result.TeamBTotal;

                    for (int seat = 0; seat < SeatCount; seat++)
                        _transport.SendTo(seat, ServerEvent.RoundScored(outcome.Result));

                    _onRoundCompleted?.Invoke(outcome);

                    var decision = MatchRunner.Decide(_teamA, _teamB, _targetScore);
                    if (decision == MatchDecision.Continue) continue;

                    int winner = decision == MatchDecision.TeamAWins ? 0 : 1;
                    for (int seat = 0; seat < SeatCount; seat++)
                        _transport.SendTo(seat, ServerEvent.MatchEnded(winner, _teamA, _teamB));
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                // Stop() — 정상 종료.
            }
            catch (Exception e)
            {
                for (int seat = 0; seat < SeatCount; seat++)
                    _transport.SendTo(seat, ServerEvent.SessionFaulted(e.Message));
            }
        }

        private void OnApply(GameState s, GameAction a) => Broadcast(s, null);

        /// <summary>RemoteAgent 가 대기에 들어갔다. 대기 좌석엔 합법수까지, 나머지엔 축약판을 보낸다.</summary>
        private void OnAwait(int seat, Awaiting awaiting)
        {
            var redacted = awaiting.Redacted();
            for (int i = 0; i < SeatCount; i++)
                _transport.SendTo(i, ServerEvent.StateUpdate(
                    ViewProjector.ProjectFor(CurrentState!, i), i == seat ? awaiting : redacted));
        }

        private void Broadcast(GameState s, Awaiting? awaiting)
        {
            CurrentState = s;
            for (int seat = 0; seat < SeatCount; seat++)
                _transport.SendTo(seat, ServerEvent.StateUpdate(ViewProjector.ProjectFor(s, seat), awaiting));
        }

        /// <summary>세션 스레드 전용. OnAwait 가 투영에 쓴다.</summary>
        private GameState? CurrentState { get; set; }

        private int TakePendingTichu(GameState s)
        {
            for (int seat = 0; seat < SeatCount; seat++)
                if (_remote[seat] != null && _remote[seat]!.TakeReservedTichu())
                    return seat;
            return -1;
        }

        /// <summary>
        /// 예약된 차례밖 폭탄 중 가장 먼저 도착한 것을 적용 가능한 형태로 돌려준다.
        /// 도착 순서로 직렬화한다(기획서 7.4). 자기 차례인 좌석의 예약은 건드리지 않는다 —
        /// 그건 드라이버가 그 좌석에게 물을 때 Await 가 직접 꺼낸다.
        /// </summary>
        private GameAction? TakePendingBomb(GameState s)
        {
            int bestSeat = -1;
            long bestArrival = long.MaxValue;

            for (int seat = 0; seat < SeatCount; seat++)
            {
                if (seat == s.Turn) continue;
                var remote = _remote[seat];
                if (remote?.PeekReservedBomb() == null) continue;

                long arrival = remote.ReservedBombArrival();
                if (arrival >= bestArrival) continue;
                bestArrival = arrival;
                bestSeat = seat;
            }

            if (bestSeat < 0) return null;

            var pending = _remote[bestSeat]!.PeekReservedBomb()!;
            var move = MoveResolver.ResolveMove(s, bestSeat, pending);
            if (move == null || !move.IsBomb) return null; // 아직 합법이 아니면 예약을 유지한다

            _remote[bestSeat]!.ConsumeReservedBomb();
            return GameAction.Play(bestSeat, move.Cards);
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test core/Tichu.sln -c Release --nologo --filter "FullyQualifiedName~MatchSessionOracleTests"`
Expected: PASS (7 tests — `TestCase` 3건 + 단일 4건)

- [ ] **Step 5: 미러 + 전체 회귀 + 커밋**

```bash
pwsh -File tools/sync-core-to-unity.ps1 && pwsh -File tools/sync-core-to-unity.ps1 -Check
dotnet test core/Tichu.sln -c Release --nologo
git add core/src/Tichu.Net Assets/_Project/Net core/tests/Tichu.Core.Tests/MatchSessionOracleTests.cs
git commit -m "feat(mp): MatchSession — 권위 세션과 오라클 비트 동일성

전용 스레드 하나에서 기존 동기 GameDriver 를 돌린다. 원격 좌석은
RemoteAgent, 나머지는 주입된 AI — 드라이버는 둘을 구분하지 않는다.

오라클: AI 4좌석을 세션 경로로 돌린 라운드가 GameDriver 직접 실행과
상태 해시·액션 로그까지 같다(시드 3종). 세션이 게임 규칙에 끼어들지
않는다는 증명이다.

차례밖 폭탄은 도착 순서로 직렬화한다(기획서 7.4). 아직 합법이 아니면
예약을 소비하지 않고 유지한다.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 7: 루프백 매치 완주 — 스크립트 인간 2 + AI 2

**Files:**
- Create: `core/tests/Tichu.Core.Tests/ScriptedClient.cs`
- Test: `core/tests/Tichu.Core.Tests/LoopbackMatchTests.cs`

**Interfaces:**
- Consumes: Task 3 `LoopbackTransport`/`ClientAction`/`ServerEvent`, Task 5 `Awaiting`, Task 6 `MatchSession`.
- Produces: `Tichu.Core.Tests.ScriptedClient`(internal, 테스트 전용) — 생성자 `ScriptedClient(int seat, LoopbackTransport transport)` · `int Seat { get; }` · `void Pump()`(수신 이벤트 처리 + 응답 제출) · `bool SubmitOneIllegalAction { get; set; }` · `int RejectionsSeen { get; }` · `bool MatchEnded { get; }` · `string? FaultReason { get; }`. 이후 태스크 없음(M1 종료).

- [ ] **Step 1: 실패하는 통합 테스트 작성**

`core/tests/Tichu.Core.Tests/LoopbackMatchTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Tichu.GameFlow.Agents;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// 루프백 통합 — 스크립트 인간 2 + AI 2 로 매치를 끝까지 돌린다.
    /// 적용된 모든 액션이 엔진 검증을 통과했다는 것은 세션이 throw 없이 MatchEnded 까지
    /// 갔다는 사실로 증명된다(드라이버는 불법 액션에 throw 한다).
    /// </summary>
    public class LoopbackMatchTests
    {
        /// <summary>
        /// 클라이언트를 돌리며 매치가 끝날 때까지 기다린다. 클라이언트가 없는 좌석의
        /// 수신함도 비워 테스트 메모리가 무한히 자라지 않게 한다.
        /// </summary>
        private static void RunUntilEnd(
            MatchSession session, LoopbackTransport transport, ScriptedClient[] clients, TimeSpan budget)
        {
            var clientSeats = new HashSet<int>();
            foreach (var c in clients) clientSeats.Add(c.Seat);

            session.Start();
            try
            {
                var deadline = DateTime.UtcNow + budget;
                while (DateTime.UtcNow < deadline)
                {
                    foreach (var c in clients) c.Pump();
                    for (int seat = 0; seat < 4; seat++)
                        if (!clientSeats.Contains(seat)) transport.Drain(seat);

                    foreach (var c in clients)
                    {
                        if (c.FaultReason != null) Assert.Fail($"세션이 실패했다: {c.FaultReason}");
                        if (c.MatchEnded) return;
                    }

                    Thread.Sleep(2);
                }
            }
            finally
            {
                session.Stop();
                session.Join(TimeSpan.FromSeconds(5));
            }
        }

        [Test]
        public void Two_scripted_humans_and_two_ai_finish_a_match()
        {
            var transport = new LoopbackTransport();

            // 좌석 0·2 = 원격(인간), 좌석 1·3 = AI.
            var seats = new IAgent?[] { null, new ScriptedAgent(), null, new ScriptedAgent() };

            var session = new MatchSession(
                seats, transport, masterSeed: 42UL,
                targetScore: 400,
                turnTimeout: TimeSpan.FromSeconds(30));

            var clients = new[] { new ScriptedClient(0, transport), new ScriptedClient(2, transport) };

            RunUntilEnd(session, transport, clients, TimeSpan.FromSeconds(90));

            Assert.That(clients[0].MatchEnded, Is.True, "매치가 목표 점수에 도달하지 못했다");
        }

        [Test]
        public void Illegal_submission_is_rejected_without_stalling_the_match()
        {
            var transport = new LoopbackTransport();
            var seats = new IAgent?[] { null, new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent() };

            var session = new MatchSession(
                seats, transport, masterSeed: 7UL,
                targetScore: 400,
                turnTimeout: TimeSpan.FromSeconds(30));

            var client = new ScriptedClient(0, transport) { SubmitOneIllegalAction = true };

            RunUntilEnd(session, transport, new[] { client }, TimeSpan.FromSeconds(90));

            Assert.That(client.MatchEnded, Is.True, "불법 제출 뒤에도 매치가 끝까지 가야 한다");
            Assert.That(client.RejectionsSeen, Is.GreaterThanOrEqualTo(1), "거부 통보를 못 받았다");
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test core/Tichu.sln -c Release --nologo --filter "FullyQualifiedName~LoopbackMatchTests"`
Expected: 빌드 실패 — `CS0246: The type or namespace name 'ScriptedClient' could not be found`

- [ ] **Step 3: `ScriptedClient` 구현**

`core/tests/Tichu.Core.Tests/ScriptedClient.cs`:

```csharp
using System.Collections.Generic;
using Tichu.Core.Cards;
using Tichu.GameFlow;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// 테스트용 얇은 클라이언트. PlayerView 와 Awaiting 만 보고 행동한다 —
    /// 서버가 대기 좌석에게 합법수를 실어 주므로 GameState 를 복원할 필요가 없다.
    ///
    /// 전략: 큰 티츄 패스 · 교환은 손패 앞 3장 · 차례가 오면 합법수 첫 번째(없으면 패스) ·
    /// 용 양도는 왼쪽 상대. 강한 플레이가 목적이 아니라 합법적으로 매치를 완주하는 것이 목적이다.
    /// </summary>
    internal sealed class ScriptedClient
    {
        private readonly int _seat;
        private readonly LoopbackTransport _transport;

        private long _seq;
        private bool _illegalSent;

        /// <summary>true 면 첫 차례에 불법 액션을 한 번 보내 거부 경로를 태운다.</summary>
        public bool SubmitOneIllegalAction { get; set; }

        public int RejectionsSeen { get; private set; }

        /// <summary>MatchEnded 를 받았는가.</summary>
        public bool MatchEnded { get; private set; }

        /// <summary>SessionFaulted 사유. 정상이면 null.</summary>
        public string? FaultReason { get; private set; }

        /// <summary>이 클라이언트가 담당하는 좌석.</summary>
        public int Seat => _seat;

        public ScriptedClient(int seat, LoopbackTransport transport)
        {
            _seat = seat;
            _transport = transport;
        }

        /// <summary>아직 처리하지 않은 이벤트를 꺼내 처리한다.</summary>
        public void Pump()
        {
            foreach (var ev in _transport.Drain(_seat))
            {
                switch (ev.Kind)
                {
                    case ServerEventKind.ActionRejected: RejectionsSeen++; continue;
                    case ServerEventKind.MatchEnded:     MatchEnded = true; continue;
                    case ServerEventKind.SessionFaulted: FaultReason = ev.Reason; continue;
                    case ServerEventKind.StateUpdate:    break;
                    default:                             continue;
                }

                var view = ev.View!;
                var awaiting = view.Awaiting;
                if (awaiting == null || awaiting.Seat != _seat) continue;

                Respond(view, awaiting);
            }
        }

        private void Respond(PlayerView view, Awaiting awaiting)
        {
            switch (awaiting.Kind)
            {
                case StepKind.GrandTichu:
                    _transport.Submit(ClientAction.DeclineGrandTichu(_seat, ++_seq));
                    break;

                case StepKind.Exchange:
                    var h = view.MyHand;
                    _transport.Submit(ClientAction.Exchange(_seat, ++_seq, h[0], h[1], h[2]));
                    break;

                case StepKind.Play:
                    SubmitPlay(awaiting);
                    break;

                case StepKind.DragonGift:
                    _transport.Submit(ClientAction.GiveDragon(_seat, ++_seq, (_seat + 1) % 4));
                    break;
            }
        }

        private void SubmitPlay(Awaiting awaiting)
        {
            var moves = awaiting.LegalMoves;

            if (SubmitOneIllegalAction && !_illegalSent && moves != null && moves.Count > 0)
            {
                _illegalSent = true;
                // 합법수의 카드를 그대로 쓰고 Rank 만 존재할 수 없는 값으로 보낸다 → 반드시 불법.
                // (손에 없을 것 같은 카드를 보내는 방식은 실제로 들고 있으면 합법이 되어 버린다.)
                _transport.Submit(ClientAction.Play(_seat, ++_seq,
                    new List<Card>(moves[0].Cards), rank: int.MaxValue, wish: null));
                // 여기서 끝낸다. 거부되면 에이전트가 다시 대기에 들어가며 새 Awaiting 을
                // 방송하므로 합법수는 다음 Pump 에서 보낸다 — 거부 통보를 삼키지 않는다.
                return;
            }

            if (moves != null && moves.Count > 0)
            {
                var m = moves[0];
                // 마작 리드 시 소원을 걸 수 있지만 단순화를 위해 걸지 않는다.
                _transport.Submit(ClientAction.Play(_seat, ++_seq, new List<Card>(m.Cards), m.Rank, wish: null));
                return;
            }

            _transport.Submit(ClientAction.Pass(_seat, ++_seq));
        }
    }
}
```

- [ ] **Step 4: `ScriptedClient`를 미러에서 제외**

`ScriptedClient.cs`는 테스트 트리 파일이라 기본적으로 유니티로 미러된다. 유니티 EditMode에서도 컴파일되므로 **제외할 필요가 없다** — `tools/sync-core-to-unity.ps1`의 `Exclude` 목록은 벤치·트레이너용이고 이 파일은 거기 해당하지 않는다. 미러만 실행한다.

```bash
pwsh -File tools/sync-core-to-unity.ps1 && pwsh -File tools/sync-core-to-unity.ps1 -Check
```

- [ ] **Step 5: 테스트 통과 확인**

Run: `dotnet test core/Tichu.sln -c Release --nologo --filter "FullyQualifiedName~LoopbackMatchTests"`
Expected: PASS (2 tests)

실패하면 먼저 `SessionFaulted`의 `Reason`을 본다. "did not act within"이면 클라이언트가 어떤 `Awaiting`에 응답하지 않은 것이고, `illegal action ...`이면 `MoveResolver`가 통과시킨 수를 엔진이 거부한 것이다(후자는 `MoveResolver` 버그이므로 Task 5로 돌아간다).

- [ ] **Step 6: M1 DoD 전체 확인**

```bash
dotnet test core/Tichu.sln -c Release --nologo
pwsh -File tools/sync-core-to-unity.ps1 -Check
```

Expected: 전부 PASS + `Mirror is in sync.` 다음 네 가지가 모두 충족된다.

1. **오라클** — `MatchSessionOracleTests.Session_round_is_bit_identical_to_direct_driver` 3건 PASS
2. **마스킹 불변식** — `ViewProjectorTests.No_other_seat_hand_card_appears_anywhere_in_the_view` + `MatchSessionOracleTests.Masking_holds_in_every_broadcast_state_update` PASS
3. **루프백 완주** — `LoopbackMatchTests` 2건 PASS
4. **회귀 0** — 기존 테스트 전부 그린, 미러 동기

- [ ] **Step 7: 커밋**

```bash
git add core/tests/Tichu.Core.Tests/ScriptedClient.cs core/tests/Tichu.Core.Tests/LoopbackMatchTests.cs Assets/_Project/Tests/EditMode
git commit -m "test(mp): 루프백 매치 완주 — 스크립트 인간 2 + AI 2

얇은 테스트 클라이언트가 PlayerView 와 Awaiting 만 보고 행동한다.
서버가 대기 좌석에게 합법수를 실어 주므로 클라이언트가 GameState 를
복원할 필요가 없다 — 멀티플레이 클라이언트의 실제 모양이기도 하다.

불법 제출을 한 번 끼워 거부 경로도 태웠다. 거부 뒤에도 매치가 끝까지
가야 한다(상태 불변 + 계속 대기).

이로써 M1 DoD 4항목이 전부 충족된다: 오라클 비트 동일성 · 마스킹
불변식 · 루프백 완주 · 회귀 0.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## M1 완료 후 상태

`core/`에 멀티플레이 전체 골격이 들어가 있고, 네트워크·서버·Unity는 하나도 건드리지 않았다. 다음 조각은 스펙 §10의 M2(직렬화 + WebSocket 서버 + 호스팅)이며, `ITichuTransport` 뒤에 끼우는 작업이라 게임·UI 코드는 손대지 않는다.

M1에서 의도적으로 남긴 것:
- `Awaiting.DeadlineUtc`는 항상 `null` — 턴 시계와 만료 정책은 M5.
- `RemoteAgent` 타임아웃 정책은 `TimeoutException` → 세션이 `SessionFaulted`를 방송하고 종료. 자동 패스·오토플레이·AI 인계는 M5.
- `ActionSeq` 중복 수신 시 현재 상태 재전송은 M2(실제 전송이 붙어야 중복이 생긴다).
- 교환 제출 현황 표시는 M4(`RoundSetup`이 `internal`이라 공개 접근자 또는 `InternalsVisibleTo` 결정이 필요).
- 싱글플레이를 세션 경로로 재배선하는 작업은 M4.
