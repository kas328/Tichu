using Tichu.Core.Cards;

namespace Tichu.Presentation.Views
{
    /// <summary>
    /// 카드 1장의 표기(라벨/색/정렬/아틀라스 키) 포맷. RuntimeTableView·CardView 공용(DRY).
    /// </summary>
    public static class CardFormat
    {
        /// <summary>카드 면 라벨. 특수=한글, 일반="랭크\n무늬글리프".</summary>
        public static string Label(Card c)
        {
            switch (c.Special)
            {
                case SpecialKind.Dragon:  return "용";
                case SpecialKind.Phoenix: return "봉";
                case SpecialKind.Dog:     return "개";
                case SpecialKind.Mahjong: return "1";
                default: return $"{RankLabel(c.Rank)}\n{SuitGlyph(c.Suit)}";
            }
        }

        public static string RankLabel(int r)
        {
            switch (r) { case 14: return "A"; case 13: return "K"; case 12: return "Q"; case 11: return "J"; default: return r.ToString(); }
        }

        public static string SuitGlyph(Suit s)
        {
            switch (s) { case Suit.Jade: return "♣"; case Suit.Sword: return "♠"; case Suit.Pagoda: return "♦"; case Suit.Star: return "♥"; default: return ""; }
        }

        /// <summary>빨강 무늬(다이아/하트). 특수는 false.</summary>
        public static bool IsRed(Card c) => !c.IsSpecial && (c.Suit == Suit.Pagoda || c.Suit == Suit.Star);

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
    }
}
