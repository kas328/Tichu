using System;
using System.Diagnostics;
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
        // async Task + AsTask. delay 스킵(fastForward=true). budget 큼 → 완전 탐색.

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

        // 겹침 검증: 주입 delay(Task.Delay 기반 — EditMode 스레드풀 타이머로 완료)와 탐색이
        // 동시에 진행되면 per-move ≈ max(delay, compute). 순차면 delay+compute.
        // 연산을 작은 예산(~1세계)으로 바운드하고 딜레이를 지배적으로 둬, compute-only(c)
        // 대비 delay를 끼운 f < delay + 0.5*c 이면 겹친 것(순차면 f≈delay+c).
        [Test, Timeout(60000)]
        public async Task DecideTurnAsync_overlaps_delay_with_compute()
        {
            const int delayMs = 800;
            const int budgetMs = 250;   // 연산을 ~1세계로 바운드(딜레이가 지배)
            var cfg = new PolicyConfig(8, 4, 0.10);
            Func<CancellationToken, UniTask> immediate = _ => UniTask.CompletedTask;
            Func<CancellationToken, UniTask> delayed = ct => Task.Delay(delayMs, ct).AsUniTask();

            // compute-only 기준 c (즉시 딜레이)
            var swc = Stopwatch.StartNew();
            await new PimcDecisionAgent(Seed, 0, cfg, budgetMs, 0, null, immediate)
                .DecideTurnAsync(PlayCtx(0), default).AsTask();
            swc.Stop();
            long c = swc.ElapsedMilliseconds;

            // delay 끼운 f — 겹치면 f≈max(delay,c), 순차면 f≈delay+c
            var swf = Stopwatch.StartNew();
            await new PimcDecisionAgent(Seed, 0, cfg, budgetMs, 0, null, delayed)
                .DecideTurnAsync(PlayCtx(0), default).AsTask();
            swf.Stop();
            long f = swf.ElapsedMilliseconds;

            Assert.That(f, Is.LessThan(delayMs + 0.5 * c),
                $"겹침이면 f≈max({delayMs},c). 순차면 f≈{delayMs}+c. (c={c}ms, f={f}ms)");
        }

        [Test, Timeout(60000)]
        public void DecideTurnAsync_aborted_throws_OCE()
        {
            var ctx = PlayCtx(0);
            using var abort = new CancellationTokenSource();
            abort.Cancel();
            // 취소는 OCE 파생(TaskCanceledException 등)으로 던져진다 — 드라이버의 catch(OperationCanceledException)가
            // 잡는 계약. CatchAsync 는 파생까지 허용(ThrowsAsync 는 정확한 타입만).
            Assert.CatchAsync<System.OperationCanceledException>(async () =>
                await Agent(0, new PolicyConfig(2, 1, 0.1)).DecideTurnAsync(ctx, abort.Token).AsTask());
        }
    }
}
