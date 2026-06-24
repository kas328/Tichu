using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests.Bench
{
    /// <summary>한 벤치 실행 집계(라운드 단위).</summary>
    public struct BenchResult
    {
        public int Rounds;
        public long PimcDiffSum;   // 관측(PIMC) 팀점수차 누적
        public int PimcWins;       // PIMC 팀점수차 > 0 인 라운드 수
    }

    /// <summary>
    /// PIMC(주입 config) vs 현 휴리스틱(AiAgent) 라운드 벤치. 미러드딜(같은 셔플·좌우 팀 스왑)로
    /// 딜 운을 상쇄한다. 순수 C#(UnityEngine 무의존) → 백그라운드 스레드/배치 실행 가능.
    /// </summary>
    public static class PimcBench
    {
        /// <summary>pairs 딜 각각을 좌우로 미러(=2*pairs 라운드) 실행. PIMC 팀점수차 합·승수 반환.</summary>
        public static BenchResult RunMirrored(int pairs, ulong baseSeed, PolicyConfig pimcConfig)
        {
            long diffSum = 0;
            int wins = 0, rounds = 0;
            for (int s = 1; s <= pairs; s++)
            {
                ulong seed = baseSeed + (ulong)(s * 7919);
                diffSum += RoundPimcDiff(seed, pimcConfig, pimcOnTeamA: true,  ref wins); rounds++;
                diffSum += RoundPimcDiff(seed, pimcConfig, pimcOnTeamA: false, ref wins); rounds++;
            }
            return new BenchResult { Rounds = rounds, PimcDiffSum = diffSum, PimcWins = wins };
        }

        // 한 라운드: 좌석 0,2=팀A·1,3=팀B. pimcOnTeamA면 팀A가 PIMC, 아니면 팀B가 PIMC.
        private static long RoundPimcDiff(ulong seed, PolicyConfig cfg, bool pimcOnTeamA, ref int wins)
        {
            var agents = new IAgent[4];
            for (int i = 0; i < 4; i++)
            {
                bool teamA = (i % 2 == 0);
                bool isPimc = pimcOnTeamA ? teamA : !teamA;
                agents[i] = isPimc ? (IAgent)new PimcAgent(seed, i, cfg) : new AiAgent(seed, i);
            }
            var outcome = new GameDriver(agents).RunRound(GameEngine.NewRound(seed));
            int pimcDiff = pimcOnTeamA
                ? outcome.Result.TeamATotal - outcome.Result.TeamBTotal
                : outcome.Result.TeamBTotal - outcome.Result.TeamATotal;
            if (pimcDiff > 0) wins++;
            return pimcDiff;
        }
    }
}
