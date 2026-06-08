using System;

namespace Tichu.Core.Cards
{
    /// <summary>카드 1장(값 타입). Rank: 일반 2~14(A=14), 마작=1, 용=15, 개/봉황=0(문맥의존).</summary>
    public readonly struct Card : IEquatable<Card>
    {
        public readonly int Rank;
        public readonly Suit Suit;
        public readonly SpecialKind Special;
        public readonly int Points;

        public Card(int rank, Suit suit, SpecialKind special, int points)
        {
            Rank = rank; Suit = suit; Special = special; Points = points;
        }

        public bool IsSpecial => Suit == Suit.Special;

        public static Card Normal(int rank, Suit suit) =>
            new Card(rank, suit, SpecialKind.None, PointsFor(rank));

        public static readonly Card Mahjong = new Card(1, Suit.Special, SpecialKind.Mahjong, 0);
        public static readonly Card Dog = new Card(0, Suit.Special, SpecialKind.Dog, 0);
        public static readonly Card Phoenix = new Card(0, Suit.Special, SpecialKind.Phoenix, -25);
        public static readonly Card Dragon = new Card(15, Suit.Special, SpecialKind.Dragon, 25);

        private static int PointsFor(int rank)
        {
            switch (rank)
            {
                case 13: return 10; // K
                case 10: return 10; // 10
                case 5: return 5;   // 5
                default: return 0;
            }
        }

        public bool Equals(Card o) => Rank == o.Rank && Suit == o.Suit && Special == o.Special;
        public override bool Equals(object? o) => o is Card c && Equals(c);
        public override int GetHashCode() => (Rank * 397) ^ ((int)Suit * 17) ^ (int)Special;

        public override string ToString() =>
            IsSpecial ? Special.ToString() : $"{Rank}{Suit.ToString()[0]}";
    }
}
