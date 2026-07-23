using System;
using System.IO;
using NUnit.Framework;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests.Bench
{
    /// <summary>
    /// D4 Fork A ① 리프 A/B. V-리프 PimcAgent vs 롤아웃-리프 PimcAgent(같은 티어, UseValueNetLeaf만 차이),
    /// 미러드딜. 동일 세계수라 정확도(탈노이즈) 격리. 학습 시드[1..]와 분리(40M/50M). Wilson 게이트.
    /// ⚠️PIMC 느림 → %TEMP% 증분 기록(중단 대비). [Explicit].
    /// </summary>
    [Explicit, Category("Bench")]
    public class ValueLeafBench
    {
        [Test] public void ValueLeaf_vs_rollout_normal() => RunAB(PolicyConfig.For(Difficulty.Normal), 30, 40_000_000UL, "normal");
        [Test] public void ValueLeaf_vs_rollout_expert() => RunAB(PolicyConfig.For(Difficulty.Expert), 30, 50_000_000UL, "expert");

        private static void RunAB(PolicyConfig tier, int pairs, ulong baseSeed, string name)
        {
            var vCfg = tier.WithValueNetLeaf(true);
            var rCfg = tier.WithValueNetLeaf(false);
            string path = Path.Combine(Path.GetTempPath(), $"tichu_valueleaf_{name}.txt");
            double diffSum = 0, diffSq = 0; int vWins = 0, ties = 0, rounds = 0;
            for (int s = 1; s <= pairs; s++)
            {
                ulong seed = baseSeed + (ulong)(s * 7919);
                for (int mirror = 0; mirror < 2; mirror++)
                {
                    bool vTeamA = (mirror == 0);
                    var agents = new IAgent[4];
                    for (int i = 0; i < 4; i++)
                    {
                        bool teamA = (i % 2 == 0);
                        bool isV = vTeamA ? teamA : !teamA;
                        agents[i] = new PimcAgent(seed, i, isV ? vCfg : rCfg);
                    }
                    var outcome = new GameDriver(agents).RunRound(GameEngine.NewRound(seed));
                    int vScore = vTeamA ? outcome.Result.TeamATotal : outcome.Result.TeamBTotal;
                    int rScore = vTeamA ? outcome.Result.TeamBTotal : outcome.Result.TeamATotal;
                    double diff = vScore - rScore;
                    diffSum += diff; diffSq += diff * diff;
                    if (diff > 0) vWins++; else if (diff == 0) ties++;
                    rounds++;

                    double mean = diffSum / rounds;
                    double se = Math.Sqrt((diffSq / rounds - mean * mean) / rounds);
                    File.WriteAllText(path,
                        $"VALUELEAF[{name}] rounds={rounds}/{pairs * 2} vAvg={mean:F2}/R (95%CI [{mean - 1.96 * se:F2},{mean + 1.96 * se:F2}]) " +
                        $"vWins={vWins}/{rounds} ({vWins / (double)rounds:P1}) WilsonLB={WilsonLB(vWins, rounds):F3} ties={ties}");
                }
            }
            TestContext.Progress.WriteLine(File.ReadAllText(path));
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
