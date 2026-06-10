using System;
using System.Collections.Generic;
using Tichu.Core;
using Tichu.Core.Game;
using Tichu.GameFlow.Agents;

namespace Tichu.GameFlow
{
    /// <summary>매치 종료 판정 결과.</summary>
    public enum MatchDecision
    {
        Continue,
        TeamAWins,
        TeamBWins
    }

    /// <summary>매치 진행 중 누적 상태.</summary>
    public sealed class MatchState
    {
        public ulong MasterSeed;
        public Rng   Master;
        public int   RoundIndex;
        public int   TeamA;
        public int   TeamB;
        public readonly List<RoundResult> Rounds = new List<RoundResult>();
    }

    /// <summary>매치 최종 결과.</summary>
    public sealed class MatchResult
    {
        /// <summary>승리 팀 (0=TeamA, 1=TeamB, -1=maxRounds 초과).</summary>
        public int WinningTeam { get; }
        public int TeamA { get; }
        public int TeamB { get; }
        public IReadOnlyList<RoundResult> Rounds { get; }

        public MatchResult(int winningTeam, int teamA, int teamB, IReadOnlyList<RoundResult> rounds)
        {
            WinningTeam = winningTeam;
            TeamA = teamA;
            TeamB = teamB;
            Rounds = rounds;
        }
    }

    /// <summary>
    /// 여러 라운드를 목표 점수(기본 1000)까지 구동하는 매치 러너.
    /// 동점 시 continue 규칙(양 팀이 같은 점수로 target 돌파 시 계속 진행)을 포함한다.
    /// </summary>
    public static class MatchRunner
    {
        /// <summary>
        /// 순수 종료/연속 판정 함수. 플레이와 무관하게 단위 테스트 가능.
        ///
        /// 규칙:
        /// - 아무도 target 미달이면 Continue.
        /// - 양 팀이 target 이상이고 동점이면 Continue (계속 진행).
        /// - 그 외(한 팀 이상이 target 이상이고 점수가 다름): 더 높은 팀이 승리.
        /// </summary>
        public static MatchDecision Decide(int teamA, int teamB, int target)
        {
            bool aCrossed = teamA >= target;
            bool bCrossed = teamB >= target;

            if (!aCrossed && !bCrossed)
                return MatchDecision.Continue;

            if (teamA == teamB)
                return MatchDecision.Continue;  // 동점 → 계속 진행

            return teamA > teamB ? MatchDecision.TeamAWins : MatchDecision.TeamBWins;
        }

        /// <summary>
        /// 결정적 매치를 끝까지 구동한다.
        /// 각 라운드마다 masterSeed 로부터 파생된 roundSeed 를 사용하며,
        /// agentFactory(roundSeed, seat) 로 라운드별·좌석별로 에이전트를 재생성한다.
        /// </summary>
        public static MatchResult RunMatch(
            ulong masterSeed,
            Func<ulong, int, IAgent> agentFactory,
            int target    = 1000,
            int maxRounds = 10000)
        {
            var m = new MatchState
            {
                MasterSeed = masterSeed,
                Master     = new Rng(masterSeed)
            };

            while (m.RoundIndex < maxRounds)
            {
                // Rng 는 struct → field 에서 직접 NextULong() 을 호출하면 복사본에 적용되므로
                // 로컬 변수로 꺼내 전진시킨 뒤 다시 기록한다.
                var master = m.Master;
                ulong roundSeed = master.NextULong();
                m.Master = master;

                var agents = new IAgent[4];
                for (int seat = 0; seat < 4; seat++)
                    agents[seat] = agentFactory(roundSeed, seat);

                var outcome = new GameDriver(agents).RunRound(GameEngine.NewRound(roundSeed));

                m.TeamA += outcome.Result.TeamATotal;
                m.TeamB += outcome.Result.TeamBTotal;
                m.Rounds.Add(outcome.Result);
                m.RoundIndex++;

                var d = Decide(m.TeamA, m.TeamB, target);
                if (d != MatchDecision.Continue)
                {
                    int winner = d == MatchDecision.TeamAWins ? 0 : 1;
                    return new MatchResult(winner, m.TeamA, m.TeamB, m.Rounds);
                }
            }

            // maxRounds 초과.
            return new MatchResult(-1, m.TeamA, m.TeamB, m.Rounds);
        }
    }
}
