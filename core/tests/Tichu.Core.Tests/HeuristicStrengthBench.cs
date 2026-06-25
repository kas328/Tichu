using System;
using System.IO;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests.Bench
{
    /// <summary>
    /// 신(현재) 휴리스틱 AiAgent vs 구(P2-D, OldAiAgent) 휴리스틱 미러드 맞대결.
    /// 순수 휴리스틱이라 PIMC 없이 수천 라운드를 ms 단위로 돌릴 수 있다 → P2-E 누적
    /// 휴리스틱 변경(#1 티츄게이트·#4 파트너밟기·#3 블로킹)이 강도를 떨어뜨리지 않았는지 검증.
    /// [Explicit] — 기본 스위트 제외.
    /// </summary>
    [Explicit, Category("Bench")]
    public class HeuristicStrengthBench
    {
        [Test]
        public void New_vs_Old_heuristic_mirrored()
        {
            const int Pairs = 2000;     // ×2 미러 = 4000 라운드
            const ulong BaseSeed = 1;
            long newDiffSum = 0;
            int newWins = 0, ties = 0, rounds = 0;

            for (int s = 1; s <= Pairs; s++)
            {
                ulong seed = BaseSeed + (ulong)(s * 7919);
                for (int mirror = 0; mirror < 2; mirror++)
                {
                    bool newOnTeamA = (mirror == 0);
                    var agents = new IAgent[4];
                    for (int i = 0; i < 4; i++)
                    {
                        bool teamA = (i % 2 == 0);
                        bool isNew = newOnTeamA ? teamA : !teamA;
                        agents[i] = isNew ? (IAgent)new AiAgent(seed, i) : new OldAiAgent(seed, i);
                    }
                    var outcome = new GameDriver(agents).RunRound(GameEngine.NewRound(seed));
                    int newScore = newOnTeamA ? outcome.Result.TeamATotal : outcome.Result.TeamBTotal;
                    int oldScore = newOnTeamA ? outcome.Result.TeamBTotal : outcome.Result.TeamATotal;
                    int diff = newScore - oldScore;
                    newDiffSum += diff;
                    if (diff > 0) newWins++; else if (diff == 0) ties++;
                    rounds++;
                }
            }

            double wilson = WilsonLB(newWins, rounds);
            string report = string.Format(
                "rounds={0} newAvg={1:F1}/R newWins={2}/{3} ({4:P1}) ties={5} WilsonLB={6:F3}",
                rounds, newDiffSum / (double)rounds, newWins, rounds, newWins / (double)rounds, ties, wilson);
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "tichu_heuristic_strength.txt"), report);
            TestContext.Progress.WriteLine(report);
        }

        private static double WilsonLB(int wins, int n, double z = 1.96)
        {
            if (n == 0) return 0;
            double phat = (double)wins / n;
            double z2 = z * z;
            double denom = 1 + z2 / n;
            double center = phat + z2 / (2 * n);
            double margin = z * Math.Sqrt(phat * (1 - phat) / n + z2 / (4.0 * n * n));
            double lb = (center - margin) / denom;
            return lb < 0 ? 0 : (lb > 1 ? 1 : lb);
        }
    }
}
