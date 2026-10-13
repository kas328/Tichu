using Tichu.Core.Cards;
using Tichu.Core.Combinations;

namespace Tichu.Presentation.Views
{
    /// <summary>
    /// 카드 1장의 표기(라벨/색/정렬/아틀라스 키) 포맷. RuntimeTableView·CardView 공용(DRY).
    /// </summary>
    public static class CardFormat
    {
        /// <summary>카드 면 라벨. 특수=한글, 일반="랭크\n무늬이름".</summary>
        public static string Label(Card c)
        {
            if (c.IsSpecial) return SpecialName(c);
            return $"{RankLabel(c.Rank)}\n{SuitName(c.Suit)}";
        }

        /// <summary>무늬 아이콘이 있을 때의 면 라벨 — 일반은 랭크만(무늬는 아이콘), 특수는 이름.</summary>
        public static string IconLabel(Card c) => c.IsSpecial ? SpecialName(c) : RankLabel(c.Rank);

        private static string SpecialName(Card c)
        {
            switch (c.Special)
            {
                case SpecialKind.Dragon:  return "용";
                case SpecialKind.Phoenix: return "봉황";
                case SpecialKind.Dog:     return "개";
                default:                  return "1";   // 마작
            }
        }

        public static string RankLabel(int r)
        {
            switch (r) { case 14: return "A"; case 13: return "K"; case 12: return "Q"; case 11: return "J"; default: return r.ToString(); }
        }

        /// <summary>무늬 한 글자. ♠♣♥♦ 는 Android 폰트에 없어 보이지 않으므로 한글로 쓴다.</summary>
        public static string SuitName(Suit s)
        {
            switch (s) { case Suit.Jade: return "옥"; case Suit.Sword: return "검"; case Suit.Pagoda: return "탑"; case Suit.Star: return "별"; default: return ""; }
        }

        /// <summary>손패/트릭 정렬 키. 개=0, 마작=1, 봉=15, 용=16, 일반=랭크.</summary>
        public static int SortKey(Card c)
        {
            switch (c.Special)
            {
                case SpecialKind.Dog: return 0;
                case SpecialKind.Mahjong: return 1;
                case SpecialKind.Phoenix: return 15;
                case SpecialKind.Dragon: return 16;
                default: return c.Rank;
            }
        }

        /// <summary>CardSpriteAtlas 조회 키. 특수=종류명, 일반="랭크_무늬".</summary>
        public static string Key(Card c) => c.IsSpecial ? c.Special.ToString() : $"{c.Rank}_{c.Suit}";

        /// <summary>CardSpriteAtlas 아이콘 키. 특수=종류명, 일반=무늬명(같은 무늬는 같은 아이콘).</summary>
        public static string IconKey(Card c) => c.IsSpecial ? c.Special.ToString() : c.Suit.ToString();

        // 스트레이트/연속페어에서 카드의 표시 랭크(마작=1, 일반=Rank). 봉황·용·개는 여기 안 들어옴.
        private static int RankOf(Card c) => c.Special == SpecialKind.Mahjong ? 1 : c.Rank;

        /// <summary>봉황이 트릭에서 대체하는 랭크(표시 정렬용). 봉황이 없거나 유도 불가면 -1.
        /// 스트레이트=빈 랭크, 연속페어=2장 미만 랭크, 페어/트리플=그 랭크, 풀하우스=부족한 쪽.</summary>
        public static int PhoenixRepRank(Combination top)
        {
            if (top == null) return -1;
            bool hasPhoenix = false;
            foreach (var c in top.Cards) if (c.Special == SpecialKind.Phoenix) { hasPhoenix = true; break; }
            if (!hasPhoenix) return -1;

            int topRank = top.Rank / 2;   // Rank 는 ×2 스케일
            switch (top.Type)
            {
                case CombinationType.Pair:
                case CombinationType.Triple:
                    return topRank;                       // 전부 같은 랭크

                case CombinationType.Straight:
                {
                    var have = new bool[16];
                    foreach (var c in top.Cards)
                        if (c.Special != SpecialKind.Phoenix) { int r = RankOf(c); if (r >= 0 && r < 16) have[r] = true; }
                    int lo = topRank - top.Length + 1;
                    for (int r = lo; r <= topRank; r++) if (r >= 0 && r < 16 && !have[r]) return r;
                    return topRank;
                }

                case CombinationType.ConsecutivePairs:
                {
                    var cnt = new int[16];
                    foreach (var c in top.Cards)
                        if (c.Special != SpecialKind.Phoenix) { int r = RankOf(c); if (r >= 0 && r < 16) cnt[r]++; }
                    int pairs = top.Length / 2;
                    int lo = topRank - pairs + 1;
                    for (int r = lo; r <= topRank; r++) if (r >= 0 && r < 16 && cnt[r] < 2) return r;
                    return topRank;
                }

                case CombinationType.FullHouse:
                {
                    var cnt = new int[16];
                    foreach (var c in top.Cards)
                        if (c.Special != SpecialKind.Phoenix) { int r = RankOf(c); if (r >= 0 && r < 16) cnt[r]++; }
                    if (topRank >= 0 && topRank < 16 && cnt[topRank] < 3) return topRank;   // 봉황이 트리플 보강
                    for (int r = 2; r < 15; r++) if (r != topRank && cnt[r] == 1) return r; // 페어 보강(자연 1장)
                    return topRank;
                }

                default:
                    return topRank;   // Single 등 — 단독이라 정렬 무의미
            }
        }

        /// <summary>트릭 표시 정렬키. 봉황은 대체 랭크 위치(+0.5, 같은 랭크 자연 뒤)에, 나머지는 SortKey.</summary>
        public static double TrickSortKey(Combination top, Card card)
        {
            if (card.Special == SpecialKind.Phoenix)
            {
                int rep = PhoenixRepRank(top);
                if (rep >= 0) return rep + 0.5;
            }
            return SortKey(card);
        }
    }
}
