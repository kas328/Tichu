using System.Linq;
using NUnit.Framework;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// MatchRunner.Decide (순수 함수) + RunMatch (전체 매치 구동) 검증.
    /// </summary>
    [TestFixture]
    public class GameFlowMatchTests
    {
        // ── Decide 단위 테스트 (플레이 없음) ─────────────────────────────────────

        [Test]
        public void Decide_both_at_target_tied_continues()
        {
            Assert.That(MatchRunner.Decide(1000, 1000, 1000), Is.EqualTo(MatchDecision.Continue));
        }

        [Test]
        public void Decide_teamA_strictly_leads_above_target_wins()
        {
            Assert.That(MatchRunner.Decide(1010, 1000, 1000), Is.EqualTo(MatchDecision.TeamAWins));
        }

        [Test]
        public void Decide_teamA_at_target_opponent_below_wins()
        {
            Assert.That(MatchRunner.Decide(1000, 990, 1000), Is.EqualTo(MatchDecision.TeamAWins));
        }

        [Test]
        public void Decide_teamB_below_target_teamA_at_target_teamA_wins()
        {
            // 1000 >= target이고 990 < target이므로 A가 이김 (crosser is the leader)
            Assert.That(MatchRunner.Decide(1000, 990, 1000), Is.EqualTo(MatchDecision.TeamAWins));
        }

        [Test]
        public void Decide_teamB_at_target_opponent_below_wins()
        {
            Assert.That(MatchRunner.Decide(990, 1000, 1000), Is.EqualTo(MatchDecision.TeamBWins));
        }

        [Test]
        public void Decide_neither_at_target_continues()
        {
            Assert.That(MatchRunner.Decide(500, 400, 1000), Is.EqualTo(MatchDecision.Continue));
        }

        [Test]
        public void Decide_teamB_strictly_leads_above_target_wins()
        {
            Assert.That(MatchRunner.Decide(1000, 1010, 1000), Is.EqualTo(MatchDecision.TeamBWins));
        }

        // ── RunMatch 통합 테스트 ──────────────────────────────────────────────────

        [Test]
        public void RunMatch_with_ai_agents_produces_a_winner()
        {
            var result = MatchRunner.RunMatch(42UL, (rs, seat) => new AiAgent(rs, seat));

            Assert.That(result.WinningTeam, Is.InRange(0, 1),
                "WinningTeam must be 0 or 1, not -1");
            Assert.That(System.Math.Max(result.TeamA, result.TeamB), Is.GreaterThanOrEqualTo(1000),
                "at least one team must have reached 1000");
            if (result.WinningTeam == 0)
                Assert.That(result.TeamA, Is.GreaterThan(result.TeamB), "winner must be strictly ahead");
            else
                Assert.That(result.TeamB, Is.GreaterThan(result.TeamA), "winner must be strictly ahead");
        }

        [Test]
        public void RunMatch_carry_over_is_cumulative()
        {
            var result = MatchRunner.RunMatch(7UL, (rs, seat) => new AiAgent(rs, seat));

            int sumA = result.Rounds.Sum(r => r.TeamATotal);
            int sumB = result.Rounds.Sum(r => r.TeamBTotal);

            Assert.That(result.TeamA, Is.EqualTo(sumA),
                "TeamA must equal sum of per-round TeamATotal");
            Assert.That(result.TeamB, Is.EqualTo(sumB),
                "TeamB must equal sum of per-round TeamBTotal");
        }

        [Test]
        public void RunMatch_is_deterministic()
        {
            const ulong seed = 123456UL;
            var r1 = MatchRunner.RunMatch(seed, (rs, seat) => new AiAgent(rs, seat));
            var r2 = MatchRunner.RunMatch(seed, (rs, seat) => new AiAgent(rs, seat));

            Assert.That(r1.TeamA, Is.EqualTo(r2.TeamA));
            Assert.That(r1.TeamB, Is.EqualTo(r2.TeamB));
            Assert.That(r1.Rounds.Count, Is.EqualTo(r2.Rounds.Count));

            for (int i = 0; i < r1.Rounds.Count; i++)
            {
                Assert.That(r1.Rounds[i].TeamATotal, Is.EqualTo(r2.Rounds[i].TeamATotal),
                    $"Round {i} TeamATotal mismatch");
                Assert.That(r1.Rounds[i].TeamBTotal, Is.EqualTo(r2.Rounds[i].TeamBTotal),
                    $"Round {i} TeamBTotal mismatch");
            }
        }

        [Test]
        public void RunMatch_maxRounds_guard_returns_minus_one()
        {
            var result = MatchRunner.RunMatch(99UL, (rs, seat) => new AiAgent(rs, seat),
                target: 1_000_000, maxRounds: 3);

            Assert.That(result.WinningTeam, Is.EqualTo(-1),
                "WinningTeam must be -1 when maxRounds is hit");
            Assert.That(result.Rounds.Count, Is.EqualTo(3),
                "exactly maxRounds rounds must have been played");
        }
    }
}
