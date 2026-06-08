using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;

namespace Tichu.Core.Tests
{
    public class RecognizeBombTests
    {
        private static Combination R(params Card[] cards) =>
            CombinationRecognizer.Recognize(cards, TrickContext.Lead);

        [Test]
        public void Four_of_a_kind_is_a_bomb()
        {
            var c = R(Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Star),
                      Card.Normal(7, Suit.Sword), Card.Normal(7, Suit.Pagoda));
            Assert.That(c.Type, Is.EqualTo(CombinationType.FourBomb));
            Assert.That(c.IsBomb, Is.True);
            Assert.That(c.Rank, Is.EqualTo(14)); // 7*2
        }

        [Test]
        public void Straight_flush_is_a_bomb_and_beats_plain_straight_recognition()
        {
            // 같은 문양(Jade) 5,6,7,8,9 → 스트레이트 플러시 (일반 스트레이트로 인식되면 안 됨)
            var c = R(Card.Normal(5, Suit.Jade), Card.Normal(6, Suit.Jade), Card.Normal(7, Suit.Jade),
                      Card.Normal(8, Suit.Jade), Card.Normal(9, Suit.Jade));
            Assert.That(c.Type, Is.EqualTo(CombinationType.StraightFlushBomb));
            Assert.That(c.Length, Is.EqualTo(5));
            Assert.That(c.Rank, Is.EqualTo(18)); // top 9 *2
        }

        [Test]
        public void Phoenix_cannot_make_a_bomb()
        {
            // 7,7,7 + 봉황 → 포카드 폭탄 아님(트리플로 인식)
            var c = R(Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Star),
                      Card.Normal(7, Suit.Sword), Card.Phoenix);
            Assert.That(c.Type, Is.Not.EqualTo(CombinationType.FourBomb));
            // 같은 문양 연속 + 봉황 → 스트레이트 플러시 아님(일반 스트레이트)
            var sf = R(Card.Normal(5, Suit.Jade), Card.Normal(6, Suit.Jade), Card.Normal(7, Suit.Jade),
                       Card.Normal(8, Suit.Jade), Card.Phoenix);
            Assert.That(sf.Type, Is.EqualTo(CombinationType.Straight));
            Assert.That(sf.Type, Is.Not.EqualTo(CombinationType.StraightFlushBomb));
        }
    }
}
