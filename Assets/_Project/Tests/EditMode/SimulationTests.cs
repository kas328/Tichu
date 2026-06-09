#nullable enable
using System;
using NUnit.Framework;
using Tichu.Core.Game;
using Tichu.Core.Tests.Sim;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// 헤드리스 시뮬레이터 + 결정성 + 무결성 테스트.
    /// Heavy 카테고리는 dotnet test --filter Category=Heavy 로만 실행.
    /// </summary>
    [TestFixture]
    public class SimulationTests
    {
        // ── 기본: 한 라운드 완주 ──────────────────────────────────────────────────

        [Test]
        public void Simulator_plays_a_full_round_without_exception()
        {
            const ulong seed = 42UL;
            var (state, result, log) = Simulator.PlayRound(seed);

            Assert.That(state.Phase, Is.EqualTo(RoundPhase.RoundEnd),
                "phase must be RoundEnd after ScoreRound");
            Assert.That(result, Is.Not.Null);
            Assert.That(log.Count, Is.GreaterThan(0), "at least one action must have been applied");
        }

        // ── 결정성: 동일 시드 → 동일 해시 ────────────────────────────────────────

        [Test]
        public void Determinism_same_seed_same_final_hash_round()
        {
            const ulong seed = 12345UL;
            var (s1, _, _) = Simulator.PlayRound(seed);
            var (s2, _, _) = Simulator.PlayRound(seed);

            Assert.That(s1.ComputeHash(), Is.EqualTo(s2.ComputeHash()),
                "same seed must produce identical final hash");
        }

        [Test]
        public void Determinism_same_seed_same_final_hash_game()
        {
            const ulong seed = 99999UL;
            var g1 = Simulator.PlayGame(seed);
            var g2 = Simulator.PlayGame(seed);

            Assert.That(g1.Scores.TeamA, Is.EqualTo(g2.Scores.TeamA));
            Assert.That(g1.Scores.TeamB, Is.EqualTo(g2.Scores.TeamB));
            Assert.That(g1.ComputeHash(), Is.EqualTo(g2.ComputeHash()));
        }

        // ── 결정성: 액션 로그 재생 → 동일 해시 ──────────────────────────────────

        [Test]
        public void Determinism_action_log_replay_matches()
        {
            const ulong seed = 777UL;
            var (state, _, log) = Simulator.PlayRound(seed);
            ulong originalHash = state.ComputeHash();

            ulong replayHash = Simulator.ReplayRound(seed, log);

            Assert.That(replayHash, Is.EqualTo(originalHash),
                "replaying the recorded action log must yield the same final hash");
        }

        // ── 프로퍼티: 다수 라운드 스코어 불변식 ──────────────────────────────────

        [Test]
        public void Property_no_illegal_state_over_many_rounds()
        {
            const int count = 2000;

            for (int i = 0; i < count; i++)
            {
                ulong seed = (ulong)(i * 1_000_003 + 7); // 충분히 분산된 시드

                RoundResult result;
                try
                {
                    var (_, r, _) = Simulator.PlayRound(seed);
                    result = r;
                }
                catch (Exception ex)
                {
                    Assert.Fail($"Round seed={seed} threw: {ex.Message}");
                    return;
                }

                // 원-투이면 (200,0) or (0,200); 일반 종료이면 합이 100.
                int sum = result.TeamACardPoints + result.TeamBCardPoints;
                bool isOneTwo = (result.TeamACardPoints == 200 && result.TeamBCardPoints == 0)
                             || (result.TeamACardPoints == 0   && result.TeamBCardPoints == 200);

                if (!isOneTwo)
                {
                    Assert.That(sum, Is.EqualTo(100),
                        $"seed={seed}: normal-end card points must sum to 100, got A={result.TeamACardPoints} B={result.TeamBCardPoints}");
                }
            }
        }

        // ── Heavy: 10만 라운드 무결성 ─────────────────────────────────────────────

        [Test, Explicit, Category("Heavy")]
        public void Heavy_100k_rounds_integrity()
        {
            const int count = 100_000;

            for (int i = 0; i < count; i++)
            {
                ulong seed = (ulong)i * 6_700_417UL + 1_000_000_007UL;

                RoundResult result;
                try
                {
                    var (_, r, _) = Simulator.PlayRound(seed);
                    result = r;
                }
                catch (Exception ex)
                {
                    Assert.Fail($"Round {i} seed={seed} threw: {ex.Message}");
                    return;
                }

                int sum = result.TeamACardPoints + result.TeamBCardPoints;
                bool isOneTwo = (result.TeamACardPoints == 200 && result.TeamBCardPoints == 0)
                             || (result.TeamACardPoints == 0   && result.TeamBCardPoints == 200);

                if (!isOneTwo)
                {
                    Assert.That(sum, Is.EqualTo(100),
                        $"round {i} seed={seed}: card points must sum to 100, got A={result.TeamACardPoints} B={result.TeamBCardPoints}");
                }
            }
        }
    }
}
