# P2-A — PIMC 탐색 골격 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 현 휴리스틱(`AiAgent`)을 디폴트 정책으로 재사용하는 **단일-세계 PIMC 탐색 배선**을 만든다 — 결정화(`Determinizer`) + 롤아웃(`Pimc.Rollout`) + 탐색 에이전트(`PimcAgent`), 강도는 현 수준이되 탐색 파이프라인이 결정적·합법·치트프리임을 TDD로 못박는다.

**Architecture:** `PimcAgent.DecideTurn`만 탐색한다 — 관측 좌석의 미관측 손패를 1개 "세계"로 결정화하고, 각 루트 합법수를 적용한 뒤 `GameDriver.RunRound`로 현 휴리스틱(`AiAgent`)을 끝까지 롤아웃해 `ScoreRound` 팀점수차를 보상으로 받는다. 나머지 6개 결정 메서드는 내부 `AiAgent`에 위임한다. 신규 추상 경계는 기존 `IAgent` 하나뿐 — 드라이버/룰엔진/매치러너는 한 줄도 바뀌지 않는다.

**Tech Stack:** C# (Unity 6000.3, `Tichu.GameFlow` asmdef, `noEngineReferences=true`) · NUnit EditMode 테스트 · UnityMCP `run_tests`.

## Global Constraints

설계 스펙: `docs/superpowers/specs/2026-06-24-tichu-phase2-ai-pimc-design.md`. 모든 태스크는 아래를 암묵적으로 포함한다.

- **신규 asmdef 0개.** 탐색 코어 3파일은 전부 기존 `Assets/_Project/GameFlow/Agents/` (asmdef `Tichu.GameFlow`, `noEngineReferences=true`, `references=["Tichu.Core"]`) 아래, 네임스페이스 `Tichu.GameFlow.Agents`. 테스트는 기존 `Assets/_Project/Tests/EditMode/` (asmdef `Tichu.Core.Tests`, 네임스페이스 `Tichu.Core.Tests`).
- **UnityEngine 비의존.** `Tichu.GameFlow`는 `noEngineReferences=true`라 `UnityEngine` 타입을 쓸 수 없다(컴파일 차단). 순수 C#만.
- **결정성:** 모든 무작위는 주입된 `Tichu.Core.Rng`(struct SplitMix64)에서만. `System.Random`/`System.DateTime`/`Stopwatch` 사용 금지. `Rng`는 struct이므로 **로컬로 꺼내 전진 후 되쓰기**(`var local = field; …; field = local`), 또는 `ref Rng`로 전달. 필드/프로퍼티 직접 호출 금지.
- **치트 가드(CRITICAL):** `Determinizer`는 관측 좌석 외 `Seats[*].Hand`를 **읽어서는 안 된다**. 관측 좌석 손패·공개정보는 불변, 상대 손패는 미관측 풀에서만 재분배. 단위테스트로 강제.
- **보상 부호:** 관측 좌석 팀 기준. `Seating.TeamOf(seat)==0`이면 `TeamATotal − TeamBTotal`, 아니면 `TeamBTotal − TeamATotal`. 높을수록 관측자에게 유리.
- **에이전트 생성자 시그니처:** `(ulong roundSeed, int seat)` — 기존 `AiAgent`/`RandomAgent`와 동일해야 `MatchRunner.RunMatch`의 `agentFactory`에 그대로 꽂힌다.
- **⚠️ 테스트 실행 규율:** `run_tests`는 **클래스 필터로만** (`test_names=["DeterminizerTests"]` 등). **전체 `Tichu.Core.Tests` 실행 금지**(`SimulationTests`의 10만판이 메인스레드 점유 → MCP/에디터 stuck). `run_tests` 전 **PlayMode 정지 필수**.
- **⚠️ 신규 .cs 임포트:** 새 스크립트는 `execute_code`로 `AssetDatabase.ImportAsset(path, ForceUpdate)` + `AssetDatabase.Refresh()` 후 컴파일해야 한다(`refresh_unity`만으론 임포트 누락). 컴파일 후 `read_console`로 에러 0 확인 후에만 새 타입 사용.
- **오라클 불침범:** 탐색 AI는 기존 동기/비동기 `ComputeHash` 오라클 경로에 넣지 않는다(P2-A는 그 경로를 건드리지 않음). 기존 EditMode 그린 유지.

---

## File Structure

| 파일 | 책임 |
|---|---|
| `Assets/_Project/GameFlow/Agents/Determinizer.cs` (생성) | 미관측 손패를 룰 제약(56장 폐쇄·공개 hand-count 일치) 충족하게 1개 세계로 샘플. 치트 가드의 단일 출처. |
| `Assets/_Project/GameFlow/Agents/Pimc.cs` (생성) | `Rollout` — 완전정보 세계를 현 휴리스틱(`AiAgent`)으로 끝까지 플레이해 관측 팀 점수차 보상 반환. |
| `Assets/_Project/GameFlow/Agents/PimcAgent.cs` (생성) | `IAgent` 구현. `DecideTurn`만 단일-세계 PIMC 탐색, 나머지는 내부 `AiAgent` 위임. |
| `Assets/_Project/Tests/EditMode/DeterminizerTests.cs` (생성) | 폐쇄·hand-count·관측 불변·치트 가드·결정성·apply-합법. |
| `Assets/_Project/Tests/EditMode/PimcAgentTests.cs` (생성) | 롤아웃 정확성·롤아웃 결정성·합법수 반환·결정성·위임·풀라운드 완주. |

각 `.cs` 생성 시 Unity가 `.cs.meta`를 자동 생성한다(수동 생성 불필요).

---

## Deviation from Spec (의도적 단순화 — CLAUDE.md "단순함 우선")

스펙 §11 P2-A는 "현 휴리스틱을 `IPolicy` 디폴트 정책으로 추출"을 적었으나, **본 계획은 `IPolicy` 인터페이스를 도입하지 않는다.** 이유:

- 롤아웃은 `GameDriver.RunRound`를 재사용하며, 이는 `IAgent[]`를 받는다. 현 휴리스틱 `AiAgent`는 **이미 `IAgent`** 이므로 디폴트 정책으로 그대로 쓸 수 있다.
- P2-A는 단일 정책(휴리스틱)만 쓴다. 두 번째 정책(ε-노이즈)은 **P2-B에서 처음 필요**하다. 단일 구현뿐인 추상화는 CLAUDE.md "단일 사용 코드에 추상화 금지" 위반.
- **`IPolicy` 도입은 P2-B로 연기** — ε-노이즈 정책이 실제로 두 번째 구현체가 될 때 도입한다(그때가 추상화의 정당한 시점).

루트 수의 **마작 소원은 P2-A에서 `null`(소원 없음)로 고정**한다(합법). 소원 탐색은 후속 티어로 연기. 롤아웃 내부에서는 `AiAgent`가 자체 `MaybeWish`로 소원을 처리하므로 롤아웃 정확성에는 영향 없다.

---

### Task 1: Determinizer (결정화 + 치트 가드)

**Files:**
- Create: `Assets/_Project/GameFlow/Agents/Determinizer.cs`
- Test: `Assets/_Project/Tests/EditMode/DeterminizerTests.cs`

**Interfaces:**
- Consumes: `Tichu.Core.Game.GameState.Clone()`, `GameState.Seats[i].Hand/WonCards`, `GameState.CurrentTrick`, `GameState.CompletedTricks`; `Tichu.Core.Cards.Deck.CreateStandard()` / `Deck.Shuffle(IList<Card>, ref Rng)`; `Tichu.Core.Rng`; `Tichu.Core.Game.Play.Combination.Cards`.
- Produces: `public static GameState Determinizer.Sample(GameState src, int observerSeat, ref Rng rng)` — `src`를 변형하지 않고, 관측 좌석 외 손패만 미관측 풀에서 재분배한 **새 `GameState`** 반환. 폐쇄 불일치 시 `InvalidOperationException`.

- [ ] **Step 1: 실패 테스트 작성**

`Assets/_Project/Tests/EditMode/DeterminizerTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Tichu.Core;
using Tichu.Core.Cards;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    /// <summary>Determinizer: 56장 폐쇄·hand-count 일치·관측 불변·치트 가드·결정성.</summary>
    public class DeterminizerTests
    {
        // 56장을 4×14로 그대로 나눈 닫힌 Play 상태(셋업 우회). 모든 카드가 손패에 분포 → 폐쇄 성립.
        private static GameState FreshPlayState(int turn)
        {
            var deck = Deck.CreateStandard();            // 56장, 결정적 순서
            Assert.That(deck.Count, Is.EqualTo(56));
            var hands = new IReadOnlyList<Card>[4];
            for (int i = 0; i < 4; i++)
                hands[i] = deck.GetRange(i * 14, 14);
            return GameFlowHelpers.PlayState(turn, hands);
        }

        [Test]
        public void Sample_keeps_observer_hand_and_does_not_mutate_source()
        {
            var src = FreshPlayState(0);
            var observerBefore = new List<Card>(src.Seats[0].Hand);
            var opp1Before = new List<Card>(src.Seats[1].Hand);

            var rng = new Rng(42UL);
            var det = Determinizer.Sample(src, 0, ref rng);

            // 관측 좌석(0) 손패 불변.
            Assert.That(det.Seats[0].Hand, Is.EqualTo(observerBefore));
            // 원본 src 는 변형되지 않는다(상대 손패도 그대로).
            Assert.That(src.Seats[1].Hand, Is.EqualTo(opp1Before));
        }

        [Test]
        public void Sample_redistributes_full_56_card_closure_by_hand_counts()
        {
            var src = FreshPlayState(0);
            var rng = new Rng(7UL);
            var det = Determinizer.Sample(src, 0, ref rng);

            // 4 손패 합집합 == 56장 전체(중복 없음).
            var all = new HashSet<Card>();
            for (int i = 0; i < 4; i++) all.UnionWith(det.Seats[i].Hand);
            Assert.That(all.Count, Is.EqualTo(56), "결정화 후에도 56장 폐쇄가 유지돼야 한다");

            // 각 상대 좌석의 장수가 원본과 동일.
            for (int i = 1; i < 4; i++)
                Assert.That(det.Seats[i].Hand.Count, Is.EqualTo(src.Seats[i].Hand.Count));
        }

        [Test]
        public void Sample_draws_opponents_only_from_unseen_pool_no_cheating()
        {
            var src = FreshPlayState(0);
            // 미관측 풀 = 56장 − 관측 좌석(0) 손패.
            var observerSet = new HashSet<Card>(src.Seats[0].Hand);

            var rng = new Rng(123UL);
            var det = Determinizer.Sample(src, 0, ref rng);

            // 상대 좌석에 배정된 모든 카드는 관측자가 든 카드일 수 없다(미관측 풀 출신).
            for (int seat = 1; seat < 4; seat++)
                foreach (var c in det.Seats[seat].Hand)
                    Assert.That(observerSet.Contains(c), Is.False,
                        $"치트: 상대 {seat} 가 관측자 손패 카드 {c} 를 받았다");

            // 상대에 배정된 카드 집합 == 미관측 풀 전체(빠짐/추가 없음).
            var dealt = new HashSet<Card>();
            for (int seat = 1; seat < 4; seat++) dealt.UnionWith(det.Seats[seat].Hand);
            Assert.That(dealt.Count, Is.EqualTo(56 - observerSet.Count));
        }

        [Test]
        public void Sample_is_deterministic_for_same_seed_and_varies_by_seed()
        {
            var src = FreshPlayState(0);

            var r1 = new Rng(99UL); var a = Determinizer.Sample(src, 0, ref r1);
            var r2 = new Rng(99UL); var b = Determinizer.Sample(src, 0, ref r2);
            for (int seat = 1; seat < 4; seat++)
                Assert.That(a.Seats[seat].Hand, Is.EqualTo(b.Seats[seat].Hand),
                    "같은 시드 → 같은 분배");

            var r3 = new Rng(100UL); var c = Determinizer.Sample(src, 0, ref r3);
            bool anyDiff = false;
            for (int seat = 1; seat < 4; seat++)
                if (!a.Seats[seat].Hand.SequenceEqual(c.Seats[seat].Hand)) anyDiff = true;
            Assert.That(anyDiff, Is.True, "다른 시드 → 분배가 달라져야 한다(42장 셔플)");
        }

        [Test]
        public void Sample_result_passes_engine_apply_for_a_legal_move()
        {
            var src = FreshPlayState(0);
            var rng = new Rng(5UL);
            var det = Determinizer.Sample(src, 0, ref rng);

            // 관측 좌석의 합법수 하나를 적용해도 엔진이 거부하지 않아야 한다.
            var legal = LegalMoveGenerator.LegalMoves(det, 0);
            Assert.That(legal.Count, Is.GreaterThan(0));
            var res = GameEngine.Apply(det, GameAction.Play(0, legal[0].Cards));
            Assert.That(res.Ok, Is.True, res.RejectReason);
        }
    }
}
```

- [ ] **Step 2: 실패 확인**

`Determinizer` 타입이 없어 **컴파일 실패**(green 불가)임을 확인. (UnityMCP) PlayMode 정지 후:
- `read_console` → `CS0103: The name 'Determinizer' does not exist` 류 에러 확인. (테스트 러너 실행 전이라도 컴파일 에러로 충분.)

- [ ] **Step 3: 최소 구현 작성**

`Assets/_Project/GameFlow/Agents/Determinizer.cs`:

```csharp
using System;
using System.Collections.Generic;
using Tichu.Core;
using Tichu.Core.Cards;
using Tichu.Core.Game;

namespace Tichu.GameFlow.Agents
{
    /// <summary>
    /// 미관측 손패를 1개 "세계"로 결정화한다.
    /// 관측 좌석의 손패·공개정보는 절대 변형하지 않으며(치트 방지),
    /// 미관측 풀(56장 − 관측 손패 − 공개된 모든 카드)을 상대 좌석의 공개된 장수에 맞게 재분배한다.
    /// 분배 결과는 56장 폐쇄를 유지하므로 GameEngine.Apply 가 거부하지 않는다.
    /// </summary>
    public static class Determinizer
    {
        /// <summary>
        /// src 의 복제본에서 observerSeat 외 좌석 손패를 미관측 풀로 재분배한 새 GameState 를 반환한다.
        /// src 는 변형하지 않는다. 56장 폐쇄가 맞지 않으면(불완전 상태) InvalidOperationException.
        /// </summary>
        public static GameState Sample(GameState src, int observerSeat, ref Rng rng)
        {
            var clone = src.Clone();

            // 1) 관측자에게 보이는 모든 카드(= 미관측 풀에서 제외할 카드).
            var visible = new HashSet<Card>();
            foreach (var c in clone.Seats[observerSeat].Hand) visible.Add(c);
            for (int i = 0; i < 4; i++)
                foreach (var c in clone.Seats[i].WonCards) visible.Add(c);
            if (clone.CurrentTrick != null)
                foreach (var p in clone.CurrentTrick.History)
                    if (p.Combination != null)
                        foreach (var c in p.Combination.Cards) visible.Add(c);
            foreach (var t in clone.CompletedTricks)
                foreach (var p in t.History)
                    if (p.Combination != null)
                        foreach (var c in p.Combination.Cards) visible.Add(c);

            // 2) 미관측 풀 = 표준 56장 − 보이는 카드.
            var pool = new List<Card>();
            foreach (var c in Deck.CreateStandard())
                if (!visible.Contains(c)) pool.Add(c);

            // 3) 폐쇄 불변식: 풀 크기 == 상대 좌석 손패 장수 합.
            int need = 0;
            for (int i = 0; i < 4; i++)
                if (i != observerSeat) need += clone.Seats[i].Hand.Count;
            if (need != pool.Count)
                throw new InvalidOperationException(
                    $"determinize closure mismatch: pool={pool.Count} need={need} (observer={observerSeat})");

            // 4) 풀 셔플(주입 Rng).
            Deck.Shuffle(pool, ref rng);

            // 5) 상대 좌석에 순차 분배(공개된 장수 정확히 일치).
            int idx = 0;
            for (int i = 0; i < 4; i++)
            {
                if (i == observerSeat) continue;
                int count = clone.Seats[i].Hand.Count;
                var newHand = new List<Card>(count);
                for (int k = 0; k < count; k++) newHand.Add(pool[idx++]);
                clone.Seats[i].Hand = newHand;
            }

            return clone;
        }
    }
}
```

- [ ] **Step 4: 임포트 + 컴파일 + 통과 확인**

(UnityMCP) PlayMode 정지 상태에서:
1. `execute_code` 로 `AssetDatabase.ImportAsset("Assets/_Project/GameFlow/Agents/Determinizer.cs", ImportAssetOptions.ForceUpdate)` + `AssetDatabase.ImportAsset(".../DeterminizerTests.cs", ForceUpdate)` + `AssetDatabase.Refresh()`.
2. `read_console` → 컴파일 에러 0 확인.
3. `run_tests(mode="EditMode", test_names=["DeterminizerTests"])`.

Expected: 5개 테스트 PASS.

- [ ] **Step 5: 커밋**

```bash
git add "Assets/_Project/GameFlow/Agents/Determinizer.cs" "Assets/_Project/GameFlow/Agents/Determinizer.cs.meta" "Assets/_Project/Tests/EditMode/DeterminizerTests.cs" "Assets/_Project/Tests/EditMode/DeterminizerTests.cs.meta"
git commit -m "feat(p2a): Determinizer — 56장 폐쇄 결정화 + 치트 가드 단위테스트"
```

---

### Task 2: Pimc.Rollout (단일-세계 롤아웃 보상)

**Files:**
- Create: `Assets/_Project/GameFlow/Agents/Pimc.cs`
- Test: `Assets/_Project/Tests/EditMode/PimcAgentTests.cs` (이 태스크에서 파일 생성, Task 3에서 테스트 추가)

**Interfaces:**
- Consumes: `Tichu.GameFlow.GameDriver(IAgent[]).RunRound(GameState)` → `RoundOutcome.Result` (`RoundResult.TeamATotal/TeamBTotal`); `Tichu.GameFlow.Agents.AiAgent(ulong,int)`; `Tichu.Core.Game.Seating.TeamOf(int)`.
- Produces: `public static int Pimc.Rollout(GameState world, int observerSeat, ulong policySeed)` — `world`(완전정보; 호출자가 클론 전달, 내부에서 변형됨)를 디폴트 휴리스틱으로 끝까지 플레이해 **관측 팀 부호 점수차** 반환.

- [ ] **Step 1: 실패 테스트 작성** — `PimcAgentTests.cs` 생성, 롤아웃 테스트만 먼저.

`Assets/_Project/Tests/EditMode/PimcAgentTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core;
using Tichu.Core.Cards;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    /// <summary>Pimc.Rollout + PimcAgent 탐색 배선 검증.</summary>
    public class PimcAgentTests
    {
        private static GameState FreshPlayState(int turn)
        {
            var deck = Deck.CreateStandard();
            var hands = new IReadOnlyList<Card>[4];
            for (int i = 0; i < 4; i++)
                hands[i] = deck.GetRange(i * 14, 14);
            return GameFlowHelpers.PlayState(turn, hands);
        }

        // ── Rollout 정확성 ───────────────────────────────────────────────────────────

        [Test]
        public void Rollout_completes_without_throwing_and_returns_signed_reward()
        {
            // 결정화한 완전정보 세계를 디폴트 정책으로 끝까지 플레이 → throw 없이 정수 보상.
            var src = FreshPlayState(0);
            var rng = new Rng(11UL);
            var world = Determinizer.Sample(src, 0, ref rng);

            int reward = Pimc.Rollout(world.Clone(), observerSeat: 0, policySeed: 777UL);

            // 한 판 카드점수 합은 100 + 티츄델타라 절대값이 비현실적으로 크지 않다(완주 증거).
            Assert.That(reward, Is.InRange(-600, 600));
        }

        [Test]
        public void Rollout_is_deterministic_for_same_world_and_seed()
        {
            var src = FreshPlayState(0);
            var rng = new Rng(11UL);
            var world = Determinizer.Sample(src, 0, ref rng);

            int a = Pimc.Rollout(world.Clone(), 0, 777UL);
            int b = Pimc.Rollout(world.Clone(), 0, 777UL);
            Assert.That(a, Is.EqualTo(b), "같은 세계+시드 → 같은 보상");
        }

        [Test]
        public void Rollout_reward_sign_is_relative_to_observer_team()
        {
            // 같은 세계를 팀0 관점(seat0)과 팀1 관점(seat1)으로 롤아웃하면 부호가 반대.
            var src = FreshPlayState(0);
            var rng = new Rng(11UL);
            var world = Determinizer.Sample(src, 0, ref rng);

            int teamA = Pimc.Rollout(world.Clone(), observerSeat: 0, policySeed: 777UL);
            int teamB = Pimc.Rollout(world.Clone(), observerSeat: 1, policySeed: 777UL);
            Assert.That(teamA, Is.EqualTo(-teamB), "관측 팀 부호로 보상이 뒤집혀야 한다");
        }
    }
}
```

- [ ] **Step 2: 실패 확인** — `Pimc` 타입 부재로 컴파일 실패. `read_console`로 `Pimc` 미정의 에러 확인.

- [ ] **Step 3: 최소 구현 작성**

`Assets/_Project/GameFlow/Agents/Pimc.cs`:

```csharp
using Tichu.Core.Game;

namespace Tichu.GameFlow.Agents
{
    /// <summary>PIMC 탐색 코어. P2-A: 단일-세계 롤아웃 보상만 제공한다.</summary>
    public static class Pimc
    {
        /// <summary>
        /// world(완전정보 세계, 호출자가 클론을 넘긴다)를 현 휴리스틱(AiAgent)으로 끝까지 플레이하고
        /// 관측 좌석 팀 기준 점수차(TeamATotal−TeamBTotal, 팀1이면 부호 반전)를 반환한다.
        /// AiAgent 는 결정적이므로 (world, policySeed) 고정 시 보상도 결정적이다.
        /// </summary>
        public static int Rollout(GameState world, int observerSeat, ulong policySeed)
        {
            var agents = new IAgent[4];
            for (int seat = 0; seat < 4; seat++)
                agents[seat] = new AiAgent(policySeed, seat);

            var outcome = new GameDriver(agents).RunRound(world);
            int diff = outcome.Result.TeamATotal - outcome.Result.TeamBTotal;
            return Seating.TeamOf(observerSeat) == 0 ? diff : -diff;
        }
    }
}
```

- [ ] **Step 4: 임포트 + 컴파일 + 통과 확인**

(UnityMCP) PlayMode 정지 상태에서:
1. `execute_code`로 `Pimc.cs` + `PimcAgentTests.cs` `ImportAsset(..., ForceUpdate)` + `Refresh()`.
2. `read_console` → 에러 0.
3. `run_tests(mode="EditMode", test_names=["PimcAgentTests"])`.

Expected: 3개 테스트 PASS.

- [ ] **Step 5: 커밋**

```bash
git add "Assets/_Project/GameFlow/Agents/Pimc.cs" "Assets/_Project/GameFlow/Agents/Pimc.cs.meta" "Assets/_Project/Tests/EditMode/PimcAgentTests.cs" "Assets/_Project/Tests/EditMode/PimcAgentTests.cs.meta"
git commit -m "feat(p2a): Pimc.Rollout — 휴리스틱 디폴트 정책 단일-세계 롤아웃 보상"
```

---

### Task 3: PimcAgent (단일-세계 PIMC 탐색 에이전트)

**Files:**
- Create: `Assets/_Project/GameFlow/Agents/PimcAgent.cs`
- Test: `Assets/_Project/Tests/EditMode/PimcAgentTests.cs:` (Task 2 파일에 테스트 추가)

**Interfaces:**
- Consumes: `Determinizer.Sample(GameState, int, ref Rng)`; `Pimc.Rollout(GameState, int, ulong)`; `Tichu.GameFlow.Agents.AiAgent`; `Tichu.GameFlow.MoveOrder.Strength(Combination)`; `Tichu.Core.Game.GameEngine.Apply`, `GameAction.Play/Pass`; `Tichu.Core.Rng`; `DecisionContext`(`State/Seat/LegalMoves/CanPass`).
- Produces: `public sealed class PimcAgent : IAgent`, 생성자 `(ulong roundSeed, int seat)`. `DecideTurn`만 단일-세계 탐색, 나머지 5개 메서드는 내부 `AiAgent` 위임.

- [ ] **Step 1: 실패 테스트 작성** — `PimcAgentTests.cs`에 메서드 추가(기존 클래스 안, Rollout 테스트 아래에 이어 붙임).

```csharp
        // ── PimcAgent 탐색 배선 ──────────────────────────────────────────────────────

        [Test]
        public void DecideTurn_returns_a_legal_non_null_move_on_lead()
        {
            var s = FreshPlayState(0);                 // seat0 리드(트릭 없음)
            var ctx = GameFlowHelpers.Context(s, 0);
            var d = new PimcAgent(2024UL, 0).DecideTurn(ctx);

            Assert.That(d.IsPass, Is.False, "리드는 패스 불가 → 수를 내야 한다");
            Assert.That(d.Move, Is.Not.Null);
            // 반환 수는 합법수에 속해야 한다.
            bool legal = false;
            foreach (var m in ctx.LegalMoves)
                if (m.Rank == d.Move!.Rank && m.Type == d.Move!.Type && m.Length == d.Move!.Length) legal = true;
            Assert.That(legal, Is.True, "반환 수는 LegalMoves 안에 있어야 한다");
        }

        [Test]
        public void DecideTurn_is_deterministic_for_same_state_and_seed()
        {
            var s1 = FreshPlayState(0);
            var s2 = FreshPlayState(0);
            var d1 = new PimcAgent(55UL, 0).DecideTurn(GameFlowHelpers.Context(s1, 0));
            var d2 = new PimcAgent(55UL, 0).DecideTurn(GameFlowHelpers.Context(s2, 0));

            Assert.That(d1.IsPass, Is.EqualTo(d2.IsPass));
            if (!d1.IsPass)
            {
                Assert.That(d1.Move!.Rank, Is.EqualTo(d2.Move!.Rank));
                Assert.That(d1.Move!.Type, Is.EqualTo(d2.Move!.Type));
                Assert.That(d1.Move!.Length, Is.EqualTo(d2.Move!.Length));
            }
        }

        [Test]
        public void NonTurn_decisions_delegate_to_heuristic()
        {
            // 강한 8장 → CallGrandTichu 는 휴리스틱과 동일하게 true.
            var strong = new List<Card>
            {
                Card.Dragon, Card.Phoenix,
                Card.Normal(14, Suit.Jade), Card.Normal(14, Suit.Sword),
                Card.Normal(13, Suit.Pagoda), Card.Normal(13, Suit.Star),
                Card.Normal(3, Suit.Jade), Card.Normal(4, Suit.Sword)
            };
            var s = GameFlowHelpers.PlayState(0, strong,
                new List<Card> { Card.Normal(2, Suit.Jade) },
                new List<Card> { Card.Normal(2, Suit.Sword) },
                new List<Card> { Card.Normal(2, Suit.Pagoda) });
            var ctx = GameFlowHelpers.Context(s, 0);

            var pimc = new PimcAgent(1UL, 0).CallGrandTichu(ctx);
            var heur = new AiAgent(1UL, 0).CallGrandTichu(ctx);
            Assert.That(pimc, Is.EqualTo(heur), "비-턴 결정은 휴리스틱에 위임돼야 한다");
        }

        [Test]
        public void Four_PimcAgents_complete_a_round_without_throwing()
        {
            // 단일-세계 탐색이 재귀/폭주 없이 한 라운드를 완주하는지(배선 무결성).
            var s = FreshPlayState(0);
            var agents = new IAgent[]
            {
                new PimcAgent(900UL, 0), new PimcAgent(900UL, 1),
                new PimcAgent(900UL, 2), new PimcAgent(900UL, 3)
            };
            var outcome = new GameDriver(agents).RunRound(s);
            Assert.That(outcome.State.Phase, Is.EqualTo(RoundPhase.RoundEnd));
            Assert.That(outcome.Result, Is.Not.Null);
        }
```

- [ ] **Step 2: 실패 확인** — `PimcAgent` 타입 부재로 컴파일 실패. `read_console` 확인.

- [ ] **Step 3: 최소 구현 작성**

`Assets/_Project/GameFlow/Agents/PimcAgent.cs`:

```csharp
using Tichu.Core;
using Tichu.Core.Combinations;
using Tichu.Core.Game;

namespace Tichu.GameFlow.Agents
{
    /// <summary>
    /// 단일-세계 PIMC 탐색 에이전트(P2-A 골격).
    /// DecideTurn 만 탐색: 미관측 손패를 1개 세계로 결정화하고, 각 루트 합법수를 적용한 뒤
    /// 현 휴리스틱(AiAgent)으로 끝까지 롤아웃해 관측 팀 점수차가 최대인 수를 고른다.
    /// 동점깨기는 MoveOrder.Strength 최소(사람다운 보존 편향). 나머지 결정은 휴리스틱 위임.
    /// 무작위는 결정화 셔플에만 쓰이며, 고정 노드수(worlds=1)에서 결정적이다.
    /// </summary>
    public sealed class PimcAgent : IAgent
    {
        private readonly ulong _roundSeed;
        private readonly int _seat;
        private readonly AiAgent _heuristic;   // 비-턴 결정 위임 + (개념상) 디폴트 정책과 동일 로직.
        private Rng _rng;

        public PimcAgent(ulong roundSeed, int seat)
        {
            _roundSeed = roundSeed;
            _seat = seat;
            _heuristic = new AiAgent(roundSeed, seat);
            _rng = new Rng(roundSeed ^ 0x91C0_0000_0000_0001UL ^ (ulong)seat);
        }

        // ── 탐색하는 결정: 자기 턴 ─────────────────────────────────────────────────
        public TurnDecision DecideTurn(in DecisionContext ctx)
        {
            var legal = ctx.LegalMoves;
            if (legal.Count == 0)
                return TurnDecision.Pass;   // 방어(드라이버상 도달 안 함).

            // 미관측 손패를 1개 세계로 결정화(Rng 로컬 전진 후 되쓰기).
            var rng = _rng;
            var world = Determinizer.Sample(ctx.State, ctx.Seat, ref rng);
            _rng = rng;

            // 롤아웃 정책 시드: 라운드/좌석 파생(결정적, 게임 셔플과 비상관).
            ulong policySeed = _roundSeed ^ 0xR0LL_seed_placeholder; // ← 아래 주: 실제 값으로 교체

            Combination? best = null;
            int bestStrength = int.MaxValue;
            long bestEv = long.MinValue;

            // 각 합법수: 적용 후 롤아웃 보상.
            for (int i = 0; i < legal.Count; i++)
            {
                var move = legal[i];
                var sim = world.Clone();
                var applied = GameEngine.Apply(sim, GameAction.Play(ctx.Seat, move.Cards)); // P2-A: wish=null
                if (!applied.Ok) continue;

                int ev = Pimc.Rollout(sim, ctx.Seat, policySeed);
                int strength = MoveOrder.Strength(move);
                if (ev > bestEv || (ev == bestEv && strength < bestStrength))
                {
                    bestEv = ev;
                    bestStrength = strength;
                    best = move;
                }
            }

            // 패스가 합법이면 후보에 포함(EV 비교).
            if (ctx.CanPass)
            {
                var sim = world.Clone();
                var applied = GameEngine.Apply(sim, GameAction.Pass(ctx.Seat));
                if (applied.Ok)
                {
                    int ev = Pimc.Rollout(sim, ctx.Seat, policySeed);
                    if (ev > bestEv)             // 동점이면 수를 선호(패스는 마지막).
                        return TurnDecision.Pass;
                }
            }

            return best == null ? TurnDecision.Pass : TurnDecision.Play(best);
        }

        // ── 위임하는 결정(P2-A): 휴리스틱 그대로 ────────────────────────────────────
        public bool CallGrandTichu(in DecisionContext ctx) => _heuristic.CallGrandTichu(ctx);
        public ExchangeChoice ChooseExchange(in DecisionContext ctx) => _heuristic.ChooseExchange(ctx);
        public bool CallTichu(in DecisionContext ctx) => _heuristic.CallTichu(ctx);
        public Combination? DecideBomb(in DecisionContext ctx) => _heuristic.DecideBomb(ctx);
        public int ChooseDragonRecipient(in DecisionContext ctx) => _heuristic.ChooseDragonRecipient(ctx);
    }
}
```

> **주(구현 시 반드시 교체):** 위 `0xR0LL_seed_placeholder`는 컴파일되지 않는 의사 표기다. 실제로는 유효한 `ulong` 리터럴 상수로 교체한다, 예:
> ```csharp
> ulong policySeed = _roundSeed ^ 0x5043_0000_0000_0001UL ^ (ulong)ctx.Seat;
> ```
> (게임 셔플 Rng·`AiAgent`/`RandomAgent`의 `0xA1A1…` 상수와 다른 값이면 된다. 결정성만 보장하면 충분.)

- [ ] **Step 4: 임포트 + 컴파일 + 통과 확인**

(UnityMCP) PlayMode 정지 상태에서:
1. `execute_code`로 `PimcAgent.cs` + 갱신된 `PimcAgentTests.cs` `ImportAsset(..., ForceUpdate)` + `Refresh()`.
2. `read_console` → 에러 0 (특히 `policySeed` 자리표시자 교체 확인).
3. `run_tests(mode="EditMode", test_names=["PimcAgentTests"])`.

Expected: 7개 테스트 PASS(Task 2의 3개 + Task 3의 4개).

- [ ] **Step 5: 회귀 + 커밋**

회귀(클래스 필터로만, 전체 스위트 금지):
- `run_tests(mode="EditMode", test_names=["AiAgentTests"])` → 기존 그린 유지 확인(휴리스틱·드라이버 무영향).
- `run_tests(mode="EditMode", test_names=["MoveOrderTests"])` → 그린 유지.

```bash
git add "Assets/_Project/GameFlow/Agents/PimcAgent.cs" "Assets/_Project/GameFlow/Agents/PimcAgent.cs.meta" "Assets/_Project/Tests/EditMode/PimcAgentTests.cs"
git commit -m "feat(p2a): PimcAgent — 단일-세계 PIMC 탐색 배선(DecideTurn) + 휴리스틱 위임"
```

---

## Self-Review

**1. Spec coverage (스펙 §11 P2-A):**
- Determinizer(제약충족 분배 + 치트가드) → Task 1 ✓
- 현 휴리스틱을 디폴트 정책으로 → Task 2 `Pimc.Rollout`이 `AiAgent`를 디폴트 정책으로 사용 ✓ (`IPolicy` 추출은 의도적 연기 — Deviation 절에 문서화)
- 단일 세계 롤아웃 → ScoreRound 보상 → Task 2 ✓
- `PimcAgent`(고정노드수, worlds=1) → Task 3 ✓
- 신규 asmdef 0 → 3파일 모두 기존 `Tichu.GameFlow` ✓
- 검증(TDD): 결정성·치트가드·롤아웃 정확성 → §12.1(치트가드/결정성)=Task 1, §12.2(롤아웃 정확성)=Task 2, §12.3(PIMC 결정성)=Task 3 ✓

**2. Placeholder scan:** 코드 자리표시자는 `policySeed`의 `0xR0LL_seed_placeholder` 하나뿐 — Step 3 직후 "주"에서 유효 리터럴로 교체를 명시(의도적, 실행자가 반드시 처리). 그 외 TBD/TODO 없음.

**3. Type consistency:** `Determinizer.Sample(GameState, int, ref Rng)` — Task 1 정의, Task 3 소비 일치. `Pimc.Rollout(GameState, int, ulong)` — Task 2 정의, Task 3 소비 일치. `PimcAgent(ulong, int)` — `AiAgent`/`RandomAgent`와 동일. `RoundResult.TeamATotal/TeamBTotal`, `MoveOrder.Strength`, `GameAction.Play(seat, cards)`/`Pass(seat)`, `Seating.TeamOf` — 모두 실제 소스 시그니처와 대조 완료.

**미해결/주의:** `FreshPlayState`(4×14 손패)는 셋업을 우회한 닫힌 Play 상태다. `GameDriver.RunRound`가 이 상태에서 완주하는지는 Task 3 풀라운드 테스트가 검증한다(만약 `ScoreRound`가 셋업-우회 상태에서 문제를 일으키면 systematic-debugging으로 처리 — 단, Play→Scoring 경로는 진입 방식과 무관하게 동일하므로 통과가 기대값).

---

## Execution Handoff

**Plan complete and saved to `docs/superpowers/plans/2026-06-24-tichu-p2a-search-skeleton.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — 태스크마다 새 subagent 디스패치, 태스크 사이 리뷰, 빠른 반복.

**2. Inline Execution** — 이 세션에서 executing-plans로 체크포인트 배치 실행.

**Which approach?**
