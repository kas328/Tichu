using System.Collections.Generic;
using Tichu.Core.Cards;

namespace Tichu.Core.Combinations
{
    /// <summary>
    /// 손패에서 폭탄(4카드 동값 / 같은 무늬 5장 이상 연속)에 속한 카드들의 집합을 찾는다.
    /// 봉황 제외, 턴·트릭 문맥 무관(들고만 있으면 폭탄). UI 강조용.
    /// </summary>
    public static class BombScanner
    {
        public static HashSet<Card> BombCards(IReadOnlyList<Card> hand)
        {
            var result = new HashSet<Card>();
            if (hand == null || hand.Count == 0) return result;

            // 1) 4카드 폭탄: 같은 일반 랭크 4장 이상.
            var byRank = new List<Card>[15]; // index 1..14
            for (int r = 0; r < 15; r++) byRank[r] = new List<Card>();
            foreach (var c in hand)
                if (!c.IsSpecial) byRank[c.Rank].Add(c);
            for (int r = 2; r <= 14; r++)
                if (byRank[r].Count >= 4)
                    foreach (var c in byRank[r]) result.Add(c);

            // 2) 스트레이트플러시 폭탄: 같은 무늬 연속 길이 >=5인 극대 구간 전체.
            foreach (Suit suit in new[] { Suit.Jade, Suit.Sword, Suit.Pagoda, Suit.Star })
            {
                var suited = new Card[15];
                var has = new bool[15];
                foreach (var c in hand)
                    if (!c.IsSpecial && c.Suit == suit) { suited[c.Rank] = c; has[c.Rank] = true; }

                int run = 0;
                for (int r = 2; r <= 15; r++) // r=15 = sentinel: 끝 구간 flush
                {
                    bool present = r <= 14 && has[r];
                    if (present) { run++; }
                    else
                    {
                        if (run >= 5)
                            for (int k = r - run; k <= r - 1; k++) result.Add(suited[k]);
                        run = 0;
                    }
                }
            }
            return result;
        }
    }
}
