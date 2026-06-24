using System.Collections.Generic;
using Tichu.Core.Cards;
using Tichu.Core.Game;

namespace Tichu.GameFlow.Agents
{
    /// <summary>
    /// reach-probability 가중(콜 단서). 콜한 상대의 "콜 시점 손"(현재 배정 손 + 히스토리상 그 좌석이 낸 카드)이
    /// 강할수록 그 세계가 관측 콜과 일관 → 가중↑. 균등 샘플 + 이 가중 = 중요도 샘플링.
    /// 작은 티츄(14장)는 정확 재구성; 큰 티츄(8장·교환전)는 14장 재구성을 근사로 사용.
    /// </summary>
    public static class ReachWeight
    {
        private const double K = 0.15; // 가중 강도 시작값(P2-C 벤치 튜닝).

        /// <summary>손패 고카드 강함(Dragon4·Phoenix3·A2·K1). AiAgent.HandPower 미러.</summary>
        public static int HandStrength(IReadOnlyList<Card> hand)
        {
            int score = 0;
            for (int i = 0; i < hand.Count; i++)
            {
                var c = hand[i];
                switch (c.Special)
                {
                    case SpecialKind.Dragon: score += 4; break;
                    case SpecialKind.Phoenix: score += 3; break;
                    case SpecialKind.None:
                        if (c.Rank == 14) score += 2;
                        else if (c.Rank == 13) score += 1;
                        break;
                }
            }
            return score;
        }

        /// <summary>관측자 외 콜한 좌석의 콜시점 손 강함으로 세계 가중. 콜 없으면 1.0.</summary>
        public static double WorldWeight(GameState world, int observerSeat)
        {
            double weight = 1.0;
            for (int seat = 0; seat < 4; seat++)
            {
                if (seat == observerSeat) continue;
                if (world.Seats[seat].Call == TichuCall.None) continue;
                int atCall = HandStrength(world.Seats[seat].Hand) + PlayedStrength(world, seat);
                weight *= 1.0 + K * atCall;
            }
            return weight;
        }

        // 히스토리상 seat 이 낸 카드들의 강함 합(현재/완료 트릭).
        private static int PlayedStrength(GameState world, int seat)
        {
            int sum = 0;
            if (world.CurrentTrick != null) sum += PlaysOf(world.CurrentTrick.History, seat);
            foreach (var t in world.CompletedTricks) sum += PlaysOf(t.History, seat);
            return sum;
        }

        private static int PlaysOf(List<Play> history, int seat)
        {
            int sum = 0;
            foreach (var p in history)
                if (p.Seat == seat && p.Combination != null) sum += HandStrength(p.Combination.Cards);
            return sum;
        }
    }
}
