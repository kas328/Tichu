using System;
using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// GameFlow 풀게임 완주·결정성·불변식·Heavy 통합 테스트.
    /// Heavy 카테고리는 dotnet test --filter Category=Heavy 로만 실행.
    /// </summary>
    [TestFixture]
    public class GameFlowFullGameTests
    {
        // ── 헬퍼 ────────────────────────────────────────────────────────────────────

        private static Func<ulong, int, IAgent> AiFactory()
            => (rs, seat) => new AiAgent(rs, seat);

        private static Func<ulong, int, IAgent> RandomFactory()
            => (rs, seat) => new RandomAgent(rs, seat);

        /// <summary>원-투 또는 일반 종료 불변식: 카드 포인트 합.</summary>
        private static void AssertCardPointInvariant(RoundResult r, string label)
        {
            bool isOneTwo = (r.TeamACardPoints == 200 && r.TeamBCardPoints == 0)
                         || (r.TeamACardPoints == 0   && r.TeamBCardPoints == 200);
            if (!isOneTwo)
            {
                int sum = r.TeamACardPoints + r.TeamBCardPoints;
                Assert.That(sum, Is.EqualTo(100),
                    $"{label}: 일반 종료 카드 포인트 합이 100이어야 함. A={r.TeamACardPoints} B={r.TeamBCardPoints}");
            }
        }

        // ── 1. 다수 시드에 대한 AI vs AI 완주 ──────────────────────────────────────

        [Test]
        public void Ai_vs_ai_full_game_completes()
        {
            ulong[] seeds = { 1UL, 42UL, 777UL, 99999UL, 123456UL };
            var factory = AiFactory();

            foreach (ulong seed in seeds)
            {
                MatchResult result;
                try
                {
                    result = MatchRunner.RunMatch(seed, factory);
                }
                catch (Exception ex)
                {
                    Assert.Fail($"seed {seed}: {ex}");
                    return;
                }

                Assert.That(result.WinningTeam, Is.InRange(0, 1),
                    $"seed {seed}: WinningTeam은 0 또는 1이어야 함 (not -1)");
                Assert.That(Math.Max(result.TeamA, result.TeamB), Is.GreaterThanOrEqualTo(1000),
                    $"seed {seed}: 최소 한 팀이 1000점에 도달해야 함");
                if (result.WinningTeam == 0)
                    Assert.That(result.TeamA, Is.GreaterThan(result.TeamB),
                        $"seed {seed}: TeamA 승리 시 TeamA > TeamB");
                else
                    Assert.That(result.TeamB, Is.GreaterThan(result.TeamA),
                        $"seed {seed}: TeamB 승리 시 TeamB > TeamA");
            }
        }

        // ── 2. 동일 시드 → 동일 매치 결과 (결정성) ──────────────────────────────────

        [Test]
        public void Match_is_deterministic_same_seed()
        {
            const ulong seed = 55555UL;
            var factory = AiFactory();

            var r1 = MatchRunner.RunMatch(seed, factory);
            var r2 = MatchRunner.RunMatch(seed, factory);

            Assert.That(r1.TeamA,        Is.EqualTo(r2.TeamA),        "TeamA 총점이 같아야 함");
            Assert.That(r1.TeamB,        Is.EqualTo(r2.TeamB),        "TeamB 총점이 같아야 함");
            Assert.That(r1.Rounds.Count, Is.EqualTo(r2.Rounds.Count), "라운드 수가 같아야 함");

            for (int i = 0; i < r1.Rounds.Count; i++)
            {
                var a = r1.Rounds[i];
                var b = r2.Rounds[i];
                Assert.That(a.TeamACardPoints, Is.EqualTo(b.TeamACardPoints), $"Round {i} TeamACardPoints");
                Assert.That(a.TeamBCardPoints, Is.EqualTo(b.TeamBCardPoints), $"Round {i} TeamBCardPoints");
                Assert.That(a.TeamATichuDelta, Is.EqualTo(b.TeamATichuDelta), $"Round {i} TeamATichuDelta");
                Assert.That(a.TeamBTichuDelta, Is.EqualTo(b.TeamBTichuDelta), $"Round {i} TeamBTichuDelta");
                Assert.That(a.TeamATotal,      Is.EqualTo(b.TeamATotal),      $"Round {i} TeamATotal");
                Assert.That(a.TeamBTotal,      Is.EqualTo(b.TeamBTotal),      $"Round {i} TeamBTotal");
            }
        }

        // ── 3. 단일 라운드 결정성 + 로그 리플레이 ────────────────────────────────────

        [Test]
        public void Round_is_deterministic_and_replayable()
        {
            const ulong roundSeed = 314159UL;

            IAgent[] MakeAgents() => new IAgent[]
            {
                new AiAgent(roundSeed, 0),
                new AiAgent(roundSeed, 1),
                new AiAgent(roundSeed, 2),
                new AiAgent(roundSeed, 3)
            };

            // 동일 라운드를 두 번 실행 → 최종 해시 동일
            var out1 = new GameDriver(MakeAgents()).RunRound(GameEngine.NewRound(roundSeed));
            var out2 = new GameDriver(MakeAgents()).RunRound(GameEngine.NewRound(roundSeed));

            Assert.That(out1.State.ComputeHash(), Is.EqualTo(out2.State.ComputeHash()),
                "동일 시드 라운드는 동일 최종 해시를 가져야 함");

            // 첫 번째 실행의 로그를 신선한 상태에 재적용 → ScoreRound → 동일 해시
            var replay = GameEngine.NewRound(roundSeed);
            foreach (var a in out1.Log)
            {
                var res = GameEngine.Apply(replay, a);
                Assert.That(res.Ok, Is.True, $"리플레이 액션 거부: {a.Kind} seat {a.Seat}: {res.RejectReason}");
            }
            Assert.That(replay.Phase, Is.EqualTo(RoundPhase.Scoring));
            ScoreCalculator.ScoreRound(replay);

            Assert.That(replay.ComputeHash(), Is.EqualTo(out1.State.ComputeHash()),
                "로그 리플레이 결과가 원본과 동일한 해시여야 함");
        }

        // ── 4. 다수 라운드 카드 포인트 불변식 ───────────────────────────────────────

        [Test]
        public void Per_round_score_invariant_over_many_seeds()
        {
            // RunMatch 를 통해 약 2000라운드 수집
            const int targetRounds = 2000;
            var factory = AiFactory();
            int checked_ = 0;
            ulong masterSeed = 0UL;

            while (checked_ < targetRounds)
            {
                var match = MatchRunner.RunMatch(masterSeed++, factory);
                foreach (var r in match.Rounds)
                {
                    AssertCardPointInvariant(r, $"masterSeed={masterSeed - 1} round {checked_}");
                    checked_++;
                    if (checked_ >= targetRounds) break;
                }
            }

            Assert.That(checked_, Is.GreaterThanOrEqualTo(targetRounds),
                "충분한 라운드를 검사했어야 함");
        }

        // ── 5. 불법 상태 없음 + 종료 순서 유효성 ────────────────────────────────────

        [Test]
        public void No_illegal_state_and_finish_order_valid()
        {
            const int count = 500;
            var factory = AiFactory();

            for (int i = 0; i < count; i++)
            {
                ulong seed = (ulong)(i * 1_000_003 + 7);
                RoundOutcome outcome;
                try
                {
                    outcome = new GameDriver(new IAgent[]
                    {
                        new AiAgent(seed, 0), new AiAgent(seed, 1),
                        new AiAgent(seed, 2), new AiAgent(seed, 3)
                    }).RunRound(GameEngine.NewRound(seed));
                }
                catch (Exception ex)
                {
                    Assert.Fail($"seed={seed}: 예외 발생: {ex.Message}");
                    return;
                }

                var r = outcome.Result;
                var seats = outcome.State.Seats;

                // FinishOrder 1이 정확히 하나 존재
                int firstCount = 0;
                for (int s = 0; s < 4; s++)
                    if (seats[s].FinishOrder == 1) firstCount++;
                Assert.That(firstCount, Is.EqualTo(1),
                    $"seed={seed}: FinishOrder==1인 좌석이 정확히 하나여야 함");

                // FinishOrder 값들이 {0..4} 범위이고 중복/갭이 없다
                // (FinishOrder 0 = 미완주; 1,2,3 = 완주 순서; 원-투에서는 1,2만)
                var orders = new int[4];
                for (int s = 0; s < 4; s++) orders[s] = seats[s].FinishOrder;
                Array.Sort(orders);
                // 정렬 후: 일반 종료 = [0, 1, 2, 3], 원-투 = [0, 0, 1, 2]
                for (int k = 1; k < 4; k++)
                {
                    Assert.That(orders[k], Is.GreaterThanOrEqualTo(orders[k - 1]),
                        $"seed={seed}: FinishOrder 배열이 오름차순이어야 함 (갭/역전 없음)");
                    if (orders[k] > 0 && orders[k - 1] > 0)
                        Assert.That(orders[k] - orders[k - 1], Is.LessThanOrEqualTo(1),
                            $"seed={seed}: FinishOrder 갭 없어야 함");
                }

                // 종료 타입별 손패 검사
                bool isOneTwo = (r.TeamACardPoints == 200 && r.TeamBCardPoints == 0)
                             || (r.TeamACardPoints == 0   && r.TeamBCardPoints == 200);

                if (isOneTwo)
                {
                    // 원-투: 패배 팀 두 좌석에 손패가 있을 수 있음 (카드를 이미 다 낸 경우는 없을 수도 있음)
                    // 승리 팀(FinishOrder 1,2) 두 좌석은 손패 없음
                    for (int s = 0; s < 4; s++)
                    {
                        if (seats[s].FinishOrder == 1 || seats[s].FinishOrder == 2)
                            Assert.That(seats[s].Hand.Count, Is.EqualTo(0),
                                $"seed={seed}: 원-투 승리 좌석 {s}는 손패가 없어야 함");
                    }
                }
                else
                {
                    // 일반 종료: 아웃된 3좌석은 손패 없음, 마지막 1좌석은 손패가 있을 수 있음
                    int notOutCount = 0;
                    for (int s = 0; s < 4; s++)
                        if (!seats[s].IsOut) notOutCount++;
                    Assert.That(notOutCount, Is.EqualTo(1),
                        $"seed={seed}: 일반 종료 시 IsOut이 아닌 좌석이 정확히 하나여야 함");
                    for (int s = 0; s < 4; s++)
                        if (seats[s].IsOut)
                            Assert.That(seats[s].Hand.Count, Is.EqualTo(0),
                                $"seed={seed}: 아웃된 좌석 {s}는 손패가 없어야 함");
                }
            }
        }

        // ── 6. Heavy: 10만 라운드 무결성 ────────────────────────────────────────────

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
                    var outcome = new GameDriver(new IAgent[]
                    {
                        new AiAgent(seed, 0), new AiAgent(seed, 1),
                        new AiAgent(seed, 2), new AiAgent(seed, 3)
                    }).RunRound(GameEngine.NewRound(seed));
                    result = outcome.Result;
                }
                catch (Exception ex)
                {
                    Assert.Fail($"Round {i} seed={seed} threw: {ex}");
                    return;
                }

                AssertCardPointInvariant(result, $"round {i} seed={seed}");
            }
        }

        // ── 7. AI vs Random: AI 팀 평균이 Random 팀 이상 ────────────────────────────

        [Test, Explicit]
        public void Ai_beats_random_on_average()
        {
            // seats {0,2} = AiAgent (TeamA), seats {1,3} = RandomAgent (TeamB)
            Func<ulong, int, IAgent> mixedFactory = (rs, seat) =>
                seat % 2 == 0
                    ? (IAgent)new AiAgent(rs, seat)
                    : new RandomAgent(rs, seat);

            const int matchCount = 100;
            long totalAi = 0, totalRandom = 0;

            for (int i = 0; i < matchCount; i++)
            {
                ulong seed = (ulong)(i * 31337 + 1);
                var match = MatchRunner.RunMatch(seed, mixedFactory);
                totalAi     += match.TeamA;
                totalRandom += match.TeamB;
            }

            double avgAi     = totalAi     / (double)matchCount;
            double avgRandom = totalRandom / (double)matchCount;

            // AI 팀 평균이 Random 팀 이상이어야 함 (휴리스틱 품질 문서화)
            Assert.That(avgAi, Is.GreaterThanOrEqualTo(avgRandom),
                $"AI 팀 평균({avgAi:F1})이 Random 팀 평균({avgRandom:F1}) 이상이어야 함");
        }
    }
}
