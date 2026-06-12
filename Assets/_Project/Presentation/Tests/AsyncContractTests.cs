using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Tichu.Core.Combinations;
using Tichu.Core.Game;
using Tichu.GameFlow.Agents;
using Tichu.Presentation;

namespace Tichu.Presentation.Tests
{
    /// <summary>
    /// IDecisionAgent / IHumanInputPort のシグニチャが GameFlow 型と整合し、
    /// コンパイル・呼び出しできることを確認する契約テスト。
    /// </summary>
    [TestFixture]
    public class AsyncContractTests
    {
        // ── dummy implementations ──────────────────────────────────────────

        private class DummyDecisionAgent : IDecisionAgent
        {
            public UniTask<bool> CallGrandTichuAsync(DecisionContext ctx, CancellationToken ct)
                => UniTask.FromResult(false);

            public UniTask<ExchangeChoice> ChooseExchangeAsync(DecisionContext ctx, CancellationToken ct)
                => UniTask.FromResult(default(ExchangeChoice));

            public UniTask<bool> CallTichuAsync(DecisionContext ctx, CancellationToken ct)
                => UniTask.FromResult(false);

            public UniTask<TurnDecision> DecideTurnAsync(DecisionContext ctx, CancellationToken ct)
                => UniTask.FromResult(TurnDecision.Pass);

            public UniTask<Combination?> DecideBombAsync(DecisionContext ctx, CancellationToken ct)
                => UniTask.FromResult<Combination?>(null);

            public UniTask<int> ChooseDragonRecipientAsync(DecisionContext ctx, CancellationToken ct)
                => UniTask.FromResult(0);
        }

        private class DummyHumanInputPort : IHumanInputPort
        {
            public UniTask<bool> RequestGrandTichuAsync(DecisionContext ctx, CancellationToken ct)
                => UniTask.FromResult(false);

            public UniTask<ExchangeChoice> RequestExchangeAsync(DecisionContext ctx, CancellationToken ct)
                => UniTask.FromResult(default(ExchangeChoice));

            public UniTask<bool> RequestTichuAsync(DecisionContext ctx, CancellationToken ct)
                => UniTask.FromResult(false);

            public UniTask<TurnDecision> RequestTurnDecisionAsync(DecisionContext ctx, CancellationToken ct)
                => UniTask.FromResult(TurnDecision.Pass);

            public UniTask<Combination?> RequestBombAsync(DecisionContext ctx, CancellationToken ct)
                => UniTask.FromResult<Combination?>(null);

            public UniTask<int> RequestDragonRecipientAsync(DecisionContext ctx, CancellationToken ct)
                => UniTask.FromResult(0);
        }

        // ── helpers ────────────────────────────────────────────────────────

        private static DecisionContext MakeContext()
        {
            // GameEngine.NewRound produces a fully valid GrandTichuDecision state.
            var state = GameEngine.NewRound(seed: 42UL);
            return new DecisionContext(state, 0);
        }

        // ── IDecisionAgent tests ───────────────────────────────────────────

        [Test]
        public void DecisionAgent_CallGrandTichu_ReturnsBool()
        {
            IDecisionAgent agent = new DummyDecisionAgent();
            var ctx = MakeContext();
            bool result = agent.CallGrandTichuAsync(ctx, default).GetAwaiter().GetResult();
            Assert.That(result, Is.False);
        }

        [Test]
        public void DecisionAgent_ChooseExchange_ReturnsExchangeChoice()
        {
            IDecisionAgent agent = new DummyDecisionAgent();
            var ctx = MakeContext();
            ExchangeChoice result = agent.ChooseExchangeAsync(ctx, default).GetAwaiter().GetResult();
            Assert.That(result, Is.EqualTo(default(ExchangeChoice)));
        }

        [Test]
        public void DecisionAgent_CallTichu_ReturnsBool()
        {
            IDecisionAgent agent = new DummyDecisionAgent();
            var ctx = MakeContext();
            bool result = agent.CallTichuAsync(ctx, default).GetAwaiter().GetResult();
            Assert.That(result, Is.False);
        }

        [Test]
        public void DecisionAgent_DecideTurn_ReturnsTurnDecision()
        {
            IDecisionAgent agent = new DummyDecisionAgent();
            var ctx = MakeContext();
            TurnDecision result = agent.DecideTurnAsync(ctx, default).GetAwaiter().GetResult();
            Assert.That(result.IsPass, Is.True);
        }

        [Test]
        public void DecisionAgent_DecideBomb_ReturnsNullableCombination()
        {
            IDecisionAgent agent = new DummyDecisionAgent();
            var ctx = MakeContext();
            Combination? result = agent.DecideBombAsync(ctx, default).GetAwaiter().GetResult();
            Assert.That(result, Is.Null);
        }

        [Test]
        public void DecisionAgent_ChooseDragonRecipient_ReturnsInt()
        {
            IDecisionAgent agent = new DummyDecisionAgent();
            var ctx = MakeContext();
            int result = agent.ChooseDragonRecipientAsync(ctx, default).GetAwaiter().GetResult();
            Assert.That(result, Is.EqualTo(0));
        }

        // ── IHumanInputPort tests ──────────────────────────────────────────

        [Test]
        public void HumanInputPort_RequestGrandTichu_ReturnsBool()
        {
            IHumanInputPort port = new DummyHumanInputPort();
            var ctx = MakeContext();
            bool result = port.RequestGrandTichuAsync(ctx, default).GetAwaiter().GetResult();
            Assert.That(result, Is.False);
        }

        [Test]
        public void HumanInputPort_RequestExchange_ReturnsExchangeChoice()
        {
            IHumanInputPort port = new DummyHumanInputPort();
            var ctx = MakeContext();
            ExchangeChoice result = port.RequestExchangeAsync(ctx, default).GetAwaiter().GetResult();
            Assert.That(result, Is.EqualTo(default(ExchangeChoice)));
        }

        [Test]
        public void HumanInputPort_RequestTichu_ReturnsBool()
        {
            IHumanInputPort port = new DummyHumanInputPort();
            var ctx = MakeContext();
            bool result = port.RequestTichuAsync(ctx, default).GetAwaiter().GetResult();
            Assert.That(result, Is.False);
        }

        [Test]
        public void HumanInputPort_RequestTurnDecision_ReturnsTurnDecision()
        {
            IHumanInputPort port = new DummyHumanInputPort();
            var ctx = MakeContext();
            TurnDecision result = port.RequestTurnDecisionAsync(ctx, default).GetAwaiter().GetResult();
            Assert.That(result.IsPass, Is.True);
        }

        [Test]
        public void HumanInputPort_RequestBomb_ReturnsNullableCombination()
        {
            IHumanInputPort port = new DummyHumanInputPort();
            var ctx = MakeContext();
            Combination? result = port.RequestBombAsync(ctx, default).GetAwaiter().GetResult();
            Assert.That(result, Is.Null);
        }

        [Test]
        public void HumanInputPort_RequestDragonRecipient_ReturnsInt()
        {
            IHumanInputPort port = new DummyHumanInputPort();
            var ctx = MakeContext();
            int result = port.RequestDragonRecipientAsync(ctx, default).GetAwaiter().GetResult();
            Assert.That(result, Is.EqualTo(0));
        }
    }
}
