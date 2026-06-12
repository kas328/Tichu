using NUnit.Framework;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;
using Tichu.Presentation;

namespace Tichu.Presentation.Tests
{
    /// <summary>
    /// AsyncGameDriver.RunRoundAsync — 비동기 1라운드 오케스트레이션 검증.
    /// 동기 GameDriver 의 구조적 미러이므로, 모든 결정이 즉시 완료되어
    /// RunRoundAsync(s, default).GetAwaiter().GetResult() 로 동기처럼 완주한다.
    /// 핵심: 동일 시드에 대해 동기 드라이버와 비트 동일 결과를 내는 오라클 교차검증.
    /// </summary>
    [TestFixture]
    public class AsyncGameDriverTests
    {
        private static IDecisionAgent[] FourDefaults() =>
            new IDecisionAgent[]
            {
                new ScriptedDecisionAgent(),
                new ScriptedDecisionAgent(),
                new ScriptedDecisionAgent(),
                new ScriptedDecisionAgent(),
            };

        // ── 완주 ──────────────────────────────────────────────────────────────────

        [Test]
        public void Async_round_reaches_end()
        {
            var driver = new AsyncGameDriver(FourDefaults());
            var outcome = driver.RunRoundAsync(GameEngine.NewRound(42UL), default)
                .GetAwaiter().GetResult();

            Assert.That(outcome.State.Phase, Is.EqualTo(RoundPhase.RoundEnd));
            Assert.That(outcome.Result, Is.Not.Null);
            Assert.That(outcome.Log.Count, Is.GreaterThan(0));
        }

        // ── 로그 리플레이 결정성 ────────────────────────────────────────────────────

        [Test]
        public void Async_replay_reproduces_hash()
        {
            var driver = new AsyncGameDriver(FourDefaults());
            var outcome = driver.RunRoundAsync(GameEngine.NewRound(42UL), default)
                .GetAwaiter().GetResult();

            // 신선한 동일 시드 라운드에 로그를 순서대로 재적용.
            var replay = GameEngine.NewRound(42UL);
            foreach (var a in outcome.Log)
            {
                var r = GameEngine.Apply(replay, a);
                Assert.That(r.Ok, Is.True, $"리플레이 액션 거부됨: {a.Kind} seat {a.Seat}: {r.RejectReason}");
            }
            // 원본은 RunRoundAsync 후 ScoreRound 까지 갔으므로 리플레이도 Scoring 에서 ScoreRound 호출.
            Assert.That(replay.Phase, Is.EqualTo(RoundPhase.Scoring));
            ScoreCalculator.ScoreRound(replay);

            Assert.That(replay.ComputeHash(), Is.EqualTo(outcome.State.ComputeHash()));
        }

        // ── 오라클: 동기 드라이버와 비트 동일 결과 ─────────────────────────────────────

        [TestCase(1UL)]
        [TestCase(7UL)]
        [TestCase(42UL)]
        [TestCase(123UL)]
        [TestCase(9999UL)]
        public void Oracle_matches_sync_driver(ulong seed)
        {
            // (a) 동기 드라이버: AiAgent×4.
            var sync = new GameDriver(new IAgent[]
            {
                new AiAgent(seed, 0),
                new AiAgent(seed, 1),
                new AiAgent(seed, 2),
                new AiAgent(seed, 3),
            }).RunRound(GameEngine.NewRound(seed));

            // (b) 비동기 드라이버: AiDecisionAgent×4(동일 시드/좌석으로 AiAgent 래핑).
            var async = new AsyncGameDriver(new IDecisionAgent[]
            {
                new AiDecisionAgent(seed, 0),
                new AiDecisionAgent(seed, 1),
                new AiDecisionAgent(seed, 2),
                new AiDecisionAgent(seed, 3),
            }).RunRoundAsync(GameEngine.NewRound(seed), default).GetAwaiter().GetResult();

            // 동일 액션 시퀀스 ⇒ 동일 최종 상태 ⇒ 동일 해시, 동일 로그 길이.
            Assert.That(async.State.ComputeHash(), Is.EqualTo(sync.State.ComputeHash()),
                $"seed {seed}: 최종 상태 해시가 동기 드라이버와 달라졌다(구조 분기).");
            Assert.That(async.Log.Count, Is.EqualTo(sync.Log.Count),
                $"seed {seed}: 로그 길이가 동기 드라이버와 다르다(구조 분기).");
        }
    }
}
