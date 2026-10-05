using System.Diagnostics;
using NUnit.Framework;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;

namespace Tichu.Presentation.Tests
{
    /// <summary>
    /// 동기 페이싱 어댑터. 두 가지를 지킨다.
    ///  · 지연 배치 — 턴 결정만 느리고 셋업 결정은 즉시(트릭당 3배 지연 방지).
    ///  · 겹침     — per-move ≈ max(지연, 연산). 잠들고 계산하면 합이 된다.
    /// </summary>
    [TestFixture]
    public class PacedPimcAgentTests
    {
        private const int Delay = 300;

        private static DecisionContext Ctx(GameState s, int seat) => new DecisionContext(s, seat);

        /// <summary>탐색 OFF(Easy) 설정 — 연산이 0 에 가까워 지연만 측정된다.</summary>
        private static PolicyConfig NoSearch => new PolicyConfig(0, 0, 0.0);

        private static long Millis(System.Action a)
        {
            var sw = Stopwatch.StartNew();
            a();
            return sw.ElapsedMilliseconds;
        }

        [Test]
        public void Setup_decisions_are_not_delayed()
        {
            var s = GameEngine.NewRound(31337UL);
            var agent = new PacedPimcAgent(31337UL, 1, NoSearch, budgetMs: 0, delayMs: Delay);

            Assert.That(Millis(() => agent.CallGrandTichu(Ctx(s, 1))), Is.LessThan(Delay / 2),
                "큰 티츄는 즉시 — 지연을 공용 헬퍼에 넣으면 여기서 걸린다");
            Assert.That(Millis(() => agent.ChooseExchange(Ctx(s, 1))), Is.LessThan(Delay / 2),
                "교환은 즉시");
            Assert.That(Millis(() => agent.CallTichu(Ctx(s, 1))), Is.LessThan(Delay / 2),
                "작은 티츄는 즉시");
            Assert.That(Millis(() => agent.ChooseDragonRecipient(Ctx(s, 1))), Is.LessThan(Delay / 2),
                "용 양도는 즉시");
        }

        [Test]
        public void Declined_bomb_is_not_delayed()
        {
            // 폭탄 창은 매 트릭 세 좌석을 폴링한다. 안 던질 때 지연이 걸리면 트릭마다 3배가 된다.
            var s = GameEngine.NewRound(31337UL);
            var agent = new PacedPimcAgent(31337UL, 1, NoSearch, budgetMs: 0, delayMs: Delay);

            long ms = Millis(() => agent.DecideBomb(Ctx(s, 1)));
            Assert.That(ms, Is.LessThan(Delay / 2), "폭탄을 던지지 않으면 즉시 반환");
        }

        [Test]
        public void Turn_decision_is_delayed()
        {
            var s = PlayPhaseState(31337UL);
            var agent = new PacedPimcAgent(31337UL, s.Turn, NoSearch, budgetMs: 0, delayMs: Delay);

            long ms = Millis(() => agent.DecideTurn(Ctx(s, s.Turn)));
            Assert.That(ms, Is.GreaterThanOrEqualTo(Delay - 50), "턴 결정엔 관전 지연이 걸린다");
        }

        [Test]
        public void Search_overlaps_the_delay()
        {
            var s = PlayPhaseState(31337UL);
            int seat = s.Turn;
            // 세계 수를 크게 둬 예산이 반드시 구속되게 만든다 → 연산 ≈ budget.
            var heavy = new PolicyConfig(256, 4, 0.05);
            const int Budget = 500;

            long computeOnly = Millis(() =>
                new PacedPimcAgent(31337UL, seat, heavy, Budget, delayMs: 0).DecideTurn(Ctx(s, seat)));

            // 전제 확인: 연산이 실제로 무겁지 않으면 이 테스트는 판별력이 없다.
            Assume.That(computeOnly, Is.GreaterThan(Budget / 2),
                $"예산이 구속되지 않았다(연산 {computeOnly}ms) — 겹침을 측정할 수 없다");

            long paced = Millis(() =>
                new PacedPimcAgent(31337UL, seat, heavy, Budget, delayMs: Budget).DecideTurn(Ctx(s, seat)));

            // 겹침: ≈ max(지연, 연산) ≈ Budget.  순차: ≈ 지연 + 연산 ≈ 2×Budget.
            Assert.That(paced, Is.LessThan(Budget + computeOnly / 2),
                $"연산이 지연과 겹쳐야 한다(연산 {computeOnly}ms, 페이싱 {paced}ms)");
        }

        /// <summary>
        /// 카드가 깔린 Play 페이즈 상태를 하나 잡아 온다. GameDriver 는 한 단계씩 미는
        /// 공개 진입점이 없으므로(RunRound 뿐), onApply 훅에서 첫 Play 상태를 복제한다.
        /// </summary>
        private static GameState PlayPhaseState(ulong seed)
        {
            var agents = new IAgent[]
            {
                new AiAgent(seed, 0), new AiAgent(seed, 1), new AiAgent(seed, 2), new AiAgent(seed, 3),
            };
            GameState captured = null;
            new GameDriver(agents).RunRound(GameEngine.NewRound(seed), (st, act) =>
            {
                if (captured == null
                    && st.Phase == RoundPhase.Play
                    && st.CurrentTrick != null
                    && st.CurrentTrick.Top != null)
                    captured = st.Clone();
            });
            Assert.That(captured, Is.Not.Null, "Play 페이즈 상태를 잡지 못했다");
            return captured;
        }
    }
}
