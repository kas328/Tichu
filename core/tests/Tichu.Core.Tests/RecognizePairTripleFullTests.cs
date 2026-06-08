using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;

namespace Tichu.Core.Tests
{
    public class RecognizePairTripleFullTests
    {
        private static Combination R(params Card[] cards) =>
            CombinationRecognizer.Recognize(cards, TrickContext.Lead);

        [Test]
        public void Pair_of_same_rank()
        {
            var c = R(Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Star));
            Assert.That(c.Type, Is.EqualTo(CombinationType.Pair));
            Assert.That(c.Length, Is.EqualTo(2));
            Assert.That(c.Rank, Is.EqualTo(18)); // 9*2
        }

        [Test]
        public void Pair_with_phoenix()
        {
            var c = R(Card.Normal(9, Suit.Jade), Card.Phoenix);
            Assert.That(c.Type, Is.EqualTo(CombinationType.Pair));
            Assert.That(c.Rank, Is.EqualTo(18));
            Assert.That(c.PointsInPlay, Is.EqualTo(-25)); // 봉황 점수 유지
        }

        [Test]
        public void Triple_plain_and_with_phoenix()
        {
            Assert.That(R(Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Star), Card.Normal(7, Suit.Sword)).Type,
                        Is.EqualTo(CombinationType.Triple));
            var ph = R(Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Star), Card.Phoenix);
            Assert.That(ph.Type, Is.EqualTo(CombinationType.Triple));
            Assert.That(ph.Rank, Is.EqualTo(14)); // 7*2
        }

        [Test]
        public void FullHouse_rank_is_triple_rank()
        {
            var c = R(Card.Normal(8, Suit.Jade), Card.Normal(8, Suit.Star), Card.Normal(8, Suit.Sword),
                      Card.Normal(4, Suit.Jade), Card.Normal(4, Suit.Star));
            Assert.That(c.Type, Is.EqualTo(CombinationType.FullHouse));
            Assert.That(c.Length, Is.EqualTo(5));
            Assert.That(c.Rank, Is.EqualTo(16)); // 8*2 (트리플 기준)
        }

        [Test]
        public void FullHouse_with_phoenix_completing_the_pair()
        {
            // 8,8,8 + 4 + 봉황 → 봉황이 4의 짝 → 풀하우스(트리플 8)
            var c = R(Card.Normal(8, Suit.Jade), Card.Normal(8, Suit.Star), Card.Normal(8, Suit.Sword),
                      Card.Normal(4, Suit.Jade), Card.Phoenix);
            Assert.That(c.Type, Is.EqualTo(CombinationType.FullHouse));
            Assert.That(c.Rank, Is.EqualTo(16));
        }

        [Test]
        public void Mahjong_cannot_form_pair()
        {
            Assert.That(R(Card.Mahjong, Card.Phoenix).Type, Is.EqualTo(CombinationType.Invalid));
        }

        [Test]
        public void Two_different_ranks_is_not_a_pair()
        {
            Assert.That(R(Card.Normal(9, Suit.Jade), Card.Normal(8, Suit.Star)).Type,
                        Is.EqualTo(CombinationType.Invalid));
        }
    }
}
