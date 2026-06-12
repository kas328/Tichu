using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Tichu.Core.Game;
using Tichu.GameFlow.Agents;
using Tichu.Presentation;

namespace Tichu.Presentation.Tests
{
    /// <summary>
    /// AiDecisionAgent 가 동기 AiAgent 와 동일한 결정을 반환하며,
    /// 각 UniTask 가 즉시 완료(동기 완료)됨을 검증한다.
    /// </summary>
    [TestFixture]
    public class AiDecisionAgentTests
    {
        private const ulong Seed = 99UL;
        private const int Seat = 2;

        // ── 헬퍼 ──────────────────────────────────────────────────────────────

        /// <summary>GrandTichuDecision 단계의 유효한 컨텍스트를 생성한다.</summary>
        private static DecisionContext MakeCtx()
        {
            // GameEngine.NewRound 는 8장 딜 직후 GrandTichuDecision 상태를 돌려준다.
            var state = GameEngine.NewRound(seed: Seed);
            return new DecisionContext(state, Seat);
        }

        // ── CallGrandTichuAsync ────────────────────────────────────────────────

        [Test]
        public void CallGrandTichuAsync_MatchesSyncAiAgent()
        {
            var ctx = MakeCtx();
            bool expected = new AiAgent(Seed, Seat).CallGrandTichu(ctx);

            var task = new AiDecisionAgent(Seed, Seat).CallGrandTichuAsync(ctx, default);

            // 즉시 완료 검증
            Assert.That(task.GetAwaiter().IsCompleted, Is.True,
                "UniTask 는 즉시 완료여야 한다.");

            bool actual = task.GetAwaiter().GetResult();
            Assert.That(actual, Is.EqualTo(expected));
        }

        // ── ChooseDragonRecipientAsync ─────────────────────────────────────────

        [Test]
        public void ChooseDragonRecipientAsync_MatchesSyncAiAgent()
        {
            var ctx = MakeCtx();
            int expected = new AiAgent(Seed, Seat).ChooseDragonRecipient(ctx);

            var task = new AiDecisionAgent(Seed, Seat).ChooseDragonRecipientAsync(ctx, default);

            Assert.That(task.GetAwaiter().IsCompleted, Is.True,
                "UniTask 는 즉시 완료여야 한다.");

            int actual = task.GetAwaiter().GetResult();
            Assert.That(actual, Is.EqualTo(expected));
        }

        // ── CallTichuAsync ────────────────────────────────────────────────────

        [Test]
        public void CallTichuAsync_MatchesSyncAiAgent()
        {
            var ctx = MakeCtx();
            bool expected = new AiAgent(Seed, Seat).CallTichu(ctx);

            var task = new AiDecisionAgent(Seed, Seat).CallTichuAsync(ctx, default);

            Assert.That(task.GetAwaiter().IsCompleted, Is.True,
                "UniTask 는 즉시 완료여야 한다.");

            bool actual = task.GetAwaiter().GetResult();
            Assert.That(actual, Is.EqualTo(expected));
        }

        // ── IDecisionAgent 타입 준수 ──────────────────────────────────────────

        [Test]
        public void AiDecisionAgent_ImplementsIDecisionAgent()
        {
            // 컴파일 타임 타입 검증 — 인터페이스 할당 가능 여부.
            IDecisionAgent agent = new AiDecisionAgent(Seed, Seat);
            Assert.That(agent, Is.Not.Null);
        }
    }
}
