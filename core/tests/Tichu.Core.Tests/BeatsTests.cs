using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;

namespace Tichu.Core.Tests
{
    public class BeatsTests
    {
        private static Combination Single(Card c, TrickContext ctx) =>
            CombinationRecognizer.Recognize(new[] { c }, ctx);
        private static Combination Lead(params Card[] cards) =>
            CombinationRecognizer.Recognize(cards, TrickContext.Lead);

        [Test]
        public void Higher_same_type_beats_lower()
        {
            var top = Lead(Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Star));   // 페어 9
            var cand = Lead(Card.Normal(11, Suit.Jade), Card.Normal(11, Suit.Star)); // 페어 J
            Assert.That(CombinationComparer.Beats(cand, top), Is.True);
            Assert.That(CombinationComparer.Beats(top, cand), Is.False);
        }

        [Test]
        public void Different_type_or_length_does_not_beat()
        {
            var top = Lead(Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Star));    // 페어
            var cand = Lead(Card.Normal(13, Suit.Jade));                              // 단독
            Assert.That(CombinationComparer.Beats(cand, top), Is.False);
        }

        [Test]
        public void Four_bomb_beats_non_bomb_and_higher_bomb_wins()
        {
            var pair = Lead(Card.Normal(14, Suit.Jade), Card.Normal(14, Suit.Star)); // 페어 A
            var bomb7 = Lead(Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Star),
                             Card.Normal(7, Suit.Sword), Card.Normal(7, Suit.Pagoda));
            var bomb9 = Lead(Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Star),
                             Card.Normal(9, Suit.Sword), Card.Normal(9, Suit.Pagoda));
            Assert.That(CombinationComparer.Beats(bomb7, pair), Is.True);
            Assert.That(CombinationComparer.Beats(bomb9, bomb7), Is.True);
            Assert.That(CombinationComparer.Beats(bomb7, bomb9), Is.False);
        }

        [Test]
        public void Straight_flush_beats_four_bomb_and_longer_sf_wins()
        {
            var four = Lead(Card.Normal(14, Suit.Jade), Card.Normal(14, Suit.Star),
                            Card.Normal(14, Suit.Sword), Card.Normal(14, Suit.Pagoda));
            var sf5 = Lead(Card.Normal(5, Suit.Jade), Card.Normal(6, Suit.Jade), Card.Normal(7, Suit.Jade),
                           Card.Normal(8, Suit.Jade), Card.Normal(9, Suit.Jade));
            var sf6 = Lead(Card.Normal(5, Suit.Star), Card.Normal(6, Suit.Star), Card.Normal(7, Suit.Star),
                           Card.Normal(8, Suit.Star), Card.Normal(9, Suit.Star), Card.Normal(10, Suit.Star));
            Assert.That(CombinationComparer.Beats(sf5, four), Is.True);
            Assert.That(CombinationComparer.Beats(sf6, sf5), Is.True);   // 더 긺
            Assert.That(CombinationComparer.Beats(sf5, sf6), Is.False);
        }

        [Test]
        public void Dragon_is_highest_single_and_phoenix_cannot_beat_it()
        {
            var dragon = Single(Card.Dragon, TrickContext.Lead);                  // Rank 30
            var aceTop = new TrickContext(false, true, 28);                       // 직전 A(28)
            var phoenixOverAce = Single(Card.Phoenix, aceTop);                    // 29
            Assert.That(CombinationComparer.Beats(dragon, phoenixOverAce), Is.True);

            var dragonTop = new TrickContext(false, true, 30);
            var phoenixOverDragon = Single(Card.Phoenix, dragonTop);             // 구조상 31이지만
            Assert.That(CombinationComparer.Beats(phoenixOverDragon, dragon), Is.False); // 용은 못 이김
        }

        [Test]
        public void Phoenix_beats_a_normal_single_by_half()
        {
            var kingTop = new TrickContext(false, true, 26);                      // K
            var phoenix = Single(Card.Phoenix, kingTop);                          // 27 (13.5)
            var king = Single(Card.Normal(13, Suit.Jade), TrickContext.Lead);    // 26
            Assert.That(CombinationComparer.Beats(phoenix, king), Is.True);
        }
    }
}
