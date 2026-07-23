using System;
using System.IO;
using System.Text;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests.Bench
{
    /// <summary>
    /// B1 격리 벤치. A팀 = 콜 헤드 ON, B팀 = OFF(현행 HandPower), 그 외 전부 동일 휴리스틱.
    /// 미러드(양 배치)로 딜 편향 제거. 학습 시드[1..]와 분리된 벤치 시드[10_000_000..].
    /// τ 스윕으로 최적 임계값 탐색. 채택: margin≥0 & WilsonLB>0.5 & 회귀없음.
    /// [Explicit] — 기본 스위트 제외.
    /// </summary>
    [Explicit, Category("Bench")]
    public class GrandCallHeadBench
    {
        [Test]
        public void CallHead_on_vs_off_mirrored_sweep()
        {
            const int Pairs = 8000;             // ×2 미러 = 16000 라운드/τ (마진 CI 조임)
            const ulong BaseSeed = 10_000_000;  // 학습과 분리
            double[] taus = { 0.50, 0.55 };     // 초기 스윕 최고 구간 집중
            var sb = new StringBuilder();
            sb.AppendLine("metric: onAvg=팀점수차/R(매치 승패 결정), winRate/Wilson=라운드 승률(고분산 레버엔 저평가)");

            foreach (double tau in taus)
            {
                double diffSum = 0, diffSq = 0; int onWins = 0, ties = 0, rounds = 0;
                long onCalls = 0, offCalls = 0; // 그랜드 콜 빈도(진단): 초기 8장 게이트 divergence.
                for (int s = 1; s <= Pairs; s++)
                {
                    ulong seed = BaseSeed + (ulong)(s * 7919);
                    // 진단: 이 딜의 4좌석에서 ON(P>τ) vs OFF(HandPower≥10) 콜 수.
                    var s0 = GameEngine.NewRound(seed);
                    for (int seat = 0; seat < 4; seat++)
                    {
                        bool onCall = CallNet.Grand.PredictProb(GrandTichuFeatures.Encode(s0.Seats[seat].Hand)) > tau;
                        bool offCall = HandPowerGrand(s0.Seats[seat].Hand);
                        if (onCall) onCalls++;
                        if (offCall) offCalls++;
                    }
                    for (int mirror = 0; mirror < 2; mirror++)
                    {
                        bool onTeamA = (mirror == 0);
                        var agents = new IAgent[4];
                        for (int i = 0; i < 4; i++)
                        {
                            bool teamA = (i % 2 == 0);
                            bool on = onTeamA ? teamA : !teamA;
                            agents[i] = new AiAgent(seed, i, useGrandCallNet: on, grandThreshold: tau);
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
                double lo = mean - 1.96 * se, hi = mean + 1.96 * se;
                double wilson = WilsonLB(onWins, rounds);
                sb.AppendLine(
                    $"tau={tau:F2} rounds={rounds} onAvg={mean:F2}/R (95%CI [{lo:F2},{hi:F2}], se={se:F2}) " +
                    $"winRate={onWins / (double)rounds:P1} WilsonLB={wilson:F3} ties={ties} " +
                    $"onGrandRate={onCalls / (double)(Pairs * 4):P2} offGrandRate={offCalls / (double)(Pairs * 4):P2}");
            }
            var report = sb.ToString();
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "tichu_grand_callhead_bench.txt"), report);
            TestContext.Progress.WriteLine(report);
        }

        // OFF 게이트 재현(AiAgent.HandPower 미러): 용4·봉황3·개1·A2·K1 합 ≥ 10.
        private static bool HandPowerGrand(System.Collections.Generic.IReadOnlyList<Tichu.Core.Cards.Card> hand)
        {
            int score = 0;
            for (int i = 0; i < hand.Count; i++)
            {
                var c = hand[i];
                switch (c.Special)
                {
                    case Tichu.Core.Cards.SpecialKind.Dragon: score += 4; break;
                    case Tichu.Core.Cards.SpecialKind.Phoenix: score += 3; break;
                    case Tichu.Core.Cards.SpecialKind.Dog: score += 1; break;
                    default:
                        if (c.Rank == 14) score += 2; else if (c.Rank == 13) score += 1; break;
                }
            }
            return score >= 10;
        }

        /// <summary>
        /// 승자의 저주 교정: τ=0.55 를 스윕과 완전 분리된 시드(홀드아웃)에서 고정 실행 →
        /// 선택 편향 없는 마진 추정. 적대 리뷰 CONFIRMED(minor) 대응.
        /// 스윕 시드는 10_000_000 + s*7919; 여기선 900_000_000 + s*7919(범위 900M~963M, 학습 1..50k·
        /// 스윕 10M~73M 과 비겹침)로 재현.
        /// </summary>
        [Test]
        public void CallHead_confirm_fixed_tau055_heldout()
        {
            const int Pairs = 8000;              // ×2 미러 = 16000 라운드
            const ulong BaseSeed = 900_000_000;  // 학습·스윕과 완전 분리(홀드아웃)
            const double Tau = 0.55;             // 스윕 없이 고정
            double diffSum = 0, diffSq = 0; int onWins = 0, ties = 0, rounds = 0;
            for (int s = 1; s <= Pairs; s++)
            {
                ulong seed = BaseSeed + (ulong)(s * 7919);
                for (int mirror = 0; mirror < 2; mirror++)
                {
                    bool onTeamA = (mirror == 0);
                    var agents = new IAgent[4];
                    for (int i = 0; i < 4; i++)
                    {
                        bool teamA = (i % 2 == 0);
                        bool on = onTeamA ? teamA : !teamA;
                        agents[i] = new AiAgent(seed, i, useGrandCallNet: on, grandThreshold: Tau);
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
            double lo = mean - 1.96 * se, hi = mean + 1.96 * se;
            var report = $"HELDOUT tau=0.55 rounds={rounds} onAvg={mean:F2}/R (95%CI [{lo:F2},{hi:F2}], se={se:F2}) " +
                         $"winRate={onWins / (double)rounds:P1} WilsonLB={WilsonLB(onWins, rounds):F3} ties={ties}";
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "tichu_grand_callhead_heldout.txt"), report);
            TestContext.Progress.WriteLine(report);
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
