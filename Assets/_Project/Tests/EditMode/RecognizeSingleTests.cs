using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;

namespace Tichu.Core.Tests
{
    public class RecognizeSingleTests
    {
        private static Combination R(TrickContext ctx, params Card[] cards) =>
            CombinationRecognizer.Recognize(cards, ctx);

        [Test]
        public void Normal_single_rank_is_value_times_two()
        {
            var c = R(TrickContext.Lead, Card.Normal(13, Suit.Jade)); // K
            Assert.That(c.Type, Is.EqualTo(CombinationType.Single));
            Assert.That(c.Length, Is.EqualTo(1));
            Assert.That(c.Rank, Is.EqualTo(26)); // 13*2
        }

        [Test]
        public void Mahjong_and_dragon_singles_have_extreme_ranks()
        {
            Assert.That(R(TrickContext.Lead, Card.Mahjong).Rank, Is.EqualTo(2));   // 1*2
            Assert.That(R(TrickContext.Lead, Card.Dragon).Rank, Is.EqualTo(30));   // 15*2
        }

        [Test]
        public void Phoenix_lead_single_is_one_and_half()
        {
            Assert.That(R(TrickContext.Lead, Card.Phoenix).Rank, Is.EqualTo(3));   // 1.5*2
        }

        [Test]
        public void Phoenix_following_single_is_half_above_top()
        {
            // 직전 단독이 K(26). 봉황 = 26+1 = 27 (=13.5)
            var ctx = new TrickContext(false, true, 26);
            Assert.That(R(ctx, Card.Phoenix).Rank, Is.EqualTo(27));
        }

        [Test]
        public void Dog_single_is_recognized_structurally()
        {
            var c = R(TrickContext.Lead, Card.Dog);
            Assert.That(c.Type, Is.EqualTo(CombinationType.Single)); // 흐름 레이어(Part 2)가 특수 처리
        }

        [Test]
        public void Empty_is_invalid()
        {
            Assert.That(CombinationRecognizer.Recognize(System.Array.Empty<Card>(), TrickContext.Lead).Type,
                        Is.EqualTo(CombinationType.Invalid));
        }
    }
}
