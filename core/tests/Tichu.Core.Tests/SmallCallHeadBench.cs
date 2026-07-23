using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Tichu.Core.Cards;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests.Bench
{
    /// <summary>
    /// Small Tichu 콜 헤드 격리 벤치. A팀 = 헤드 ON, B팀 = OFF(현행 강도게이트), 그 외 동일 휴리스틱.
    /// 미러드로 딜 편향 제거. 학습 시드[1..]와 분리된 벤치 시드[30_000_000..].
    /// τ 스윕 + 마진 95%CI + 콜빈도 진단. 채택: 홀드아웃 마진 유의 & 회귀없음(B1 선례).
    /// [Explicit].
    /// </summary>
    [Explicit, Category("Bench")]
    public class SmallCallHeadBench
    {
        [Test]
        public void CallHead_on_vs_off_mirrored_sweep()
        {
            const int Pairs = 8000;             // ×2 미러 = 16000 라운드/τ
            const ulong BaseSeed = 30_000_000;  // 학습과 분리
            double[] taus = { 0.50, 0.55 };
            var sb = new StringBuilder();
            sb.AppendLine("metric: onAvg=팀점수차/R(매치 승패), winRate/Wilson=라운드 승률(고분산 저평가)");

            foreach (double tau in taus)
            {
                double diffSum = 0, diffSq = 0; int onWins = 0, ties = 0, rounds = 0;
                long onCalls = 0, offCalls = 0, seatsSeen = 0;
                for (int s = 1; s <= Pairs; s++)
                {
                    ulong seed = BaseSeed + (ulong)(s * 7919);
                    // 진단: 교환 후 14장에서 ON(P>τ) vs OFF(용/봉황+HandPower≥7) 강도게이트 발화율.
                    var hands = PostExchangeHands(seed);
                    for (int seat = 0; seat < 4; seat++)
                    {
                        if (CallNet.Small.PredictProb(SmallTichuFeatures.Encode(hands[seat])) > tau) onCalls++;
                        if (OffStrengthGate(hands[seat])) offCalls++;
                        seatsSeen++;
                    }
                    for (int mirror = 0; mirror < 2; mirror++)
                    {
                        bool onTeamA = (mirror == 0);
                        var agents = new IAgent[4];
                        for (int i = 0; i < 4; i++)
                        {
                            bool teamA = (i % 2 == 0);
                            bool on = onTeamA ? teamA : !teamA;
                            agents[i] = new AiAgent(seed, i, useSmallTichuNet: on, smallThreshold: tau);
                        }
                        var outcome = new GameDriver(agents).RunRound(GameEngine.NewRound(seed));
                        int onScore = onTeamA ? outcome.Result.TeamATotal : outcome.Result.TeamBTotal;
                        int offScore = onTeamA ? outcome.Result.TeamBTotal : outcome.Result.TeamATotal;
                        double diff = onScore - offScore;
                        diffSum += diff; diffSq += diff * diff;
                        if (diff > 0) onWins++; else if (diff == 0) ties++;
                        rounds++;
                    }
                }
                double mean = diffSum / rounds;
                double var = diffSq / rounds - mean * mean;
                double se = Math.Sqrt(var / rounds);
                double wilson = WilsonLB(onWins, rounds);
                sb.AppendLine(
                    $"tau={tau:F2} rounds={rounds} onAvg={mean:F2}/R (95%CI [{mean - 1.96 * se:F2},{mean + 1.96 * se:F2}], se={se:F2}) " +
                    $"winRate={onWins / (double)rounds:P1} WilsonLB={wilson:F3} ties={ties} " +
                    $"onSmallRate={onCalls / (double)seatsSeen:P2} offSmallRate={offCalls / (double)seatsSeen:P2}");
            }
            var report = sb.ToString();
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "tichu_small_callhead_bench.txt"), report);
            TestContext.Progress.WriteLine(report);
        }

        [Test]
        public void CallHead_confirm_fixed_tau_heldout()
        {
            const int Pairs = 12000;             // ×2 미러 = 24000 라운드
            const ulong BaseSeed = 930_000_000;  // 학습·스윕과 완전 분리(홀드아웃)
            const double Tau = 0.55;
            double diffSum = 0, diffSq = 0; int onWins = 0, ties = 0, rounds = 0;
            double pairSum = 0, pairSq = 0; // 페어드: 미러 쌍 평균(트릭-점수 변동 상쇄 → 티츄 효과만)
            for (int s = 1; s <= Pairs; s++)
            {
                ulong seed = BaseSeed + (ulong)(s * 7919);
                double pairDiffSum = 0;
                for (int mirror = 0; mirror < 2; mirror++)
                {
                    bool onTeamA = (mirror == 0);
                    var agents = new IAgent[4];
                    for (int i = 0; i < 4; i++)
                    {
                        bool teamA = (i % 2 == 0);
                        bool on = onTeamA ? teamA : !teamA;
                        agents[i] = new AiAgent(seed, i, useSmallTichuNet: on, smallThreshold: Tau);
                    }
                    var outcome = new GameDriver(agents).RunRound(GameEngine.NewRound(seed));
                    int onScore = onTeamA ? outcome.Result.TeamATotal : outcome.Result.TeamBTotal;
                    int offScore = onTeamA ? outcome.Result.TeamBTotal : outcome.Result.TeamATotal;
                    double diff = onScore - offScore;
                    diffSum += diff; diffSq += diff * diff; pairDiffSum += diff;
                    if (diff > 0) onWins++; else if (diff == 0) ties++;
                    rounds++;
                }
                double pm = pairDiffSum / 2.0;
                pairSum += pm; pairSq += pm * pm;
            }
            double mean = diffSum / rounds;
            double se = Math.Sqrt((diffSq / rounds - mean * mean) / rounds);
            double pMean = pairSum / Pairs;
            double pSe = Math.Sqrt((pairSq / Pairs - pMean * pMean) / Pairs);
            var report =
                $"SMALL HELDOUT tau={Tau:F2} rounds={rounds}\n" +
                $"  per-round: onAvg={mean:F2}/R (95%CI [{mean - 1.96 * se:F2},{mean + 1.96 * se:F2}], se={se:F2}) winRate={onWins / (double)rounds:P1} WilsonLB={WilsonLB(onWins, rounds):F3}\n" +
                $"  paired(정확): onAvg={pMean:F2}/R (95%CI [{pMean - 1.96 * pSe:F2},{pMean + 1.96 * pSe:F2}], se={pSe:F2})";
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "tichu_small_callhead_heldout.txt"), report);
            TestContext.Progress.WriteLine(report);
        }

        // 교환 후 14장 손패(4좌석). Grand+Exchange 만 드라이브(플레이 전).
        private static List<Card>[] PostExchangeHands(ulong seed)
        {
            var s = GameEngine.NewRound(seed);
            var agents = new IAgent[4];
            for (int seat = 0; seat < 4; seat++) agents[seat] = new AiAgent(seed, seat);
            int grandNext = 0, exchangeNext = 0, steps = 0;
            while (s.Phase != RoundPhase.Play)
            {
                if (++steps > 100) throw new InvalidOperationException("setup stuck");
                if (s.Phase == RoundPhase.GrandTichuDecision)
                {
                    int seat = grandNext++;
                    bool call = agents[seat].CallGrandTichu(new DecisionContext(s, seat));
                    var res = GameEngine.Apply(s, call ? GameAction.CallGrandTichu(seat) : GameAction.DeclineGrandTichu(seat));
                    if (!res.Ok) throw new InvalidOperationException(res.RejectReason);
                }
                else if (s.Phase == RoundPhase.Exchange)
                {
                    int seat = exchangeNext++;
                    var ex = agents[seat].ChooseExchange(new DecisionContext(s, seat));
                    var res = GameEngine.Apply(s, GameAction.Exchange(seat, new[] { ex.ToLeft }, new[] { ex.ToPartner }, new[] { ex.ToRight }));
                    if (!res.Ok) throw new InvalidOperationException(res.RejectReason);
                }
                else break;
            }
            var hands = new List<Card>[4];
            for (int seat = 0; seat < 4; seat++) hands[seat] = new List<Card>(s.Seats[seat].Hand);
            return hands;
        }

        // OFF 강도게이트 재현: (용/봉황 보유 + HandPower≥7). 폭탄 단축은 ON/OFF 공통이라 divergence서 상쇄.
        private static bool OffStrengthGate(IReadOnlyList<Card> hand)
        {
            bool hasHighSpecial = false; int score = 0;
            for (int i = 0; i < hand.Count; i++)
            {
                var c = hand[i];
                switch (c.Special)
                {
                    case SpecialKind.Dragon: hasHighSpecial = true; score += 4; break;
                    case SpecialKind.Phoenix: hasHighSpecial = true; score += 3; break;
                    case SpecialKind.Dog: score += 1; break;
                    default:
                        if (c.Rank == 14) score += 2; else if (c.Rank == 13) score += 1; break;
                }
            }
            return hasHighSpecial && score >= 7;
        }

        private static double WilsonLB(int wins, int n, double z = 1.96)
        {
            if (n == 0) return 0;
            double phat = (double)wins / n, z2 = z * z;
            double denom = 1 + z2 / n;
            double center = phat + z2 / (2 * n);
            double margin = z * Math.Sqrt(phat * (1 - phat) / n + z2 / (4.0 * n * n));
            double lb = (center - margin) / denom;
            return lb < 0 ? 0 : (lb > 1 ? 1 : lb);
        }
    }
}
