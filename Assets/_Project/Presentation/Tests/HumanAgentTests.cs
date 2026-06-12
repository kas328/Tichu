using System;
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
    /// HumanAgent 가 IHumanInputPort 에 결정을 위임하고,
    /// 취소 토큰이 전파되는지 검증한다.
    /// </summary>
    [TestFixture]
    public class HumanAgentTests
    {
        // ── FakeInputPort ─────────────────────────────────────────────────────

        /// <summary>
        /// 테스트용 IHumanInputPort.
        /// RequestTurnDecisionAsync 만 완료 제어를 노출한다(나머지는 즉시 완료).
        /// </summary>
        private class FakeInputPort : IHumanInputPort
        {
            // 현재 진행 중인 턴 결정 요청을 제어하는 소스.
            private UniTaskCompletionSource<TurnDecision>? _turnUtcs;

            /// <summary>대기 중인 RequestTurnDecisionAsync 를 d 로 완료시킨다.</summary>
            public void CompleteTurn(TurnDecision d) => _turnUtcs?.TrySetResult(d);

            // ── IHumanInputPort 구현 ──────────────────────────────────────────

            public UniTask<bool> RequestGrandTichuAsync(DecisionContext ctx, CancellationToken ct)
                => UniTask.FromResult(false);

            public UniTask<ExchangeChoice> RequestExchangeAsync(DecisionContext ctx, CancellationToken ct)
                => UniTask.FromResult(default(ExchangeChoice));

            public UniTask<bool> RequestTichuAsync(DecisionContext ctx, CancellationToken ct)
                => UniTask.FromResult(false);

            /// <summary>
            /// 새 UniTaskCompletionSource 를 생성하고, ct 취소 시 자동으로 취소되도록 등록한다.
            /// CompleteTurn() 또는 ct 취소로만 완료된다.
            /// </summary>
            public UniTask<TurnDecision> RequestTurnDecisionAsync(DecisionContext ctx, CancellationToken ct)
            {
                _turnUtcs = new UniTaskCompletionSource<TurnDecision>();
                // ct 취소 시 UniTask 도 취소로 전이.
                ct.Register(() => _turnUtcs.TrySetCanceled(ct));
                return _turnUtcs.Task;
            }

            public UniTask<Combination?> RequestBombAsync(DecisionContext ctx, CancellationToken ct)
                => UniTask.FromResult<Combination?>(null);

            public UniTask<int> RequestDragonRecipientAsync(DecisionContext ctx, CancellationToken ct)
                => UniTask.FromResult(0);
        }

        // ── 헬퍼 ──────────────────────────────────────────────────────────────

        private static DecisionContext MakeCtx()
        {
            var state = GameEngine.NewRound(seed: 42UL);
            return new DecisionContext(state, 0);
        }

        // ── 테스트 ────────────────────────────────────────────────────────────

        /// <summary>
        /// CompleteTurn() 호출 전까지 태스크가 완료되지 않고,
        /// 호출 후에는 지정한 결정값을 반환한다.
        /// </summary>
        [Test]
        public void Awaits_until_submitted()
        {
            var fake = new FakeInputPort();
            var human = new HumanAgent(fake);
            var ctx = MakeCtx();

            // 태스크를 한 번만 캡처한다(UniTask 는 두 번 await 불가).
            UniTask<TurnDecision> task = human.DecideTurnAsync(ctx, default);

            // CompleteTurn 전: 아직 완료되지 않아야 한다.
            Assert.That(task.Status, Is.Not.EqualTo(UniTaskStatus.Succeeded),
                "CompleteTurn() 호출 전에는 태스크가 완료되어서는 안 된다.");

            // 결정을 전달해 태스크를 완료시킨다.
            var expected = TurnDecision.Pass;
            fake.CompleteTurn(expected);

            // 이제 동기적으로 결과를 얻을 수 있어야 한다.
            TurnDecision actual = task.GetAwaiter().GetResult();
            Assert.That(actual, Is.EqualTo(expected),
                "CompleteTurn() 으로 전달한 결정이 그대로 반환되어야 한다.");
        }

        /// <summary>
        /// 대기 중에 CancellationToken 을 취소하면 OperationCanceledException 이 전파된다.
        /// </summary>
        [Test]
        public void Cancellation_propagates()
        {
            var fake = new FakeInputPort();
            var human = new HumanAgent(fake);
            var ctx = MakeCtx();

            using var cts = new CancellationTokenSource();

            UniTask<TurnDecision> task = human.DecideTurnAsync(ctx, cts.Token);

            // 취소 전: 아직 완료되지 않아야 한다.
            Assert.That(task.Status, Is.Not.EqualTo(UniTaskStatus.Succeeded));

            // 취소 신호 발송.
            cts.Cancel();

            // 태스크가 취소로 전이했는지 확인.
            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Canceled),
                "ct 취소 후 태스크는 Canceled 상태여야 한다.");

            // GetAwaiter().GetResult() 는 OperationCanceledException 을 던져야 한다.
            Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult(),
                "취소된 태스크를 await 하면 OperationCanceledException 이 발생해야 한다.");
        }

        /// <summary>HumanAgent 는 IDecisionAgent 를 구현한다(컴파일 타임 검증).</summary>
        [Test]
        public void HumanAgent_ImplementsIDecisionAgent()
        {
            IDecisionAgent agent = new HumanAgent(new FakeInputPort());
            Assert.That(agent, Is.Not.Null);
        }
    }
}
