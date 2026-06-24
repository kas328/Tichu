using Tichu.Core.Game;

namespace Tichu.GameFlow.Agents
{
    /// <summary>PIMC 탐색 코어. P2-A: 단일-세계 롤아웃 보상만 제공한다.</summary>
    public static class Pimc
    {
        /// <summary>
        /// world(완전정보 세계, 호출자가 클론을 넘긴다)를 현 휴리스틱(AiAgent)으로 끝까지 플레이하고
        /// 관측 좌석 팀 기준 점수차(TeamATotal−TeamBTotal, 팀1이면 부호 반전)를 반환한다.
        /// AiAgent 는 결정적이므로 (world, policySeed) 고정 시 보상도 결정적이다.
        /// </summary>
        public static int Rollout(GameState world, int observerSeat, ulong policySeed)
        {
            var agents = new IAgent[4];
            for (int seat = 0; seat < 4; seat++)
                agents[seat] = new AiAgent(policySeed, seat);

            var outcome = new GameDriver(agents).RunRound(world);
            int diff = outcome.Result.TeamATotal - outcome.Result.TeamBTotal;
            return Seating.TeamOf(observerSeat) == 0 ? diff : -diff;
        }
    }
}
