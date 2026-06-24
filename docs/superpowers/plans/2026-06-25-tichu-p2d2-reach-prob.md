# P2-D2 — Reach-Probability 가중 세계 (Hard 강화) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Hard 티어가 균등 세계샘플 대신 **reach-probability 가중**(관측된 티츄/큰티츄 콜과 일관된 세계에 더 큰 가중)을 쓰게 해 Normal보다 강하게 만든다. 중요도 샘플링(uniform 샘플 + reach 가중 EV 평균)으로 구현 — Determinizer는 그대로, 가중은 PimcAgent EV 집계에서.

**Architecture:** 각 균등 결정화 세계에 `ReachWeight.WorldWeight(world, seat)` 가중치를 매긴다(콜한 상대의 "콜 시점 손패"(현재 배정 손 + 히스토리상 그 좌석이 낸 카드)가 강할수록 높음). PimcAgent는 EV를 가중 평균(Σ wᵥ·evᵥ / Σ wᵥ)으로 argmax. `PolicyConfig.UseReachProb`로 게이트(Hard/Expert=true, Easy/Normal=false). **off면 가중치 1.0 → Normal 동작 불변(결정성 보존).**

**Tech Stack:** C# (`Tichu.GameFlow` 코어) · NUnit EditMode · 백그라운드 스레드 벤치.

## Global Constraints

설계 스펙 §4.3(비균등 샘플·Rebstock 2019), §5(티어). P2-A~C는 `main`(머지 `9a2d3e4`)에. 모든 태스크 암묵 포함.

- **신규 asmdef 0**, 새 코드 `Assets/_Project/GameFlow/Agents/` (`Tichu.GameFlow.Agents`). 테스트 `Tests/EditMode/` (`Tichu.Core.Tests`).
- **결정성·AiAgent 불변·오라클 불침범** 유지. `UseReachProb=false`에서 PimcAgent는 P2-C와 동일 결과(자기일관 결정성 테스트 통과).
- **PIMC≈8초/라운드** — 강도 벤치는 **백그라운드 `Task.Run` + 파일 폴링**(메인스레드/MCP 비차단), `PimcBench.RunMirrored` 재사용. `[Explicit]` 벤치는 기본 스위트 제외 유지.
- `run_tests`는 정규화 전체이름·class 필터·`summary.total>0` 확인·PlayMode 정지. 신규/수정 .cs는 `execute_code` `ImportAsset(ForceUpdate)`+`Refresh`→`isCompiling`→`read_console` 0→테스트.

---

## File Structure

| 파일 | 책임 |
|---|---|
| `GameFlow/Agents/PolicyConfig.cs` (수정) | `bool UseReachProb`(기본 false) 필드 + ctor 선택 파라미터 + Hard/Expert 프리셋 true. |
| `GameFlow/Agents/ReachWeight.cs` (생성) | `WorldWeight(world, observerSeat)` + `HandStrength` + 콜시점 손 재구성. 순수. |
| `GameFlow/Agents/PimcAgent.cs` (수정) | EV 집계를 가중(double)로: 세계별 weight(UseReachProb? ReachWeight : 1.0). |
| `Tests/EditMode/PolicyConfigTests.cs` (수정) | UseReachProb 프리셋 검증. |
| `Tests/EditMode/ReachWeightTests.cs` (생성) | HandStrength·재구성·WorldWeight 단위(강한 콜손 세계가 가중↑). |
| `Tests/EditMode/PimcAgentTests.cs` (수정) | UseReachProb off=불변(결정성) + on=결정적·합법. |

---

## Deviations / 근사 (문서화)

- **중요도 샘플링**: 균등 샘플 유지(Determinizer 무수정) + reach 가중 EV. 별도 베이지안 샘플러 미구현(더 단순·검증 쉬움).
- **콜시점 손 재구성**: 작은 티츄(14장, 교환 후·플레이 전 선언)는 `현재 배정 손 ∪ 히스토리상 그 좌석이 낸 카드`로 **정확 재구성**. 큰 티츄(8장, 교환 전)는 교환 정보가 상태에 안 남아 정확 재구성 불가 → **같은 14장 재구성을 근사**로 사용(큰티츄 콜자도 14장 강함 상관). 문서화된 근사.
- **가중 함수**: `weight = Π(콜한 상대) (1 + K·atCallStrength)`. K는 시작값(0.15), P2-C 벤치로 튜닝. 무콜 좌석=중립(×1).
- **폭탄/소원 단서**(§4.3의 음의 단서 등)는 차기. v1은 콜 단서만.

---

### Task 1: PolicyConfig.UseReachProb

**Files:** Modify `GameFlow/Agents/PolicyConfig.cs`, `Tests/EditMode/PolicyConfigTests.cs`

**Interfaces:** Produces `PolicyConfig.UseReachProb` (bool) + ctor `(int,int,double,bool useReachProb=false)`. `For(Hard|Expert).UseReachProb==true`, `For(Easy|Normal).UseReachProb==false`.

- [ ] **Step 1: 테스트 추가(red)** — `PolicyConfigTests.cs`에 추가:

```csharp
        [Test]
        public void UseReachProb_on_for_hard_expert_off_for_easy_normal()
        {
            Assert.That(PolicyConfig.For(Difficulty.Easy).UseReachProb, Is.False);
            Assert.That(PolicyConfig.For(Difficulty.Normal).UseReachProb, Is.False);
            Assert.That(PolicyConfig.For(Difficulty.Hard).UseReachProb, Is.True);
            Assert.That(PolicyConfig.For(Difficulty.Expert).UseReachProb, Is.True);
        }
```

- [ ] **Step 2: 실패 확인** — `UseReachProb` 미정의 컴파일 에러.

- [ ] **Step 3: 구현** — `PolicyConfig.cs` 수정:

`Epsilon` 필드 아래 추가:
```csharp
        /// <summary>true면 reach-probability 가중 세계(Hard+). false면 균등.</summary>
        public readonly bool UseReachProb;
```
ctor 교체:
```csharp
        public PolicyConfig(int worlds, int rolloutsPerWorld, double epsilon, bool useReachProb = false)
        {
            Worlds = worlds;
            RolloutsPerWorld = rolloutsPerWorld;
            Epsilon = epsilon;
            UseReachProb = useReachProb;
        }
```
`For` 의 Hard/Expert 줄 교체:
```csharp
                case Difficulty.Hard:   return new PolicyConfig(16, 4, 0.05, useReachProb: true);  // 고급기능 일부 P2-D2~
                case Difficulty.Expert: return new PolicyConfig(24, 6, 0.00, useReachProb: true);
```
(Easy/Normal 줄은 그대로 — useReachProb 기본 false.)

- [ ] **Step 4: 통과** — Import + 컴파일 0 + `run_tests(["Tichu.Core.Tests.PolicyConfigTests"])` → 4 PASS(기존 3 + 신규 1). 기존 `new PolicyConfig(7,3,0.2)` 등은 선택 파라미터라 그대로 컴파일.

- [ ] **Step 5: 커밋**
```bash
git add "Assets/_Project/GameFlow/Agents/PolicyConfig.cs" "Assets/_Project/Tests/EditMode/PolicyConfigTests.cs"
git commit -m "feat(p2d2): PolicyConfig.UseReachProb (Hard/Expert=true)"
```

---

### Task 2: ReachWeight (콜 단서 가중)

**Files:** Create `GameFlow/Agents/ReachWeight.cs`, `Tests/EditMode/ReachWeightTests.cs`

**Interfaces:** Produces:
- `static int ReachWeight.HandStrength(IReadOnlyList<Card>)` — 고카드 점수(Dragon4·Phoenix3·A2·K1, Dog/Mahjong 0). (AiAgent.HandPower 미러, 공개.)
- `static double ReachWeight.WorldWeight(GameState world, int observerSeat)` — 콜한 상대의 콜시점 손(현재 배정 + 히스토리 플레이) 강함으로 가중. 무콜·관측자 제외.

- [ ] **Step 1: 테스트 작성(red)**

`Tests/EditMode/ReachWeightTests.cs`:
```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Game;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    public class ReachWeightTests
    {
        private static List<Card> H(params Card[] c) => new List<Card>(c);
        private static Card N(int r, Suit s) => Card.Normal(r, s);

        [Test]
        public void HandStrength_counts_high_cards()
        {
            int strong = ReachWeight.HandStrength(H(Card.Dragon, Card.Phoenix, N(14, Suit.Jade), N(13, Suit.Sword)));
            int weak = ReachWeight.HandStrength(H(N(2, Suit.Jade), N(3, Suit.Sword), N(4, Suit.Pagoda), N(5, Suit.Star)));
            Assert.That(strong, Is.GreaterThan(weak));
            Assert.That(weak, Is.EqualTo(0));
            Assert.That(strong, Is.EqualTo(4 + 3 + 2 + 1));
        }

        [Test]
        public void WorldWeight_is_one_when_no_calls()
        {
            var s = GameFlowHelpers.PlayState(0,
                H(N(2, Suit.Jade)), H(N(3, Suit.Jade)), H(N(4, Suit.Jade)), H(N(5, Suit.Jade)));
            // 기본 Call=None → 가중 1.0.
            Assert.That(ReachWeight.WorldWeight(s, 0), Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void WorldWeight_higher_when_caller_has_strong_hand()
        {
            // seat1 이 작은 티츄 콜. 강한 손 배정 세계 vs 약한 손 배정 세계 비교.
            var strongWorld = GameFlowHelpers.PlayState(0,
                H(N(2, Suit.Jade)), H(Card.Dragon, N(14, Suit.Sword)), H(N(4, Suit.Jade)), H(N(5, Suit.Jade)));
            var weakWorld = GameFlowHelpers.PlayState(0,
                H(N(2, Suit.Jade)), H(N(3, Suit.Sword), N(4, Suit.Sword)), H(N(4, Suit.Jade)), H(N(5, Suit.Jade)));
            strongWorld.Seats[1].Call = TichuCall.Tichu;
            weakWorld.Seats[1].Call = TichuCall.Tichu;

            double wStrong = ReachWeight.WorldWeight(strongWorld, 0);
            double wWeak = ReachWeight.WorldWeight(weakWorld, 0);
            Assert.That(wStrong, Is.GreaterThan(wWeak), "강한 손 배정이 콜과 더 일관 → 가중↑");
            Assert.That(wWeak, Is.GreaterThan(1.0), "콜 있으면 1.0 초과");
        }

        [Test]
        public void WorldWeight_observer_call_ignored()
        {
            var s = GameFlowHelpers.PlayState(0,
                H(Card.Dragon), H(N(3, Suit.Jade)), H(N(4, Suit.Jade)), H(N(5, Suit.Jade)));
            s.Seats[0].Call = TichuCall.GrandTichu; // 관측자 자신 → 무시
            Assert.That(ReachWeight.WorldWeight(s, 0), Is.EqualTo(1.0).Within(1e-9));
        }
    }
}
```

- [ ] **Step 2: 실패 확인** — `ReachWeight` 미정의.

- [ ] **Step 3: 구현**

`GameFlow/Agents/ReachWeight.cs`:
```csharp
using System.Collections.Generic;
using Tichu.Core.Cards;
using Tichu.Core.Game;

namespace Tichu.GameFlow.Agents
{
    /// <summary>
    /// reach-probability 가중(콜 단서). 콜한 상대의 "콜 시점 손"(현재 배정 손 + 히스토리상 그 좌석이 낸 카드)이
    /// 강할수록 그 세계가 관측 콜과 일관 → 가중↑. 균등 샘플 + 이 가중 = 중요도 샘플링.
    /// 작은 티츄(14장)는 정확 재구성; 큰 티츄(8장·교환전)는 14장 재구성을 근사로 사용.
    /// </summary>
    public static class ReachWeight
    {
        private const double K = 0.15; // 가중 강도 시작값(P2-C 벤치 튜닝).

        /// <summary>손패 고카드 강함(Dragon4·Phoenix3·A2·K1). AiAgent.HandPower 미러.</summary>
        public static int HandStrength(IReadOnlyList<Card> hand)
        {
            int score = 0;
            for (int i = 0; i < hand.Count; i++)
            {
                var c = hand[i];
                switch (c.Special)
                {
                    case SpecialKind.Dragon: score += 4; break;
                    case SpecialKind.Phoenix: score += 3; break;
                    case SpecialKind.None:
                        if (c.Rank == 14) score += 2;
                        else if (c.Rank == 13) score += 1;
                        break;
                }
            }
            return score;
        }

        /// <summary>관측자 외 콜한 좌석의 콜시점 손 강함으로 세계 가중. 콜 없으면 1.0.</summary>
        public static double WorldWeight(GameState world, int observerSeat)
        {
            double weight = 1.0;
            for (int seat = 0; seat < 4; seat++)
            {
                if (seat == observerSeat) continue;
                if (world.Seats[seat].Call == TichuCall.None) continue;
                int atCall = HandStrength(world.Seats[seat].Hand) + PlayedStrength(world, seat);
                weight *= 1.0 + K * atCall;
            }
            return weight;
        }

        // 히스토리상 seat 이 낸 카드들의 강함 합(현재/완료 트릭).
        private static int PlayedStrength(GameState world, int seat)
        {
            int sum = 0;
            if (world.CurrentTrick != null) sum += PlaysOf(world.CurrentTrick.History, seat);
            foreach (var t in world.CompletedTricks) sum += PlaysOf(t.History, seat);
            return sum;
        }

        private static int PlaysOf(List<Play> history, int seat)
        {
            int sum = 0;
            foreach (var p in history)
                if (p.Seat == seat && p.Combination != null) sum += HandStrength(p.Combination.Cards);
            return sum;
        }
    }
}
```

- [ ] **Step 4: 통과** — Import + 컴파일 0 + `run_tests(["Tichu.Core.Tests.ReachWeightTests"])` → 4 PASS.

- [ ] **Step 5: 커밋**
```bash
git add "Assets/_Project/GameFlow/Agents/ReachWeight.cs" "Assets/_Project/GameFlow/Agents/ReachWeight.cs.meta" "Assets/_Project/Tests/EditMode/ReachWeightTests.cs" "Assets/_Project/Tests/EditMode/ReachWeightTests.cs.meta"
git commit -m "feat(p2d2): ReachWeight — 콜 단서 세계 가중(콜시점 손 강함)"
```

---

### Task 3: PimcAgent 가중 EV 집계

**Files:** Modify `GameFlow/Agents/PimcAgent.cs`, `Tests/EditMode/PimcAgentTests.cs`

**Interfaces:** Consumes `ReachWeight.WorldWeight`. PimcAgent.DecideTurnAnytime: 세계별 `weight = _config.UseReachProb ? ReachWeight.WorldWeight(world,_seat) : 1.0`; EV를 `double weightedSum[i] += weight*ev`, `totalWeight += weight`로 집계; argmax(weightedSum). 패스: `passEvRaw * totalWeight > bestWeightedSum`. **UseReachProb=false면 weight=1.0 → 기존과 동일 argmax.**

- [ ] **Step 1: 테스트 추가(red)** — `PimcAgentTests.cs`에:
```csharp
        [Test]
        public void Hard_reachprob_decide_turn_is_deterministic_and_legal()
        {
            var cfg = PolicyConfig.For(Difficulty.Hard); // UseReachProb=true (worlds=16 → 무겁지만 1결정만)
            var s1 = FreshPlayState(0); var s2 = FreshPlayState(0);
            // 콜 단서 부여(seat1 티츄) — 가중 경로 활성.
            s1.Seats[1].Call = TichuCall.Tichu; s2.Seats[1].Call = TichuCall.Tichu;
            var ctx1 = GameFlowHelpers.Context(s1, 0);
            var d1 = new PimcAgent(55UL, 0, cfg).DecideTurn(ctx1);
            var d2 = new PimcAgent(55UL, 0, cfg).DecideTurn(GameFlowHelpers.Context(s2, 0));
            Assert.That(d1.IsPass, Is.EqualTo(d2.IsPass));
            if (!d1.IsPass)
            {
                Assert.That(d1.Move!.Rank, Is.EqualTo(d2.Move!.Rank));
                bool legal = false;
                foreach (var m in ctx1.LegalMoves)
                    if (m.Rank == d1.Move!.Rank && m.Type == d1.Move!.Type && m.Length == d1.Move!.Length) legal = true;
                Assert.That(legal, Is.True);
            }
        }
```
(기존 `DecideTurn_is_deterministic_for_same_state_and_seed`(Normal, UseReachProb=false)가 불변 회귀를 커버.)

- [ ] **Step 2: 실패 확인** — 가중 경로 미구현이어도 컴파일은 되나(현 코드 무가중), 이 테스트는 통과할 수도. 핵심은 **Normal 불변 + Hard 결정성** 둘 다. red 보장 위해, 먼저 Step3 구현 후 전체 통과로 검증(가중 도입이 Normal을 깨지 않음 확인이 본질).

- [ ] **Step 3: 구현** — `PimcAgent.cs` DecideTurnAnytime 의 집계부 교체:

`var sumEv = new long[legal.Count];` → `var weightedSum = new double[legal.Count]; double totalWeight = 0.0;`

세계 루프 안, 롤아웃 직전에 weight 계산(세계당 1회):
```csharp
                var world = Determinizer.Sample(ctx.State, _seat, ref rng);
                double weight = _config.UseReachProb ? ReachWeight.WorldWeight(world, _seat) : 1.0;
```
롤아웃 누적부 교체:
```csharp
                        weightedSum[i] += weight * Pimc.Rollout(sim, _seat, rolloutSeed, _config.Epsilon);
```
그리고 `samples++` 자리에서 `totalWeight += weight;` (샘플=(w,r)마다 weight 더함 — 같은 세계의 r 반복은 같은 weight). ⚠️weight는 세계 루프(w)에서 계산했으니 r 루프 안에서 `totalWeight += weight` 누적.

argmax 루프: `long bestSum` → `double bestSum = double.NegativeInfinity;`, `sumEv[i]` → `weightedSum[i]`.

패스부: `long passEv = (long)Pimc.Rollout(...) * _config.Worlds * rolloutsPerWorld;` → 
```csharp
                    double passEv = (double)Pimc.Rollout(passSim, _seat, policyBase, _config.Epsilon) * totalWeight;
                    if (passEv > bestSum) { _rng = rng; return TurnDecision.Pass; }
```
(uniform이면 totalWeight=W*R → 기존과 동일.)

⚠️ budget best-so-far 경로(budgetHit)도 weightedSum/totalWeight 기반 그대로 동작(부분 totalWeight로 비교). `samples>=1` 게이트 유지.

- [ ] **Step 4: 통과** — Import + 컴파일 0 + `run_tests(["Tichu.Core.Tests.PimcAgentTests"])`. 기존 + 신규 전부 PASS. **특히 `DecideTurn_is_deterministic_for_same_state_and_seed`·`Four_PimcAgents_complete_a_round`·anytime 3종이 Normal(UseReachProb=false)에서 그대로 그린**(가중 도입이 Normal 불변). Hard 결정성 신규 PASS. (worlds=16 Hard 1결정은 수초; 4봇 풀라운드는 Normal이라 OK.) `init_timeout=120000`, stuck 감시.

- [ ] **Step 5: 회귀 + 커밋** — `run_tests(["...ReachWeightTests","...PolicyConfigTests","...PimcDecisionAgentTests"])` 그린.
```bash
git add "Assets/_Project/GameFlow/Agents/PimcAgent.cs" "Assets/_Project/Tests/EditMode/PimcAgentTests.cs"
git commit -m "feat(p2d2): PimcAgent 가중 EV(reach-prob) — off면 균등 불변"
```

---

### Task 4: Hard 강도 벤치(백그라운드) + 리포트

**Files:** (코드 변경 없음) 백그라운드 벤치 실행 + `티츄_P2D2_Hard벤치.html` 생성.

- [ ] **Step 1: 백그라운드 벤치** — `execute_code`로 `Task.Run`:
  - (a) Hard vs 휴리스틱: `PimcBench.RunMirrored(pairs, seed, PolicyConfig.For(Difficulty.Hard))` — Hard도 휴리스틱 이기는지(≥ Normal 기대).
  - (b) **Hard vs Normal 직접 비교**(강도 향상 확인): 한 라운드에서 한 팀=Hard PIMC, 다른 팀=Normal PIMC. `PimcBench`는 PIMC-vs-AiAgent 전용이라, **임시 인라인 루프**로 Hard(0,2) vs Normal(1,3) 미러드 실행(execute_code 내). ⚠️Hard worlds=16라 라운드당 ~32초(8s×4) → pairs 작게(예: 5쌍=10R≈5분). 파일 폴링.
  - 결과: Hard avgDiff/승률/Wilson 하한 + (b)의 Hard-vs-Normal 점수차.
- [ ] **Step 2: 폴링** — 백그라운드 bash로 결과 파일 대기.
- [ ] **Step 3: 리포트** — `티츄_P2D2_Hard벤치.html`(파일럿 표·해석) 작성·브라우저로 표시·커밋.

> ⚠️ Hard worlds=16은 8s×4=~32s/R라 벤치가 매우 느림 → pairs 소량(5~10)로 방향성만. 정밀은 차후(속도 개선 후).

- [ ] **Step 4: 커밋**
```bash
git add "티츄_P2D2_Hard벤치.html"
git commit -m "docs(p2d2): Hard(reach-prob) 강도 벤치 리포트"
```

---

## Self-Review

**Spec coverage(§4.3 reach-prob):** 콜 단서 가중(Task 2) + Hard 게이트(Task 1) + 가중 EV(Task 3) + 강도 측정(Task 4) ✓. reach-prob "비균등 샘플"은 중요도 샘플링으로 실현(균등샘플+가중) — 문서화. 음의 단서(소원패스 등)·큰티츄 정확재구성은 차기(문서화).

**Type consistency:** `PolicyConfig(int,int,double,bool=)`·`UseReachProb` — Task1 정의, Task3 소비. `ReachWeight.WorldWeight(GameState,int)`/`HandStrength(IReadOnlyList<Card>)` — Task2 정의, Task3 소비. `Play.Seat`/`Combination.Cards`/`Trick.History`/`CompletedTricks`/`Seats[i].Call` — 실제 소스 대조 완료.

**Placeholder scan:** 없음. Task4 Hard-vs-Normal 인라인 루프는 실행 시점 작성(throwaway 측정, 커밋 코드 아님).

**미해결/주의:** ①Normal 불변은 weight=1.0 double 경로의 정수합 정확성에 의존(작은 정수합은 double에서 정확 → argmax 동일). ②Hard worlds=16 벤치 매우 느림(방향성만). ③K=0.15·worlds=16 등은 미튜닝(P2-C 벤치 후 확정). ④reach-prob가 strength fusion(과콜)을 키울 수 있음 — 벤치로 감시.

---

## Execution Handoff
**Plan saved to `docs/superpowers/plans/2026-06-25-tichu-p2d2-reach-prob.md`.**
**1. Inline Execution(권장)** — executing-plans로 Task 1→4.
**2. Subagent-Driven** — 태스크별(단, UnityMCP 경합).
**Which?**
