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
