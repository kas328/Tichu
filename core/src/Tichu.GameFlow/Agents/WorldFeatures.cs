using Tichu.Core.Cards;
using Tichu.Core.Game;

namespace Tichu.GameFlow.Agents
{
    /// <summary>
    /// D4 Fork A 리프 가치망 입력 인코더. 결정화된 완전정보 세계(GameState) → 관측좌석 상대 집계 피처.
    /// 하이브리드: 4좌석 손 집계(강도·특수·아웃·콜·획득점) + 글로벌(턴·트릭·소원·진행). 순수·결정적.
    /// 관측좌석 기준 회전(rel[0]=자기, rel[2]=파트너)이라 좌석 무관하게 일관.
    /// </summary>
    public static class WorldFeatures
    {
        public const int PerSeat = 8;
        public const int SeatBlock = 4 * PerSeat;  // 32
        public const int FeatureCount = SeatBlock + 10;  // 42

        public static float[] Encode(GameState world, int observerSeat)
        {
            var x = new float[FeatureCount];
            var seats = world.Seats;
            int handsSum = 0;

            for (int rel = 0; rel < 4; rel++)
            {
                int seat = (observerSeat + rel) % 4;
                var ps = seats[seat];
                var hand = ps.Hand;
                int b = rel * PerSeat;

                var rankCount = new int[16];
                int nHigh = 0;
                bool hasDragon = false, hasPhoenix = false;
                for (int i = 0; i < hand.Count; i++)
                {
                    var c = hand[i];
                    if (c.Special == SpecialKind.Dragon) hasDragon = true;
                    else if (c.Special == SpecialKind.Phoenix) hasPhoenix = true;
                    else if (c.Special == SpecialKind.None)
                    {
                        if (c.Rank >= 1 && c.Rank <= 15) rankCount[c.Rank]++;
                        if (c.Rank >= 11) nHigh++;
                    }
                }
                bool fourKind = false;
                for (int r = 1; r <= 15; r++) if (rankCount[r] == 4) { fourKind = true; break; }

                int wonPoints = 0;
                for (int i = 0; i < ps.WonCards.Count; i++) wonPoints += ps.WonCards[i].Points;

                x[b + 0] = hand.Count / 14f;
                x[b + 1] = nHigh / 8f;
                x[b + 2] = hasDragon ? 1f : 0f;
                x[b + 3] = hasPhoenix ? 1f : 0f;
                x[b + 4] = fourKind ? 1f : 0f;
                x[b + 5] = ps.IsOut ? 1f : 0f;
                x[b + 6] = ps.Call == TichuCall.GrandTichu ? 1f : (ps.Call == TichuCall.Tichu ? 0.5f : 0f);
                x[b + 7] = wonPoints / 100f;

                handsSum += hand.Count;
            }

            // 글로벌 (base 32).
            int turnRel = ((world.Turn - observerSeat) % 4 + 4) % 4;
            x[SeatBlock + turnRel] = 1f;             // 32..35: 턴 좌석 원핫(상대)

            var trick = world.CurrentTrick;
            if (trick != null && trick.Top != null)
            {
                x[SeatBlock + 4] = 1f;               // 36: 트릭 존재
                x[SeatBlock + 5] = trick.Top.Rank / 15f;   // 37: top 랭크
                x[SeatBlock + 6] = trick.AccumulatedPoints / 100f; // 38: 누적 점수
                x[SeatBlock + 7] = trick.LeadLength / 14f; // 39: 리드 길이
            }
            x[SeatBlock + 8] = world.Wish.HasValue ? world.Wish.Value / 15f : 0f; // 40: 마작 소원
            x[SeatBlock + 9] = handsSum / 56f;       // 41: 진행(손패합)
            return x;
        }
    }
}
