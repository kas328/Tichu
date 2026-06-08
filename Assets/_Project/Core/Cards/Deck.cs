using System.Collections.Generic;
using Tichu.Core;

namespace Tichu.Core.Cards
{
    public static class Deck
    {
        public const int Size = 56;

        public static List<Card> CreateStandard()
        {
            var cards = new List<Card>(Size);
            foreach (var suit in new[] { Suit.Jade, Suit.Sword, Suit.Pagoda, Suit.Star })
                for (int r = 2; r <= 14; r++)
                    cards.Add(Card.Normal(r, suit));
            cards.Add(Card.Mahjong);
            cards.Add(Card.Dog);
            cards.Add(Card.Phoenix);
            cards.Add(Card.Dragon);
            return cards;
        }

        /// <summary>Fisher–Yates 셔플(in-place). 동일 시드 → 동일 순서.</summary>
        public static void Shuffle(IList<Card> cards, ref Rng rng)
        {
            for (int i = cards.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                Card tmp = cards[i]; cards[i] = cards[j]; cards[j] = tmp;
            }
        }
    }
}
