# P2-B1 — 다세계 PIMC 코어 + 티어(동기) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** P2-A 단일-세계 골격을 **다세계 PIMC + 난이도 티어(동기)**로 키운다 — `PolicyConfig`(세계수·롤아웃수·ε)와 `Difficulty` 티어, ε-노이즈 롤아웃 정책(`HeuristicRolloutPolicy`), 다세계 결정화+EV 투표. 전부 EditMode에서 결정적으로 테스트되며, P2-C 벤치가 그대로 쓰는 **동기 경로**다.

**Architecture:** `PimcAgent`가 `PolicyConfig`를 받아 `config.Worlds`개 세계를 결정화하고 각 루트 합법수를 `config.RolloutsPerWorld`회 롤아웃해 세계·롤아웃 평균 EV로 argmax한다. 롤아웃 디폴트 정책은 `HeuristicRolloutPolicy`(ε확률로 무작위 합법수, 아니면 현 휴리스틱) — ε=0이면 현 `AiAgent`와 동일. Easy(worlds=0)는 탐색 없이 ε-휴리스틱 직접 결정. 비동기 래퍼·인게임 배선은 다음 증분(P2-B2).

**Tech Stack:** C# (`Tichu.GameFlow` asmdef, `noEngineReferences=true`) · NUnit EditMode · UnityMCP `run_tests`.

## Global Constraints

설계 스펙: `docs/superpowers/specs/2026-06-24-tichu-phase2-ai-pimc-design.md` §3,§5,§7. P2-A 완료분(`Determinizer`/`Pimc.Rollout`/`PimcAgent`)은 `main`(머지 `855bea3`)에 있다. 모든 태스크는 아래를 암묵 포함.

- **신규 asmdef 0개.** 새 파일은 전부 `Assets/_Project/GameFlow/Agents/`, 네임스페이스 `Tichu.GameFlow.Agents`(asmdef `Tichu.GameFlow`, `noEngineReferences=true`, `references=["Tichu.Core"]`). 테스트는 `Assets/_Project/Tests/EditMode/`(asmdef `Tichu.Core.Tests`, 네임스페이스 `Tichu.Core.Tests`).
- **UnityEngine 비의존.** `noEngineReferences=true` → `UnityEngine` 타입 금지. 순수 C#만.
- **결정성(고정 노드수 모드):** 모든 무작위는 주입된 `Tichu.Core.Rng`(struct SplitMix64). `System.Random`/`DateTime`/`Stopwatch` 금지. 같은 `(state, seed, config)` → 같은 선택수. `Rng`는 비-readonly 필드에서 직접 메서드 호출 가능(현 `AiAgent`/`RandomAgent` 패턴), 또는 `ref` 전달. ⚠️**readonly Rng 필드 금지**(방어적 복사로 전진 누락).
- **`AiAgent` 불변.** 현 `AiAgent`(GameFlow/Agents/AiAgent.cs)는 한 줄도 고치지 않는다. 새 정책은 `AiAgent`를 **합성(composition)**으로 재사용.
- **에이전트 생성자 시그니처:** P2-A `PimcAgent(ulong, int)` → P2-B1 `PimcAgent(ulong, int, PolicyConfig)`로 진화. 기존 P2-A 테스트를 새 시그니처로 갱신(이 계획에 명시).
- **ε=0 하위호환:** `HeuristicRolloutPolicy(ε=0)`는 `AiAgent`와 동일 동작(롤아웃 결과 비트동일). `Pimc.Rollout`의 ε=0 결과는 P2-A 기준선과 같아야 한다.
- **⚠️ 테스트 실행:** `run_tests(mode="EditMode", test_names=["Tichu.Core.Tests.<클래스>"])` — **정규화 전체이름 필수**(짧은 이름은 0개 실행+거짓 Passed). 결과 검증은 `summary.total>0` 확인. **전체 `Tichu.Core.Tests` 실행 금지**(`SimulationTests` 10만판 → MCP stuck). `run_tests` 전 PlayMode 정지.
- **⚠️ 신규/수정 .cs:** `execute_code(codedom)`로 `AssetDatabase.ImportAsset(path, ForceUpdate)` 전부 + `Refresh(ForceUpdate)` → `isCompiling` 폴링 → `read_console(types:["error"])` 0 확인 → 테스트. `execute_code`가 `{success:false,message:null}`면 컴파일/도메인리로드 중 → 재시도.
- **오라클 불침범:** 탐색 AI는 기존 동기/비동기 `ComputeHash` 오라클 경로에 안 들어간다(P2-B1은 그 경로 무수정). 기존 EditMode 그린 유지.

---

## File Structure

| 파일 | 책임 |
|---|---|
| `Assets/_Project/GameFlow/Agents/Difficulty.cs` (생성) | `enum Difficulty { Easy, Normal, Hard, Expert }`. |
| `Assets/_Project/GameFlow/Agents/PolicyConfig.cs` (생성) | 탐색/정책 파라미터 struct(Worlds/RolloutsPerWorld/Epsilon) + 티어 프리셋 `For(Difficulty)`. |
| `Assets/_Project/GameFlow/Agents/HeuristicRolloutPolicy.cs` (생성) | `IAgent`. ε확률 무작위 합법수, 아니면 `AiAgent` 휴리스틱. 비-턴 결정은 `AiAgent` 위임. |
| `Assets/_Project/GameFlow/Agents/Pimc.cs` (수정) | `Rollout`에 `epsilon` 추가 → `HeuristicRolloutPolicy` 디폴트 정책 사용. |
| `Assets/_Project/GameFlow/Agents/PimcAgent.cs` (수정) | 생성자에 `PolicyConfig`; `DecideTurn` 다세계 EV 투표(Easy=탐색OFF ε-휴리스틱). |
| `Assets/_Project/Tests/EditMode/PolicyConfigTests.cs` (생성) | 프리셋 값·티어 단조성. |
| `Assets/_Project/Tests/EditMode/HeuristicRolloutPolicyTests.cs` (생성) | ε=0 동일성·ε=1 무작위 합법·결정성·위임. |
| `Assets/_Project/Tests/EditMode/PimcAgentTests.cs` (수정) | P2-A 테스트를 새 시그니처로 갱신 + 다세계/Easy 테스트 추가. |

---

## Deviation from Spec (의도적 — CLAUDE.md 단순함)

- **`IPolicy` 인터페이스 미도입.** 롤아웃은 `GameDriver`(IAgent[] 수신)를 재사용하므로 정책 seam은 이미 `IAgent`다. ε-노이즈는 단일 휴리스틱 정책의 **파라미터화**(ε)이지 두 번째 다형 타입이 아니다 → 새 `IAgent` 구현 `HeuristicRolloutPolicy(epsilon)` 하나로 충분(ε=0이면 `AiAgent`와 동일). 진짜 다른 정책(학습/얕은탐색)이 생기면 그때 `IPolicy` 도입.
- **콜 게이트 const(Grand/Finish/Rich/BombMin) 외부화 보류.** 외부화하려면 frozen `AiAgent`를 고치거나 복제해야 하고, P2-B1에서 티어별로 이 값들이 달라질 필요가 없다(모든 티어가 표준 `AiAgent` 콜 사용). `PolicyConfig`는 지금 티어를 가르는 레버(Worlds/Rollouts/Epsilon)만 담는다. 게이트 외부화는 Hard/Expert 콜 보수화가 필요해지는 P2-D/E로 연기.
- **Hard/Expert 프리셋은 숫자만.** reach-prob 가중·EPIMC 등 고급 기능은 P2-D/E. P2-B1은 Easy/Normal 동작만 구현하고 Hard/Expert 프리셋은 세계수/롤아웃/ε 숫자만 정의(티어 단조성 확보).

---

### Task 1: Difficulty enum + PolicyConfig

**Files:**
- Create: `Assets/_Project/GameFlow/Agents/Difficulty.cs`
- Create: `Assets/_Project/GameFlow/Agents/PolicyConfig.cs`
- Test: `Assets/_Project/Tests/EditMode/PolicyConfigTests.cs`

**Interfaces:**
- Produces: `enum Tichu.GameFlow.Agents.Difficulty { Easy, Normal, Hard, Expert }`; `readonly struct PolicyConfig { int Worlds; int RolloutsPerWorld; double Epsilon; }` with ctor `(int worlds, int rolloutsPerWorld, double epsilon)` and `static PolicyConfig For(Difficulty d)`.

- [ ] **Step 1: 실패 테스트 작성**

`Assets/_Project/Tests/EditMode/PolicyConfigTests.cs`:

```csharp
using NUnit.Framework;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    public class PolicyConfigTests
    {
        [Test]
        public void For_maps_each_difficulty_to_distinct_preset()
        {
            var easy = PolicyConfig.For(Difficulty.Easy);
            var normal = PolicyConfig.For(Difficulty.Normal);

            Assert.That(easy.Worlds, Is.EqualTo(0), "Easy 는 탐색 OFF");
            Assert.That(normal.Worlds, Is.GreaterThan(0), "Normal 은 다세계");
            Assert.That(normal.RolloutsPerWorld, Is.GreaterThan(0));
            Assert.That(easy.Epsilon, Is.GreaterThan(normal.Epsilon), "Easy 가 더 무작위(블런더)");
        }

        [Test]
        public void Worlds_are_monotonic_non_decreasing_across_tiers()
        {
            int e = PolicyConfig.For(Difficulty.Easy).Worlds;
            int n = PolicyConfig.For(Difficulty.Normal).Worlds;
            int h = PolicyConfig.For(Difficulty.Hard).Worlds;
            int x = PolicyConfig.For(Difficulty.Expert).Worlds;
            Assert.That(e, Is.LessThanOrEqualTo(n));
            Assert.That(n, Is.LessThanOrEqualTo(h));
            Assert.That(h, Is.LessThanOrEqualTo(x));
        }

        [Test]
        public void Ctor_sets_fields()
        {
            var c = new PolicyConfig(7, 3, 0.2);
            Assert.That(c.Worlds, Is.EqualTo(7));
            Assert.That(c.RolloutsPerWorld, Is.EqualTo(3));
            Assert.That(c.Epsilon, Is.EqualTo(0.2).Within(1e-9));
        }
    }
}
```

- [ ] **Step 2: 실패 확인** — `Difficulty`/`PolicyConfig` 미정의 컴파일 에러. ImportAsset(테스트) → `read_console` 확인.

- [ ] **Step 3: 최소 구현 작성**

`Assets/_Project/GameFlow/Agents/Difficulty.cs`:

```csharp
namespace Tichu.GameFlow.Agents
{
    /// <summary>AI 난이도 티어. 차이는 PolicyConfig(탐색 예산·노이즈)뿐 — 별 구현/룰 없음.</summary>
    public enum Difficulty
    {
        Easy,
        Normal,
        Hard,
        Expert,
    }
}
```

`Assets/_Project/GameFlow/Agents/PolicyConfig.cs`:

```csharp
namespace Tichu.GameFlow.Agents
{
    /// <summary>
    /// PIMC 탐색/정책 파라미터. 난이도 티어는 이 값 주입만으로 구분된다.
    /// 숫자는 시작값(스펙 §5) — P2-C 벤치로 확정. Hard/Expert 의 reach-prob·EPIMC 등 고급
    /// 기능은 미구현(P2-D/E); 여기서는 세계수/롤아웃/ε 만 정의한다.
    /// </summary>
    public readonly struct PolicyConfig
    {
        /// <summary>결정화 세계 수. 0이면 탐색 OFF(휴리스틱 직접 결정).</summary>
        public readonly int Worlds;

        /// <summary>세계당 롤아웃 수(ε&gt;0일 때 노이즈 평균화에 의미).</summary>
        public readonly int RolloutsPerWorld;

        /// <summary>롤아웃 디폴트 정책의 무작위 확률(ε-greedy). 0이면 순수 휴리스틱.</summary>
        public readonly double Epsilon;

        public PolicyConfig(int worlds, int rolloutsPerWorld, double epsilon)
        {
            Worlds = worlds;
            RolloutsPerWorld = rolloutsPerWorld;
            Epsilon = epsilon;
        }

        /// <summary>난이도 티어별 시작 프리셋.</summary>
        public static PolicyConfig For(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Easy:   return new PolicyConfig(0, 0, 0.25);   // 탐색 OFF + 블런더
                case Difficulty.Normal: return new PolicyConfig(4, 2, 0.10);
                case Difficulty.Hard:   return new PolicyConfig(16, 4, 0.05);  // 고급기능 P2-D
                case Difficulty.Expert: return new PolicyConfig(24, 6, 0.00);  // 고급기능 P2-E
                default:                return new PolicyConfig(4, 2, 0.10);
            }
        }
    }
}
```

- [ ] **Step 4: 임포트 + 컴파일 + 통과** — `execute_code`로 3파일 ImportAsset(ForceUpdate)+Refresh → `isCompiling` False → `read_console` 0 → `run_tests(test_names=["Tichu.Core.Tests.PolicyConfigTests"])`. Expected: 3 PASS.

- [ ] **Step 5: 커밋**

```bash
git add "Assets/_Project/GameFlow/Agents/Difficulty.cs" "Assets/_Project/GameFlow/Agents/Difficulty.cs.meta" "Assets/_Project/GameFlow/Agents/PolicyConfig.cs" "Assets/_Project/GameFlow/Agents/PolicyConfig.cs.meta" "Assets/_Project/Tests/EditMode/PolicyConfigTests.cs" "Assets/_Project/Tests/EditMode/PolicyConfigTests.cs.meta"
git commit -m "feat(p2b1): Difficulty 티어 + PolicyConfig(세계수·롤아웃·ε) 프리셋"
```

---

### Task 2: HeuristicRolloutPolicy (ε-노이즈 정책)

**Files:**
- Create: `Assets/_Project/GameFlow/Agents/HeuristicRolloutPolicy.cs`
- Test: `Assets/_Project/Tests/EditMode/HeuristicRolloutPolicyTests.cs`

**Interfaces:**
- Consumes: `AiAgent(ulong, int)` 및 그 `IAgent` 메서드; `Tichu.Core.Rng`; `DecisionContext.LegalMoves/CanPass`.
- Produces: `public sealed class HeuristicRolloutPolicy : IAgent`, ctor `(ulong seed, int seat, double epsilon)`. `DecideTurn`: ε확률로 무작위 합법수(또는 패스), 아니면 `AiAgent.DecideTurn`. 나머지 5개 메서드는 `AiAgent` 위임. ε=0이면 RNG 미사용(= `AiAgent` 동일).

- [ ] **Step 1: 실패 테스트 작성**

`Assets/_Project/Tests/EditMode/HeuristicRolloutPolicyTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Game;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    public class HeuristicRolloutPolicyTests
    {
        private static List<Card> Hand(params Card[] c) => new List<Card>(c);
        private static Card N(int r, Suit s) => Card.Normal(r, s);

        // seat0 리드 상태(폭탄 없는 평이한 손).
        private static GameState LeadState()
        {
            return GameFlowHelpers.PlayState(0,
                Hand(N(3, Suit.Jade), N(5, Suit.Sword), N(9, Suit.Pagoda), N(11, Suit.Star)),
                Hand(N(4, Suit.Jade), N(6, Suit.Sword)),
                Hand(N(7, Suit.Jade), N(8, Suit.Sword)),
                Hand(N(2, Suit.Jade), N(10, Suit.Sword)));
        }

        [Test]
        public void Epsilon_zero_matches_AiAgent_decide_turn()
        {
            var s = LeadState();
            var ctx = GameFlowHelpers.Context(s, 0);
            var pol = new HeuristicRolloutPolicy(123UL, 0, 0.0);
            var ai = new AiAgent(123UL, 0);

            var dp = pol.DecideTurn(ctx);
            var da = ai.DecideTurn(ctx);
            Assert.That(dp.IsPass, Is.EqualTo(da.IsPass));
            Assert.That(dp.Move!.Rank, Is.EqualTo(da.Move!.Rank));
            Assert.That(dp.Move!.Type, Is.EqualTo(da.Move!.Type));
            Assert.That(dp.Move!.Length, Is.EqualTo(da.Move!.Length));
        }

        [Test]
        public void Epsilon_one_always_returns_a_legal_move_or_pass()
        {
            var s = LeadState();
            var ctx = GameFlowHelpers.Context(s, 0);
            var pol = new HeuristicRolloutPolicy(999UL, 0, 1.0);

            for (int i = 0; i < 50; i++)
            {
                var d = pol.DecideTurn(ctx);
                if (d.IsPass)
                {
                    Assert.That(ctx.CanPass, Is.True, "패스를 골랐으면 패스가 합법이어야 한다");
                }
                else
                {
                    bool legal = false;
                    foreach (var m in ctx.LegalMoves)
                        if (m.Rank == d.Move!.Rank && m.Type == d.Move!.Type && m.Length == d.Move!.Length) legal = true;
                    Assert.That(legal, Is.True, "무작위 수도 반드시 합법");
                }
            }
        }

        [Test]
        public void Is_deterministic_for_same_seed()
        {
            var s1 = LeadState();
            var s2 = LeadState();
            var a = new HeuristicRolloutPolicy(77UL, 0, 0.5);
            var b = new HeuristicRolloutPolicy(77UL, 0, 0.5);
            for (int i = 0; i < 20; i++)
            {
                var da = a.DecideTurn(GameFlowHelpers.Context(s1, 0));
                var db = b.DecideTurn(GameFlowHelpers.Context(s2, 0));
                Assert.That(da.IsPass, Is.EqualTo(db.IsPass));
                if (!da.IsPass)
                    Assert.That(da.Move!.Rank, Is.EqualTo(db.Move!.Rank));
            }
        }

        [Test]
        public void Non_turn_decisions_delegate_to_AiAgent()
        {
            // 강한 8장 → CallGrandTichu 위임 결과가 AiAgent 와 동일.
            var strong = Hand(Card.Dragon, Card.Phoenix,
                N(14, Suit.Jade), N(14, Suit.Sword), N(13, Suit.Pagoda), N(13, Suit.Star),
                N(3, Suit.Jade), N(4, Suit.Sword));
            var s = GameFlowHelpers.PlayState(0, strong,
                Hand(N(2, Suit.Jade)), Hand(N(2, Suit.Sword)), Hand(N(2, Suit.Pagoda)));
            var ctx = GameFlowHelpers.Context(s, 0);
            Assert.That(new HeuristicRolloutPolicy(1UL, 0, 0.9).CallGrandTichu(ctx),
                        Is.EqualTo(new AiAgent(1UL, 0).CallGrandTichu(ctx)));
        }
    }
}
```

- [ ] **Step 2: 실패 확인** — `HeuristicRolloutPolicy` 미정의. ImportAsset(테스트)→`read_console`.

- [ ] **Step 3: 최소 구현 작성**

`Assets/_Project/GameFlow/Agents/HeuristicRolloutPolicy.cs`:

```csharp
using Tichu.Core;
using Tichu.Core.Combinations;

namespace Tichu.GameFlow.Agents
{
    /// <summary>
    /// 롤아웃 디폴트 정책: ε 확률로 무작위 합법수(블런더), 아니면 현 휴리스틱(AiAgent).
    /// 비-턴 결정(그랜드/교환/티츄/폭탄/용양도)은 AiAgent 에 그대로 위임한다.
    /// ε=0 이면 RNG 를 전혀 건드리지 않아 AiAgent 와 비트동일하다.
    /// Easy 티어의 직접 결정과 PIMC 롤아웃의 디폴트 정책, 두 곳에서 재사용된다.
    /// </summary>
    public sealed class HeuristicRolloutPolicy : IAgent
    {
        private readonly AiAgent _heuristic;
        private readonly double _epsilon;
        private Rng _rng;   // 비-readonly: 직접 메서드 호출로 전진(AiAgent/RandomAgent 패턴).

        public HeuristicRolloutPolicy(ulong seed, int seat, double epsilon)
        {
            _heuristic = new AiAgent(seed, seat);
            _epsilon = epsilon;
            _rng = new Rng(seed ^ 0xB0E1_0000_0000_0001UL ^ (ulong)seat);
        }

        public TurnDecision DecideTurn(in DecisionContext ctx)
        {
            if (_epsilon > 0.0)
            {
                // [0,1) 난수: NextULong 상위 53비트로 double. ε 미만이면 무작위 합법수.
                double u = (_rng.NextULong() >> 11) * (1.0 / 9007199254740992.0); // 2^53
                if (u < _epsilon)
                {
                    var moves = ctx.LegalMoves;
                    bool canPass = ctx.CanPass;
                    int n = moves.Count + (canPass ? 1 : 0);
                    if (n > 0)
                    {
                        int pick = _rng.NextInt(n);
                        return pick < moves.Count ? TurnDecision.Play(moves[pick]) : TurnDecision.Pass;
                    }
                }
            }
            return _heuristic.DecideTurn(ctx);
        }

        public bool CallGrandTichu(in DecisionContext ctx) => _heuristic.CallGrandTichu(ctx);
        public ExchangeChoice ChooseExchange(in DecisionContext ctx) => _heuristic.ChooseExchange(ctx);
        public bool CallTichu(in DecisionContext ctx) => _heuristic.CallTichu(ctx);
        public Combination? DecideBomb(in DecisionContext ctx) => _heuristic.DecideBomb(ctx);
        public int ChooseDragonRecipient(in DecisionContext ctx) => _heuristic.ChooseDragonRecipient(ctx);
    }
}
```

> **주(ε branch와 합법성):** 무작위 분기는 `ctx.LegalMoves ∪ {Pass?}`에서만 고르므로 항상 합법이다. ε=0이면 `_rng`를 안 건드려 `AiAgent`와 비트동일(롤아웃 회귀 보장).

- [ ] **Step 4: 임포트 + 컴파일 + 통과** — 2파일 ImportAsset+Refresh → 컴파일 0 → `run_tests(test_names=["Tichu.Core.Tests.HeuristicRolloutPolicyTests"])`. Expected: 4 PASS.

- [ ] **Step 5: 커밋**

```bash
git add "Assets/_Project/GameFlow/Agents/HeuristicRolloutPolicy.cs" "Assets/_Project/GameFlow/Agents/HeuristicRolloutPolicy.cs.meta" "Assets/_Project/Tests/EditMode/HeuristicRolloutPolicyTests.cs" "Assets/_Project/Tests/EditMode/HeuristicRolloutPolicyTests.cs.meta"
git commit -m "feat(p2b1): HeuristicRolloutPolicy — ε-noise 롤아웃 정책(ε=0=AiAgent 동일)"
```

---

### Task 3: Pimc.Rollout ε-통합

**Files:**
- Modify: `Assets/_Project/GameFlow/Agents/Pimc.cs`
- Modify: `Assets/_Project/Tests/EditMode/PimcAgentTests.cs` (P2-A 롤아웃 테스트 시그니처 갱신 + ε 테스트 추가)

**Interfaces:**
- Consumes: `HeuristicRolloutPolicy(ulong, int, double)`; `GameDriver`; `Seating.TeamOf`.
- Produces: `public static int Pimc.Rollout(GameState world, int observerSeat, ulong policySeed, double epsilon)` — 디폴트 정책을 `HeuristicRolloutPolicy(policySeed, seat, epsilon)`로 교체. ε=0이면 P2-A 결과와 동일.

- [ ] **Step 1: 테스트 갱신(실패 유도)**

`PimcAgentTests.cs`의 기존 3개 롤아웃 테스트에서 `Pimc.Rollout(...)` 호출에 `epsilon` 인자를 추가한다. 정확한 치환(Edit):

기존:
```csharp
            int reward = Pimc.Rollout(world.Clone(), observerSeat: 0, policySeed: 777UL);
```
→
```csharp
            int reward = Pimc.Rollout(world.Clone(), observerSeat: 0, policySeed: 777UL, epsilon: 0.0);
```

기존:
```csharp
            int a = Pimc.Rollout(world.Clone(), 0, 777UL);
            int b = Pimc.Rollout(world.Clone(), 0, 777UL);
```
→
```csharp
            int a = Pimc.Rollout(world.Clone(), 0, 777UL, 0.0);
            int b = Pimc.Rollout(world.Clone(), 0, 777UL, 0.0);
```

기존:
```csharp
            int teamA = Pimc.Rollout(world.Clone(), observerSeat: 0, policySeed: 777UL);
            int teamB = Pimc.Rollout(world.Clone(), observerSeat: 1, policySeed: 777UL);
```
→
```csharp
            int teamA = Pimc.Rollout(world.Clone(), observerSeat: 0, policySeed: 777UL, epsilon: 0.0);
            int teamB = Pimc.Rollout(world.Clone(), observerSeat: 1, policySeed: 777UL, epsilon: 0.0);
```

그리고 ε 결정성 테스트 1개 추가(클래스 안, 롤아웃 섹션에 이어서):

```csharp
        [Test]
        public void Rollout_with_epsilon_is_deterministic_and_can_differ_from_pure()
        {
            var src = FreshPlayState(0);
            var rng = new Rng(11UL);
            var world = Determinizer.Sample(src, 0, ref rng);

            int pure  = Pimc.Rollout(world.Clone(), 0, 555UL, 0.0);
            int noisyA = Pimc.Rollout(world.Clone(), 0, 555UL, 0.5);
            int noisyB = Pimc.Rollout(world.Clone(), 0, 555UL, 0.5);
            Assert.That(noisyA, Is.EqualTo(noisyB), "같은 세계+시드+ε → 같은 보상(결정적)");
            Assert.That(noisyA, Is.InRange(-600, 600));
            // pure 와 다를 수 있음을 단언하진 않음(우연히 같을 수 있음) — 결정성만 못박는다.
        }
```

- [ ] **Step 2: 실패 확인** — 기존 시그니처(3인자) 호출이 새 4인자와 안 맞아 컴파일 에러(또는 신규 테스트의 4인자 호출이 3인자 메서드와 불일치). ImportAsset(테스트)→`read_console`로 `Pimc.Rollout` 오버로드 불일치 확인.

- [ ] **Step 3: 구현 갱신**

`Pimc.cs`를 다음으로 치환(Edit, 메서드 전체):

기존:
```csharp
        public static int Rollout(GameState world, int observerSeat, ulong policySeed)
        {
            var agents = new IAgent[4];
            for (int seat = 0; seat < 4; seat++)
                agents[seat] = new AiAgent(policySeed, seat);

            var outcome = new GameDriver(agents).RunRound(world);
            int diff = outcome.Result.TeamATotal - outcome.Result.TeamBTotal;
            return Seating.TeamOf(observerSeat) == 0 ? diff : -diff;
        }
```
→
```csharp
        public static int Rollout(GameState world, int observerSeat, ulong policySeed, double epsilon)
        {
            var agents = new IAgent[4];
            for (int seat = 0; seat < 4; seat++)
                agents[seat] = new HeuristicRolloutPolicy(policySeed, seat, epsilon);

            var outcome = new GameDriver(agents).RunRound(world);
            int diff = outcome.Result.TeamATotal - outcome.Result.TeamBTotal;
            return Seating.TeamOf(observerSeat) == 0 ? diff : -diff;
        }
```

XML 주석의 "현 휴리스틱(AiAgent)"은 "ε-노이즈 휴리스틱(HeuristicRolloutPolicy; ε=0이면 AiAgent 동일)"로 한 줄 갱신.

- [ ] **Step 4: 임포트 + 컴파일 + 통과** — Pimc.cs + PimcAgentTests.cs ImportAsset+Refresh → 컴파일 0 → `run_tests(test_names=["Tichu.Core.Tests.PimcAgentTests"])`. **이 시점 PimcAgentTests의 PimcAgent 관련 4개 테스트는 아직 옛 생성자라 컴파일 실패할 수 있다** → Task 4와 함께 통과시킨다. 따라서 Step 4에서는 **롤아웃 4개 테스트만** 임시로 통과 확인하려면, PimcAgent 생성자 변경 전이므로 이 태스크는 Task 4와 한 컴파일 단위로 묶어 진행한다(아래 주 참조).

> **⚠️ 컴파일 결합 주의:** Task 3(Pimc.Rollout 시그니처)과 Task 4(PimcAgent 생성자)는 같은 `PimcAgentTests.cs`를 건드리고 `PimcAgent`가 `Pimc.Rollout`을 호출하므로, **Task 3 구현 → 곧바로 Task 4 구현까지 진행한 뒤 한 번에 컴파일·전체 PimcAgentTests 통과**시키는 것이 안전하다. 커밋은 논리 단위로 둘로 나눈다(Task 3 = Pimc.cs + 롤아웃 테스트 갱신, Task 4 = PimcAgent.cs + 에이전트 테스트). 즉 Step 5 커밋은 Task 4 통과 후 두 번에 나눠 수행한다.

- [ ] **Step 5: (Task 4 통과 후) 커밋**

```bash
git add "Assets/_Project/GameFlow/Agents/Pimc.cs"
git commit -m "feat(p2b1): Pimc.Rollout ε-noise 정책 통합(HeuristicRolloutPolicy)"
```

---

### Task 4: PimcAgent 다세계 + PolicyConfig

**Files:**
- Modify: `Assets/_Project/GameFlow/Agents/PimcAgent.cs`
- Modify: `Assets/_Project/Tests/EditMode/PimcAgentTests.cs` (P2-A 에이전트 테스트 생성자 갱신 + 다세계/Easy 테스트)

**Interfaces:**
- Consumes: `PolicyConfig`; `Determinizer.Sample(GameState, int, ref Rng)`; `Pimc.Rollout(GameState, int, ulong, double)`; `HeuristicRolloutPolicy`; `MoveOrder.Strength`; `GameEngine.Apply`/`GameAction`.
- Produces: `PimcAgent(ulong roundSeed, int seat, PolicyConfig config) : IAgent`. `DecideTurn`: `config.Worlds<=0`이면 ε-휴리스틱 직접 결정; 아니면 `config.Worlds`세계 × `config.RolloutsPerWorld`롤아웃 평균 EV argmax(동점 `MoveOrder.Strength` 최소). 비-턴 5결정은 휴리스틱 위임.

- [ ] **Step 1: 테스트 갱신(실패 유도)**

(a) `PimcAgentTests.cs`의 기존 4개 PimcAgent 테스트에서 `new PimcAgent(<seed>, <seat>)` → `new PimcAgent(<seed>, <seat>, PolicyConfig.Normal)` 로 치환(Edit, 각 발생 위치):
- `new PimcAgent(2024UL, 0)` → `new PimcAgent(2024UL, 0, PolicyConfig.Normal)`
- `new PimcAgent(55UL, 0)` (2곳) → `new PimcAgent(55UL, 0, PolicyConfig.Normal)`
- `new PimcAgent(1UL, 0)` → `new PimcAgent(1UL, 0, PolicyConfig.Normal)`
- `new PimcAgent(900UL, 0..3)` (4곳) → `new PimcAgent(900UL, <seat>, PolicyConfig.Normal)`

(b) Easy(탐색 OFF) + 다세계 추가 테스트(클래스 안):

```csharp
        [Test]
        public void Easy_config_returns_legal_move_without_search()
        {
            // Worlds=0 → 탐색 없이 ε-휴리스틱 직접 결정. 합법수여야 한다.
            var s = FreshPlayState(0);
            var ctx = GameFlowHelpers.Context(s, 0);
            var d = new PimcAgent(2024UL, 0, PolicyConfig.For(Difficulty.Easy)).DecideTurn(ctx);
            Assert.That(d.IsPass, Is.False, "리드는 패스 불가");
            bool legal = false;
            foreach (var m in ctx.LegalMoves)
                if (m.Rank == d.Move!.Rank && m.Type == d.Move!.Type && m.Length == d.Move!.Length) legal = true;
            Assert.That(legal, Is.True);
        }

        [Test]
        public void Multiworld_decide_turn_is_deterministic()
        {
            var cfg = new PolicyConfig(4, 2, 0.1);
            var s1 = FreshPlayState(0);
            var s2 = FreshPlayState(0);
            var d1 = new PimcAgent(55UL, 0, cfg).DecideTurn(GameFlowHelpers.Context(s1, 0));
            var d2 = new PimcAgent(55UL, 0, cfg).DecideTurn(GameFlowHelpers.Context(s2, 0));
            Assert.That(d1.IsPass, Is.EqualTo(d2.IsPass));
            if (!d1.IsPass)
            {
                Assert.That(d1.Move!.Rank, Is.EqualTo(d2.Move!.Rank));
                Assert.That(d1.Move!.Type, Is.EqualTo(d2.Move!.Type));
                Assert.That(d1.Move!.Length, Is.EqualTo(d2.Move!.Length));
            }
        }

        [Test]
        public void Four_normal_pimc_agents_complete_a_round()
        {
            var cfg = PolicyConfig.Normal;
            var s = FreshPlayState(0);
            var agents = new IAgent[]
            {
                new PimcAgent(900UL, 0, cfg), new PimcAgent(900UL, 1, cfg),
                new PimcAgent(900UL, 2, cfg), new PimcAgent(900UL, 3, cfg),
            };
            var outcome = new GameDriver(agents).RunRound(s);
            Assert.That(outcome.State.Phase, Is.EqualTo(RoundPhase.RoundEnd));
            Assert.That(outcome.Result, Is.Not.Null);
        }
```

> 기존 `DecideTurn_is_deterministic_for_same_state_and_seed`(단일세계 가정)와 신규 `Multiworld_decide_turn_is_deterministic`는 둘 다 유효(Normal=다세계). 중복이면 기존 것을 새 다세계 버전이 대체하므로, 기존 테스트는 생성자만 갱신해 유지한다.

- [ ] **Step 2: 실패 확인** — 옛 생성자/Worlds 미반영으로 컴파일 또는 단언 실패. ImportAsset(테스트)→`read_console`.

- [ ] **Step 3: 구현 갱신**

`PimcAgent.cs` 전체를 다음으로 치환(Write):

```csharp
using Tichu.Core;
using Tichu.Core.Combinations;
using Tichu.Core.Game;

namespace Tichu.GameFlow.Agents
{
    /// <summary>
    /// 다세계 PIMC 탐색 에이전트(P2-B1). PolicyConfig 주입으로 티어를 가른다.
    /// DecideTurn: Worlds&gt;0 이면 config.Worlds 세계를 결정화하고 각 루트 합법수를
    /// config.RolloutsPerWorld 회(ε-노이즈) 롤아웃해 세계·롤아웃 평균 EV(관측 팀 부호)가 최대인
    /// 수를 고른다(동점깨기 MoveOrder.Strength 최소). Worlds==0(Easy)이면 탐색 없이 ε-휴리스틱
    /// 직접 결정. 나머지 결정은 휴리스틱 위임. 고정 노드수에서 결정적(결정화 셔플·ε만 Rng).
    /// </summary>
    public sealed class PimcAgent : IAgent
    {
        private readonly ulong _roundSeed;
        private readonly PolicyConfig _config;
        private readonly AiAgent _heuristic;                 // 비-턴 결정 위임.
        private readonly HeuristicRolloutPolicy _easyDirect; // Worlds==0 직접 결정(ε-휴리스틱).
        private Rng _rng;                                    // 결정화 세계 샘플링.

        public PimcAgent(ulong roundSeed, int seat, PolicyConfig config)
        {
            _roundSeed = roundSeed;
            _config = config;
            _heuristic = new AiAgent(roundSeed, seat);
            _easyDirect = new HeuristicRolloutPolicy(roundSeed, seat, config.Epsilon);
            _rng = new Rng(roundSeed ^ 0x91C0_0000_0000_0001UL ^ (ulong)seat);
        }

        public TurnDecision DecideTurn(in DecisionContext ctx)
        {
            var legal = ctx.LegalMoves;
            if (legal.Count == 0)
                return TurnDecision.Pass;

            // Easy: 탐색 OFF → ε-휴리스틱 직접 결정.
            if (_config.Worlds <= 0)
                return _easyDirect.DecideTurn(ctx);

            ulong policyBase = _roundSeed ^ 0x5043_0000_0000_0001UL ^ (ulong)ctx.Seat;
            int rolloutsPerWorld = _config.RolloutsPerWorld < 1 ? 1 : _config.RolloutsPerWorld;

            // 각 합법수의 누적 EV(세계×롤아웃 합). 패스는 마지막에 별도 집계.
            var sumEv = new long[legal.Count];
            var rng = _rng;

            for (int w = 0; w < _config.Worlds; w++)
            {
                var world = Determinizer.Sample(ctx.State, ctx.Seat, ref rng);
                for (int r = 0; r < rolloutsPerWorld; r++)
                {
                    // 공통 난수(variance reduction): 같은 (w,r)에서 모든 수가 같은 ε-시퀀스 사용.
                    ulong rolloutSeed = policyBase + (ulong)(w * rolloutsPerWorld + r);
                    for (int i = 0; i < legal.Count; i++)
                    {
                        var sim = world.Clone();
                        var applied = GameEngine.Apply(sim, GameAction.Play(ctx.Seat, legal[i].Cards)); // P2-B1: wish=null
                        if (!applied.Ok) continue;
                        sumEv[i] += Pimc.Rollout(sim, ctx.Seat, rolloutSeed, _config.Epsilon);
                    }
                }
            }

            // 패스 EV(합법일 때): 같은 세계들을 다시 쓰면 비용 2배 → 별도 가벼운 추정으로,
            // 패스는 1세계만 평가(P2-B1; 패스는 점수 양보 신호라 1세계로 충분). 결정성 위해 새 rng 사용 안 함.
            long bestSum = long.MinValue;
            int bestStrength = int.MaxValue;
            Combination? best = null;
            for (int i = 0; i < legal.Count; i++)
            {
                if (sumEv[i] == 0 && best == null && i > 0) { /* no-op: 모든 reject 방어 아래에서 처리 */ }
                int strength = MoveOrder.Strength(legal[i]);
                if (sumEv[i] > bestSum || (sumEv[i] == bestSum && strength < bestStrength))
                {
                    bestSum = sumEv[i];
                    bestStrength = strength;
                    best = legal[i];
                }
            }

            if (ctx.CanPass)
            {
                var world = Determinizer.Sample(ctx.State, ctx.Seat, ref rng);
                var sim = world.Clone();
                var applied = GameEngine.Apply(sim, GameAction.Pass(ctx.Seat));
                if (applied.Ok)
                {
                    // 패스 EV를 동일 스케일(세계×롤아웃 합)로 환산: 1세계 EV × (Worlds*rolloutsPerWorld).
                    long passEv = (long)Pimc.Rollout(sim, ctx.Seat, policyBase, _config.Epsilon)
                                  * _config.Worlds * rolloutsPerWorld;
                    if (passEv > bestSum)   // 동점이면 수를 선호.
                        return TurnDecision.Pass;
                }
            }

            _rng = rng;
            return best == null ? TurnDecision.Pass : TurnDecision.Play(best);
        }

        public bool CallGrandTichu(in DecisionContext ctx) => _heuristic.CallGrandTichu(ctx);
        public ExchangeChoice ChooseExchange(in DecisionContext ctx) => _heuristic.ChooseExchange(ctx);
        public bool CallTichu(in DecisionContext ctx) => _heuristic.CallTichu(ctx);
        public Combination? DecideBomb(in DecisionContext ctx) => _heuristic.DecideBomb(ctx);
        public int ChooseDragonRecipient(in DecisionContext ctx) => _heuristic.ChooseDragonRecipient(ctx);
    }
}
```

> **⚠️ 구현 정리(실행자 필수 단순화):** 위 best-선택 루프의 `if (sumEv[i]==0 && ...)` 줄은 의미 없는 자리표시자다 — **삭제**하고 순수 argmax만 남겨라. 또한 `_rng` 되쓰기는 패스 분기 전에 두면 패스의 Determinizer 호출이 rng를 더 전진시키므로, **`_rng = rng;`는 메서드 끝(모든 Determinizer.Sample 이후)에서 한 번만** 수행한다(위 코드대로). best==null(모든 Apply reject — 합법수에선 비도달)일 때만 Pass 폴백.
>
> **패스 EV 스케일 주의:** 수 EV는 세계×롤아웃 **합**, 패스는 1세계 EV를 같은 배수로 곱해 비교 스케일을 맞춘다. 결정성에는 영향 없음(전부 시드 파생).

- [ ] **Step 4: 임포트 + 컴파일 + 통과** — `PimcAgent.cs` + `Pimc.cs`(Task 3) + `PimcAgentTests.cs` 전부 ImportAsset(ForceUpdate)+Refresh → `isCompiling` False → `read_console` 0 → `run_tests(test_names=["Tichu.Core.Tests.PimcAgentTests"])`. Expected: 전체 PASS(P2-A 7 갱신 + Task3 ε 1 + Task4 신규 3 = 11). 4봇 다세계 라운드는 P2-A보다 무거우니(세계4×롤아웃2) 시간 여유: `init_timeout=120000`, `get_test_job(wait_timeout=90)`, `stuck_suspected` 감시.

- [ ] **Step 5: 회귀 + 커밋(둘로 분할)**

회귀: `run_tests(test_names=["Tichu.Core.Tests.DeterminizerTests","Tichu.Core.Tests.PolicyConfigTests","Tichu.Core.Tests.HeuristicRolloutPolicyTests","Tichu.Core.Tests.AiAgentTests","Tichu.Core.Tests.MoveOrderTests"])` → 전부 그린.

```bash
# Task 3 커밋(아직 안 했다면)
git add "Assets/_Project/GameFlow/Agents/Pimc.cs"
git commit -m "feat(p2b1): Pimc.Rollout ε-noise 정책 통합(HeuristicRolloutPolicy)"

# Task 4 커밋
git add "Assets/_Project/GameFlow/Agents/PimcAgent.cs" "Assets/_Project/Tests/EditMode/PimcAgentTests.cs"
git commit -m "feat(p2b1): PimcAgent 다세계 EV 투표 + PolicyConfig(Easy/Normal 티어)"
```

---

## Self-Review

**1. Spec coverage (§11 P2-B 중 동기 코어분):**
- 다세계 결정화(worlds≈4) + DecideTurn 수별 EV 투표 → Task 4 ✓
- PolicyConfig → Task 1 ✓ (콜게이트 const 외부화는 의도적 연기, Deviation 절)
- Easy/Normal 티어 매핑(ε노이즈) → Task 1(프리셋) + Task 2(ε정책) + Task 4(Easy 분기) ✓
- `IPolicy` → 미도입(Deviation: IAgent seam + HeuristicRolloutPolicy로 대체) ✓
- **비동기 `PimcDecisionAgent`·`RoundBootstrap` 난이도 주입·anytime은 본 계획 범위 밖**(P2-B2). 사용자 승인(범위=동기 코어 먼저). ✓
- 검증(§12.3 PIMC 결정성·§12.8 티어 단조성 precursor) → PolicyConfigTests/PimcAgentTests ✓. 오라클 불침범(§12.4)=P2-B1은 오라클 경로 무수정이라 자동 보존(회귀로 AiAgent/MoveOrder 그린 확인).

**2. Placeholder scan:** Task 4 구현 코드에 의도적으로 남긴 자리표시자 1곳(`if (sumEv[i]==0 ...)`)을 Step 3 "구현 정리" 주에서 **삭제 지시**로 명시. 그 외 TBD 없음. (실행자는 순수 argmax로 정리할 것.)

**3. Type consistency:** `PolicyConfig(int,int,double)`/`For(Difficulty)` — Task 1 정의, Task 4 소비 일치. `HeuristicRolloutPolicy(ulong,int,double)` — Task 2 정의, Task 3·4 소비 일치. `Pimc.Rollout(GameState,int,ulong,double)` — Task 3 정의, Task 4 소비 일치. `PimcAgent(ulong,int,PolicyConfig)` — Task 4, 테스트 갱신 일치. `MoveOrder.Strength`/`GameAction.Play|Pass`/`Seating.TeamOf`/`Determinizer.Sample(…, ref Rng)` — 실제 소스 대조 완료.

**미해결/주의:**
- **다세계 비용:** worlds×rolloutsPerWorld×legalMoves×(롤아웃 1라운드). Normal(4×2)이면 P2-A 대비 ~8배. 4봇 풀라운드 테스트가 수 초~십수 초 가능 → `wait_timeout` 여유. 단일 라운드라 MCP-stuck(10만판) 위험은 아님. 과하면 테스트용 config를 (2,1,0.1)로 낮춰도 됨(결정성·합법성 검증 목적엔 충분).
- **패스 EV 근사(1세계):** P2-B1 단순화. 패스가 자주 과대/과소평가되면 P2-C 벤치에서 드러남 → 그때 패스도 다세계로 승격 검토.
- **wish=null(루트):** P2-A와 동일 유지(롤아웃 내부는 정책이 처리). 소원 탐색은 후속.

---

## Execution Handoff

**Plan complete and saved to `docs/superpowers/plans/2026-06-24-tichu-p2b1-multiworld-core.md`. 실행 방식:**

**1. Inline Execution (이전 P2-A와 동일, 권장)** — 이 세션에서 executing-plans로 Task 1→4 순차, UnityMCP 워밍 유지.

**2. Subagent-Driven** — 태스크별 subagent + 리뷰(단, UnityMCP 단일 인스턴스 경합 위험).

**Which approach?**
