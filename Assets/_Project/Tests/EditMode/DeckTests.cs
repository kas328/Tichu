using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Tichu.Core;
using Tichu.Core.Cards;

namespace Tichu.Core.Tests
{
    public class DeckTests
    {
        [Test]
        public void Standard_deck_has_56_cards_and_100_points()
        {
            var deck = Deck.CreateStandard();
            Assert.That(deck.Count, Is.EqualTo(56));
            Assert.That(deck.Sum(c => c.Points), Is.EqualTo(100));
        }

        [Test]
        public void Standard_deck_has_52_normals_and_4_specials()
        {
            var deck = Deck.CreateStandard();
            Assert.That(deck.Count(c => !c.IsSpecial), Is.EqualTo(52));
            Assert.That(deck.Count(c => c.Special == SpecialKind.Mahjong), Is.EqualTo(1));
            Assert.That(deck.Count(c => c.Special == SpecialKind.Dog), Is.EqualTo(1));
            Assert.That(deck.Count(c => c.Special == SpecialKind.Phoenix), Is.EqualTo(1));
            Assert.That(deck.Count(c => c.Special == SpecialKind.Dragon), Is.EqualTo(1));
            // 각 문양 13랭크씩
            foreach (var suit in new[] { Suit.Jade, Suit.Sword, Suit.Pagoda, Suit.Star })
                Assert.That(deck.Count(c => !c.IsSpecial && c.Suit == suit), Is.EqualTo(13));
        }

        [Test]
        public void Shuffle_is_deterministic_for_same_seed()
        {
            var d1 = Deck.CreateStandard(); var r1 = new Rng(777UL); Deck.Shuffle(d1, ref r1);
            var d2 = Deck.CreateStandard(); var r2 = new Rng(777UL); Deck.Shuffle(d2, ref r2);
            Assert.That(d1, Is.EqualTo(d2)); // 순서까지 동일
        }

        [Test]
        public void Shuffle_differs_for_different_seed_but_preserves_multiset()
        {
            var d1 = Deck.CreateStandard(); var r1 = new Rng(1UL); Deck.Shuffle(d1, ref r1);
            var d2 = Deck.CreateStandard(); var r2 = new Rng(2UL); Deck.Shuffle(d2, ref r2);
            Assert.That(d1, Is.Not.EqualTo(d2));                 // 순서는 다름
            Assert.That(d1.OrderBy(C).ToList(),
                        Is.EqualTo(d2.OrderBy(C).ToList()));     // 구성은 동일
        }

        private static int C(Card c) => ((int)c.Suit << 8) | (c.Rank << 3) | (int)c.Special;
    }
}
