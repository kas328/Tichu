# P2-B2 — 비동기 PimcDecisionAgent + anytime + 난이도 배선 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** P2-B1 동기 다세계 PIMC를 **인게임에서 실제로 돌게** 한다 — 메인스레드 비차단 백그라운드 탐색 + anytime(시간예산 초과 → best-so-far) + 난이도 주입. 끝나면 사람이 AI(Normal PIMC)와 한 판을 플레이할 수 있다.

**Architecture:** anytime 로직은 **동기 코어**(`PimcAgent.DecideTurnAnytime(ctx, budget, abort)`)에 둔다 — `CancellationToken` 2개(예산/중단)로 제어, 토큰이 None이면 기존 고정노드수 탐색과 비트동일(결정적, EditMode 단위테스트). 얇은 비동기 래퍼 `PimcDecisionAgent : IDecisionAgent`는 `ctx.State.Clone()` 후 `UniTask.RunOnThreadPool`로 코어를 호출하고, 예산 CTS(`CancelAfter`)와 외부 ct(폭탄인터럽트)를 별 토큰으로 넘긴다. `RoundBootstrap`이 `GameLaunchArgs.Difficulty`→`PolicyConfig`로 AI 좌석을 생성한다.

**Tech Stack:** C# · Cysharp.UniTask · `Tichu.GameFlow`(코어, noEngineReferences) + `Tichu.Presentation`(래퍼/배선) · NUnit EditMode.

## Global Constraints

설계 스펙 §6(비동기/anytime)·§7(결정성). P2-B1은 `main`(머지 `4154156`)에 있다. 모든 태스크 암묵 포함.

- **코어 anytime은 토큰 기반(Stopwatch 금지).** `PimcAgent.DecideTurnAnytime`는 `CancellationToken budget`(예산)·`CancellationToken abort`(중단)만 본다. 시간 측정(`CancelAfter`)은 **Presentation 래퍼**가 담당(코어는 `IsCancellationRequested`만 점검). → 코어는 `System.Diagnostics.Stopwatch`/`DateTime` 미사용 → 고정노드수(토큰 None) 모드에서 결정적.
- **결정성 회귀:** `DecideTurn(ctx)` == `DecideTurnAnytime(ctx, None, None)` 비트동일. 기존 P2-B1 PimcAgentTests 9개 그대로 그린.
- **예산취소 ↔ 폭탄인터럽트 취소 구분(스펙 §6, CRITICAL):** 예산(budget) 만료 → **best-so-far 반환(throw 안 함)**; 외부 ct(abort, 폭탄인터럽트) → **OCE 전파**(드라이버가 이 턴 폐기). 두 신호는 **별개 토큰**.
- **메인스레드 안전:** `UniTask.RunOnThreadPool`(configureAwait 기본=메인 복귀)로 탐색은 백그라운드, 결과 적용·뷰갱신은 메인. 백그라운드 진입 전 `ctx.State.Clone()` 필수(가변 공유 차단). `LegalMoveGenerator`의 `[ThreadStatic]` 버퍼는 단일 워커라 안전.
- **AiAgent·오라클 불변.** `AiAgent` 무수정. 탐색 AI는 동기/비동기 `ComputeHash` 오라클 경로(휴리스틱 전용) 미진입.
- **에이전트 생성자:** `PimcDecisionAgent(ulong roundSeed, int seat, PolicyConfig config, int budgetMs, int delayMs, System.Func<bool> fastForward = null)` — 기존 `DelayedAiDecisionAgent`의 delay/fastForward(사람 관전 페이싱) 패턴 계승 + 스레드 탐색.
- **⚠️ 테스트 실행:** `run_tests(test_names=["Tichu.Core.Tests.<C>"])` / `["Tichu.Presentation.Tests.<C>"])` — 정규화 전체이름, `summary.total>0` 확인, **전체 스위트 금지**, PlayMode 정지. 신규/수정 .cs는 `execute_code`로 `ImportAsset(ForceUpdate)`+`Refresh`→`isCompiling` False→`read_console` 0→테스트.
- **⚠️ 스레드 테스트 주의:** PimcDecisionAgent.DecideTurnAsync는 진짜 비동기(RunOnThreadPool). EditMode에서 `async Task` 테스트 + `.AsTask()`로 await하되, **delay는 fastForward=()=>true로 스킵**(UniTask.Delay는 PlayerLoop 의존). 만약 스레드 테스트가 hang/stuck_suspected면 즉시 제거하고 동기 코어 커버리지+수동 인게임 검증으로 대체(MCP-stuck 회피).

---

## File Structure

| 파일 | 책임 |
|---|---|
| `Assets/_Project/GameFlow/Agents/PimcAgent.cs` (수정) | `DecideTurnAnytime(in DecisionContext, CancellationToken budget, CancellationToken abort)` 추가; `DecideTurn`은 그 (None,None) 위임. |
| `Assets/_Project/Presentation/Agents/PimcDecisionAgent.cs` (생성) | `IDecisionAgent`. delay+fastForward, DecideTurnAsync=Clone+RunOnThreadPool(anytime), 나머지 즉시 위임. |
| `Assets/_Project/Presentation/Shell/GameLaunchArgs.cs` (수정) | `Difficulty Difficulty = Difficulty.Normal;` 필드 추가. |
| `Assets/_Project/Presentation/RoundBootstrap.cs` (수정) | AI 좌석을 `PimcDecisionAgent`로 생성(난이도→PolicyConfig+budget). |
| `Assets/_Project/Tests/EditMode/PimcAgentTests.cs` (수정) | DecideTurnAnytime 3 테스트 추가. |
| `Assets/_Project/Presentation/Tests/PimcDecisionAgentTests.cs` (생성) | 위임 + 스레드 DecideTurnAsync + abort. |

---

## Deviations / Scope notes
- **anytime를 코어 토큰으로**(스펙은 "deadline(Stopwatch)"라 적었으나) — 토큰이 결정성 제약(§7: 코어 Stopwatch 금지)에 더 맞고 테스트가 결정적. 시간측정은 래퍼의 `CancelAfter`.
- **budgetMs는 배선 책임**(PolicyConfig에 안 넣음) — 코어는 토큰만 보므로 ms는 Presentation 관심사. `RoundBootstrap`이 난이도→budgetMs 매핑.
- **난이도 선택 UI는 범위 밖** — `GameLaunchArgs.Difficulty` 기본 Normal. 메뉴 셀렉터는 후속(필드만 준비).
- **Easy(worlds=0) 즉시 경로** — 탐색 없으니 스레드풀 우회(즉시 휴리스틱).

---

### Task 1: PimcAgent.DecideTurnAnytime (동기 anytime 코어)

**Files:**
- Modify: `Assets/_Project/GameFlow/Agents/PimcAgent.cs`
- Test: `Assets/_Project/Tests/EditMode/PimcAgentTests.cs`

**Interfaces:**
- Produces: `public TurnDecision DecideTurnAnytime(in DecisionContext ctx, System.Threading.CancellationToken budget, System.Threading.CancellationToken abort)`. budget 만료 시(≥1샘플 후) best-so-far 반환; abort 시 `OperationCanceledException`. `DecideTurn(ctx)` = `DecideTurnAnytime(ctx, None, None)`.

- [ ] **Step 1: 실패 테스트 작성** — `PimcAgentTests.cs`에 추가(클래스 안, 파일 상단 `using System.Threading;` 추가):

```csharp
        // ── anytime (동기 코어) ────────────────────────────────────────────────────

        [Test]
        public void DecideTurnAnytime_with_no_tokens_equals_DecideTurn()
        {
            var s1 = FreshPlayState(0);
            var s2 = FreshPlayState(0);
            var d1 = new PimcAgent(55UL, 0, PolicyConfig.Normal).DecideTurn(GameFlowHelpers.Context(s1, 0));
            var d2 = new PimcAgent(55UL, 0, PolicyConfig.Normal)
                .DecideTurnAnytime(GameFlowHelpers.Context(s2, 0), CancellationToken.None, CancellationToken.None);
            Assert.That(d1.IsPass, Is.EqualTo(d2.IsPass));
            if (!d1.IsPass)
            {
                Assert.That(d1.Move!.Rank, Is.EqualTo(d2.Move!.Rank));
                Assert.That(d1.Move!.Type, Is.EqualTo(d2.Move!.Type));
            }
        }

        [Test]
        public void DecideTurnAnytime_budget_cancelled_returns_legal_best_so_far()
        {
            // 예산이 미리 취소돼 있어도 ≥1 샘플 완료 후 합법수(best-so-far)를 throw 없이 반환.
            var s = FreshPlayState(0);
            var ctx = GameFlowHelpers.Context(s, 0);
            using var budget = new CancellationTokenSource();
            budget.Cancel(); // 즉시 만료
            var d = new PimcAgent(7UL, 0, new PolicyConfig(8, 2, 0.1))
                .DecideTurnAnytime(ctx, budget.Token, CancellationToken.None);
            Assert.That(d.IsPass, Is.False);
            bool legal = false;
            foreach (var m in ctx.LegalMoves)
                if (m.Rank == d.Move!.Rank && m.Type == d.Move!.Type && m.Length == d.Move!.Length) legal = true;
            Assert.That(legal, Is.True, "예산 만료 시에도 합법 best-so-far");
        }

        [Test]
        public void DecideTurnAnytime_abort_cancelled_throws_OCE()
        {
            var s = FreshPlayState(0);
            var ctx = GameFlowHelpers.Context(s, 0);
            using var abort = new CancellationTokenSource();
            abort.Cancel();
            var agent = new PimcAgent(7UL, 0, new PolicyConfig(8, 2, 0.1));
            Assert.Throws<System.OperationCanceledException>(() =>
                agent.DecideTurnAnytime(ctx, CancellationToken.None, abort.Token));
        }
```

- [ ] **Step 2: 실패 확인** — `DecideTurnAnytime` 미정의 컴파일 에러. ImportAsset(테스트)→`read_console`.

- [ ] **Step 3: 구현 — `PimcAgent.cs` 수정**

(a) 파일 상단 usings에 추가: `using System.Threading;`

(b) `DecideTurn` 메서드를 다음으로 치환(기존 body를 DecideTurnAnytime로 이동하고 토큰 점검 삽입):

```csharp
        public TurnDecision DecideTurn(in DecisionContext ctx)
            => DecideTurnAnytime(ctx, CancellationToken.None, CancellationToken.None);

        /// <summary>
        /// anytime 탐색. budget 만료 시(최소 1샘플 완료 후) 현재까지 best-so-far 반환(throw 안 함);
        /// abort 취소 시 OperationCanceledException(폭탄 인터럽트 — 드라이버가 이 턴 폐기).
        /// 토큰 둘 다 None 이면 고정노드수 결정적 탐색(= DecideTurn).
        /// </summary>
        public TurnDecision DecideTurnAnytime(in DecisionContext ctx, CancellationToken budget, CancellationToken abort)
        {
            var legal = ctx.LegalMoves;
            if (legal.Count == 0)
                return TurnDecision.Pass;

            if (_config.Worlds <= 0)
                return _policy.DecideTurn(ctx);

            ulong policyBase = _roundSeed ^ 0x5043_0000_0000_0001UL ^ (ulong)_seat;
            int rolloutsPerWorld = _config.RolloutsPerWorld < 1 ? 1 : _config.RolloutsPerWorld;

            var sumEv = new long[legal.Count];
            var rng = _rng;
            int samples = 0;
            bool budgetHit = false;

            for (int w = 0; w < _config.Worlds && !budgetHit; w++)
            {
                var world = Determinizer.Sample(ctx.State, _seat, ref rng);
                for (int r = 0; r < rolloutsPerWorld; r++)
                {
                    abort.ThrowIfCancellationRequested();                  // 폭탄 인터럽트 → 폐기
                    if (samples >= 1 && budget.IsCancellationRequested) { budgetHit = true; break; } // anytime
                    ulong rolloutSeed = policyBase + (ulong)(w * rolloutsPerWorld + r);
                    for (int i = 0; i < legal.Count; i++)
                    {
                        var sim = world.Clone();
                        if (!GameEngine.Apply(sim, GameAction.Play(_seat, legal[i].Cards)).Ok) continue;
                        sumEv[i] += Pimc.Rollout(sim, _seat, rolloutSeed, _config.Epsilon);
                    }
                    samples++;
                }
            }

            long bestSum = long.MinValue;
            int bestStrength = int.MaxValue;
            Combination? best = null;
            for (int i = 0; i < legal.Count; i++)
            {
                int strength = MoveOrder.Strength(legal[i]);
                if (sumEv[i] > bestSum || (sumEv[i] == bestSum && strength < bestStrength))
                {
                    bestSum = sumEv[i];
                    bestStrength = strength;
                    best = legal[i];
                }
            }

            // 패스 평가는 예산 미만료일 때만(만료 시 즉시 반환으로 anytime 존중).
            if (ctx.CanPass && !budgetHit)
            {
                var passWorld = Determinizer.Sample(ctx.State, _seat, ref rng);
                var passSim = passWorld.Clone();
                if (GameEngine.Apply(passSim, GameAction.Pass(_seat)).Ok)
                {
                    long passEv = (long)Pimc.Rollout(passSim, _seat, policyBase, _config.Epsilon)
                                  * _config.Worlds * rolloutsPerWorld;
                    if (passEv > bestSum) { _rng = rng; return TurnDecision.Pass; }
                }
            }

            _rng = rng;
            return best == null ? TurnDecision.Pass : TurnDecision.Play(best);
        }
```

(즉, P2-B1의 DecideTurn 본문을 DecideTurnAnytime로 옮기고, world/rollout 루프에 `abort.ThrowIfCancellationRequested()` + `samples>=1 && budget` 점검 + `samples` 카운트 + 패스 `!budgetHit` 가드만 추가.)

- [ ] **Step 4: 임포트 + 컴파일 + 통과** — PimcAgent.cs + PimcAgentTests.cs ImportAsset+Refresh → 컴파일 0 → `run_tests(test_names=["Tichu.Core.Tests.PimcAgentTests"])`. Expected: 12 PASS(기존 9 + 신규 3). 기존 9가 여전히 그린(= DecideTurn 회귀).

- [ ] **Step 5: 커밋**

```bash
git add "Assets/_Project/GameFlow/Agents/PimcAgent.cs" "Assets/_Project/Tests/EditMode/PimcAgentTests.cs"
git commit -m "feat(p2b2): PimcAgent.DecideTurnAnytime — 토큰 기반 anytime 동기 코어"
```

---

### Task 2: PimcDecisionAgent (비동기 래퍼)

**Files:**
- Create: `Assets/_Project/Presentation/Agents/PimcDecisionAgent.cs`
- Test: `Assets/_Project/Presentation/Tests/PimcDecisionAgentTests.cs`

**Interfaces:**
- Consumes: `PimcAgent(ulong,int,PolicyConfig)` + `DecideTurnAnytime`/`DecideTurn`/비-턴 메서드; `PolicyConfig`; UniTask.
- Produces: `public sealed class PimcDecisionAgent : IDecisionAgent`, ctor `(ulong roundSeed, int seat, PolicyConfig config, int budgetMs, int delayMs, System.Func<bool> fastForward = null)`. DecideTurnAsync=delay+Clone+RunOnThreadPool(anytime, 예산 CTS); 비-턴=즉시 위임.

- [ ] **Step 1: 실패 테스트 작성**

`Assets/_Project/Presentation/Tests/PimcDecisionAgentTests.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Tichu.Core.Game;
using Tichu.GameFlow.Agents;
using Tichu.Presentation;

namespace Tichu.Presentation.Tests
{
    [TestFixture]
    public class PimcDecisionAgentTests
    {
        private const ulong Seed = 77UL;

        private static DecisionContext GrandCtx(int seat)
            => new DecisionContext(GameEngine.NewRound(Seed), seat);

        // 56장 4×14 닫힌 Play 상태(seat 턴).
        private static DecisionContext PlayCtx(int seat)
        {
            var deck = Tichu.Core.Cards.Deck.CreateStandard();
            var hands = new System.Collections.Generic.IReadOnlyList<Tichu.Core.Cards.Card>[4];
            for (int i = 0; i < 4; i++) hands[i] = deck.GetRange(i * 14, 14);
            var s = new GameState { Phase = RoundPhase.Play, Turn = seat, CurrentTrick = null };
            for (int i = 0; i < 4; i++)
            {
                s.Seats[i] = new PlayerSeat { SeatIndex = i };
                s.Seats[i].Hand.AddRange(hands[i]);
            }
            return new DecisionContext(s, seat);
        }

        private static PimcDecisionAgent Agent(int seat, PolicyConfig cfg, int budgetMs = 5000)
            => new PimcDecisionAgent(Seed, seat, cfg, budgetMs, delayMs: 0, fastForward: () => true);

        // ── 비-턴 위임(즉시 완료) ───────────────────────────────────────────────────

        [Test]
        public void CallGrandTichuAsync_matches_PimcAgent_and_completes_sync()
        {
            var ctx = GrandCtx(2);
            bool expected = new PimcAgent(Seed, 2, PolicyConfig.Normal).CallGrandTichu(ctx);
            var task = Agent(2, PolicyConfig.Normal).CallGrandTichuAsync(ctx, default);
            Assert.That(task.GetAwaiter().IsCompleted, Is.True, "비-턴은 즉시 완료");
            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(expected));
        }

        [Test]
        public void ChooseDragonRecipientAsync_matches_PimcAgent()
        {
            var ctx = PlayCtx(0);
            int expected = new PimcAgent(Seed, 0, PolicyConfig.Normal).ChooseDragonRecipient(ctx);
            var task = Agent(0, PolicyConfig.Normal).ChooseDragonRecipientAsync(ctx, default);
            Assert.That(task.GetAwaiter().IsCompleted, Is.True);
            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(expected));
        }

        [Test]
        public void Implements_IDecisionAgent()
        {
            IDecisionAgent a = Agent(0, PolicyConfig.Normal);
            Assert.That(a, Is.Not.Null);
        }

        // ── 스레드 DecideTurnAsync ──────────────────────────────────────────────────
        // ⚠️ async Task + AsTask. delay 스킵(fastForward=true). budget 큼 → 완전 탐색.
        // hang/stuck 발생 시 이 두 테스트 제거(동기 코어 Task1 커버 + 수동 인게임 검증).

        [Test, Timeout(60000)]
        public async Task DecideTurnAsync_returns_a_legal_move()
        {
            var ctx = PlayCtx(0);
            var d = await Agent(0, new PolicyConfig(2, 1, 0.1)).DecideTurnAsync(ctx, default).AsTask();
            Assert.That(d.IsPass, Is.False);
            bool legal = false;
            foreach (var m in ctx.LegalMoves)
                if (m.Rank == d.Move!.Rank && m.Type == d.Move!.Type && m.Length == d.Move!.Length) legal = true;
            Assert.That(legal, Is.True);
        }

        [Test, Timeout(60000)]
        public void DecideTurnAsync_aborted_throws_OCE()
        {
            var ctx = PlayCtx(0);
            using var abort = new CancellationTokenSource();
            abort.Cancel();
            Assert.ThrowsAsync<System.OperationCanceledException>(async () =>
                await Agent(0, new PolicyConfig(2, 1, 0.1)).DecideTurnAsync(ctx, abort.Token).AsTask());
        }
    }
}
```

- [ ] **Step 2: 실패 확인** — `PimcDecisionAgent` 미정의. ImportAsset(테스트)→`read_console`.

- [ ] **Step 3: 구현 작성**

`Assets/_Project/Presentation/Agents/PimcDecisionAgent.cs`:

```csharp
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Tichu.Core.Combinations;
using Tichu.GameFlow.Agents;

namespace Tichu.Presentation
{
    /// <summary>
    /// 동기 다세계 <see cref="PimcAgent"/>를 <see cref="IDecisionAgent"/>로 감싸는 앱 어댑터.
    /// DecideTurn 만 백그라운드(UniTask.RunOnThreadPool)에서 anytime 탐색하고(메인스레드 비차단),
    /// 예산 CTS(CancelAfter budgetMs) 만료 → best-so-far, 외부 ct(폭탄인터럽트) → OCE 전파.
    /// 셋업·폭탄·용양도 등 나머지 결정은 즉시 위임(휴리스틱). 사람 관전 페이싱용 delay/fastForward 포함.
    /// </summary>
    public sealed class PimcDecisionAgent : IDecisionAgent
    {
        private readonly PimcAgent _pimc;
        private readonly PolicyConfig _config;
        private readonly int _budgetMs;
        private readonly int _delayMs;
        private readonly Func<bool> _fastForward;

        public PimcDecisionAgent(ulong roundSeed, int seat, PolicyConfig config,
            int budgetMs, int delayMs, Func<bool> fastForward = null)
        {
            _pimc = new PimcAgent(roundSeed, seat, config);
            _config = config;
            _budgetMs = budgetMs;
            _delayMs = delayMs;
            _fastForward = fastForward;
        }

        private UniTask DelayAsync(CancellationToken ct)
            => _fastForward != null && _fastForward() ? UniTask.CompletedTask : UniTask.Delay(_delayMs, cancellationToken: ct);

        public UniTask<bool> CallGrandTichuAsync(DecisionContext ctx, CancellationToken ct)
            => UniTask.FromResult(_pimc.CallGrandTichu(ctx));

        public UniTask<ExchangeChoice> ChooseExchangeAsync(DecisionContext ctx, CancellationToken ct)
            => UniTask.FromResult(_pimc.ChooseExchange(ctx));

        public UniTask<bool> CallTichuAsync(DecisionContext ctx, CancellationToken ct)
            => UniTask.FromResult(_pimc.CallTichu(ctx));

        public async UniTask<TurnDecision> DecideTurnAsync(DecisionContext ctx, CancellationToken ct)
        {
            await DelayAsync(ct);

            // Easy(탐색 OFF): 즉시 휴리스틱(스레드풀 우회).
            if (_config.Worlds <= 0)
                return _pimc.DecideTurn(ctx);

            var snap = ctx.State.Clone();   // 가변 공유 차단(백그라운드 진입 전).
            int seat = ctx.Seat;
            using var budgetCts = new CancellationTokenSource(_budgetMs);   // anytime 예산.
            var budget = budgetCts.Token;
            return await UniTask.RunOnThreadPool(
                () => _pimc.DecideTurnAnytime(new DecisionContext(snap, seat), budget, ct),
                cancellationToken: ct);
        }

        public async UniTask<Combination?> DecideBombAsync(DecisionContext ctx, CancellationToken ct)
        {
            var bomb = _pimc.DecideBomb(ctx);     // P2-B2: 휴리스틱(즉시).
            if (bomb != null) await DelayAsync(ct);
            return bomb;
        }

        public UniTask<int> ChooseDragonRecipientAsync(DecisionContext ctx, CancellationToken ct)
            => UniTask.FromResult(_pimc.ChooseDragonRecipient(ctx));
    }
}
```

- [ ] **Step 4: 임포트 + 컴파일 + 통과** — PimcDecisionAgent.cs + 테스트 ImportAsset+Refresh → 컴파일 0 → `run_tests(test_names=["Tichu.Presentation.Tests.PimcDecisionAgentTests"])`. Expected: 5 PASS. **스레드 테스트 2개 hang/stuck_suspected 감시**(`get_test_job` 모니터). hang 시: 두 스레드 테스트 제거 후 재실행(나머지 3 PASS), 동기 코어(Task1)+수동 인게임으로 대체하고 그 사실을 커밋 메시지·메모리에 기록.

- [ ] **Step 5: 커밋**

```bash
git add "Assets/_Project/Presentation/Agents/PimcDecisionAgent.cs" "Assets/_Project/Presentation/Agents/PimcDecisionAgent.cs.meta" "Assets/_Project/Presentation/Tests/PimcDecisionAgentTests.cs" "Assets/_Project/Presentation/Tests/PimcDecisionAgentTests.cs.meta"
git commit -m "feat(p2b2): PimcDecisionAgent — 백그라운드 anytime 탐색 비동기 래퍼"
```

---

### Task 3: GameLaunchArgs.Difficulty + RoundBootstrap 난이도 배선

**Files:**
- Modify: `Assets/_Project/Presentation/Shell/GameLaunchArgs.cs`
- Modify: `Assets/_Project/Presentation/RoundBootstrap.cs`

**Interfaces:**
- Consumes: `PimcDecisionAgent`; `PolicyConfig.For(Difficulty)`; `GameLaunchArgs.Difficulty`.
- Produces: `GameLaunchArgs.Difficulty` 필드(기본 Normal). `RoundBootstrap`이 AI 좌석 1~3을 `PimcDecisionAgent`로 생성(난이도→PolicyConfig + budgetMs 매핑).

- [ ] **Step 1: GameLaunchArgs 필드 추가** (`GameLaunchArgs.cs`)

`MySeat` 필드 아래에 추가:
```csharp

        /// <summary>AI 난이도(기본 Normal). 메뉴 셀렉터는 후속 — 현재 기본값 사용.</summary>
        public Tichu.GameFlow.Agents.Difficulty Difficulty = Tichu.GameFlow.Agents.Difficulty.Normal;
```

- [ ] **Step 2: RoundBootstrap 배선** (`RoundBootstrap.cs`)

(a) `RunMatchAsync`의 AI 좌석 생성부(기존 `DelayedAiDecisionAgent` 3개)를 PimcDecisionAgent로 치환:

기존:
```csharp
                    var agents = new IDecisionAgent[]
                    {
                        human,
                        new DelayedAiDecisionAgent(seed, 1, _args.AiDelayMs, () => vm.FastForward),
                        new DelayedAiDecisionAgent(seed, 2, _args.AiDelayMs, () => vm.FastForward),
                        new DelayedAiDecisionAgent(seed, 3, _args.AiDelayMs, () => vm.FastForward),
                    };
```
→
```csharp
                    var cfg = Tichu.GameFlow.Agents.PolicyConfig.For(_args.Difficulty);
                    int budgetMs = BudgetMsFor(_args.Difficulty);
                    var agents = new IDecisionAgent[]
                    {
                        human,
                        new PimcDecisionAgent(seed, 1, cfg, budgetMs, _args.AiDelayMs, () => vm.FastForward),
                        new PimcDecisionAgent(seed, 2, cfg, budgetMs, _args.AiDelayMs, () => vm.FastForward),
                        new PimcDecisionAgent(seed, 3, cfg, budgetMs, _args.AiDelayMs, () => vm.FastForward),
                    };
```

(b) 클래스에 난이도→budgetMs 매핑 헬퍼 추가(CreateCanvas 위 등 적당한 위치):
```csharp
        /// <summary>난이도별 인게임 1수 시간예산(ms). 스펙 §5 시작값.</summary>
        private static int BudgetMsFor(Tichu.GameFlow.Agents.Difficulty d)
        {
            switch (d)
            {
                case Tichu.GameFlow.Agents.Difficulty.Easy:   return 0;    // 탐색 OFF
                case Tichu.GameFlow.Agents.Difficulty.Normal: return 80;
                case Tichu.GameFlow.Agents.Difficulty.Hard:   return 250;
                case Tichu.GameFlow.Agents.Difficulty.Expert: return 300;
                default:                                       return 80;
            }
        }
```

(c) `DelayedAiDecisionAgent` 가 더 이상 RoundBootstrap에서 안 쓰이면 그대로 둔다(다른 사용처/오라클 테스트에서 쓰일 수 있으므로 삭제 금지 — 사전 dead code 제거는 범위 밖, 발견 시 언급만).

- [ ] **Step 3: 임포트 + 컴파일** — GameLaunchArgs.cs + RoundBootstrap.cs ImportAsset+Refresh → `isCompiling` False → `read_console` 0(에러 없음). (RoundBootstrap은 MonoBehaviour 컴포지션 루트라 단위테스트 없음 — 컴파일 + 수동 인게임으로 검증.)

- [ ] **Step 4: 회귀** — `run_tests(test_names=["Tichu.Presentation.Tests.AsyncGameDriverTests","Tichu.Presentation.Tests.AiDecisionAgentTests","Tichu.Core.Tests.PimcAgentTests"])` → 전부 그린(오라클 불침범·코어 회귀 확인).

- [ ] **Step 5: 커밋**

```bash
git add "Assets/_Project/Presentation/Shell/GameLaunchArgs.cs" "Assets/_Project/Presentation/RoundBootstrap.cs"
git commit -m "feat(p2b2): RoundBootstrap 난이도 배선 — AI 좌석=PimcDecisionAgent(난이도→PolicyConfig+budget)"
```

- [ ] **Step 6: 수동 인게임 검증(사용자)** — Table 씬 플레이 → 인간 1 + Normal PIMC 3으로 한 라운드 완주, AI 수가 자연스럽고 프레임 히칭 없는지(메인스레드 비차단) 육안 확인. (자동화 불가 — 사람 확인.)

---

## Self-Review

**1. Spec coverage (§11 P2-B 비동기분 + §6):**
- `PimcDecisionAgent`(Clone+RunOnThreadPool+anytime ≤80ms) → Task 2 ✓
- anytime(예산→best-so-far) + 예산↔폭탄 취소 구분 → Task 1(코어 토큰) + Task 2(별 토큰 배선) ✓
- `RoundBootstrap` 난이도 주입 + `GameLaunchArgs.Difficulty` → Task 3 ✓
- 검증: PIMC 결정성(코어 None-토큰=DecideTurn) Task1 ✓; anytime Task1/Task2 ✓; 오라클 불침범 Task3 회귀 ✓

**2. Placeholder scan:** 자리표시자 없음. 스레드 테스트는 hang 시 제거 절차를 명시(자리표시자 아님).

**3. Type consistency:** `DecideTurnAnytime(in DecisionContext, CancellationToken, CancellationToken)` — Task1 정의, Task2 소비 일치. `PimcDecisionAgent(ulong,int,PolicyConfig,int,int,Func<bool>)` — Task2 정의, Task3 소비 일치. `PolicyConfig.For(Difficulty)`/`GameLaunchArgs.Difficulty`/`PimcAgent` 비-턴 메서드 — 실제 소스 대조 완료.

**미해결/주의:**
- **스레드 EditMode 테스트 위험**(RunOnThreadPool + 메인복귀): null SyncContext(EditMode)에선 풀스레드 완료로 정상 기대하나, hang/stuck 시 제거하고 동기코어+수동으로 대체(Task2 Step4 명시).
- **DelayedAiDecisionAgent dead 가능성**: RoundBootstrap이 안 쓰게 되면 사용처 확인(오라클/다른 테스트). 미사용이어도 P2-B2에서 삭제 안 함(범위 밖, 언급만).
- **인게임 메인스레드 비차단/프레임**: 자동 검증 불가 → 사용자 수동(Task3 Step6).

---

## Execution Handoff

**Plan complete and saved to `docs/superpowers/plans/2026-06-24-tichu-p2b2-async-anytime.md`. 실행 방식:**
**1. Inline Execution(권장, 기존과 동일)** — executing-plans로 Task 1→3, UnityMCP 워밍 유지.
**2. Subagent-Driven** — 태스크별 subagent(단, UnityMCP 단일 인스턴스 경합 위험).
**Which approach?**
