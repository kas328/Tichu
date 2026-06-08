using NUnit.Framework;
using Tichu.Core.Cards;

namespace Tichu.Core.Tests
{
    public class CardTests
    {
        [Test]
        public void Normal_card_points_are_correct()
        {
            Assert.That(Card.Normal(13, Suit.Jade).Points, Is.EqualTo(10)); // K
            Assert.That(Card.Normal(10, Suit.Star).Points, Is.EqualTo(10)); // 10
            Assert.That(Card.Normal(5, Suit.Sword).Points, Is.EqualTo(5));  // 5
            Assert.That(Card.Normal(14, Suit.Pagoda).Points, Is.EqualTo(0)); // A
            Assert.That(Card.Normal(2, Suit.Jade).Points, Is.EqualTo(0));
        }

        [Test]
        public void Special_cards_have_correct_meta()
        {
            Assert.That(Card.Dragon.Points, Is.EqualTo(25));
            Assert.That(Card.Phoenix.Points, Is.EqualTo(-25));
            Assert.That(Card.Mahjong.Points, Is.EqualTo(0));
            Assert.That(Card.Dog.Points, Is.EqualTo(0));
            Assert.That(Card.Mahjong.Rank, Is.EqualTo(1));
            Assert.That(Card.Dragon.Rank, Is.EqualTo(15));
            Assert.That(Card.Phoenix.IsSpecial, Is.True);
            Assert.That(Card.Normal(7, Suit.Jade).IsSpecial, Is.False);
        }

        [Test]
        public void Equality_compares_rank_suit_special()
        {
            var sevenJade = Card.Normal(7, Suit.Jade);
            var sameValue = Card.Normal(7, Suit.Jade); // 별개 인스턴스, 값 동일
            Assert.That(sevenJade, Is.EqualTo(sameValue));
            Assert.That(Card.Normal(7, Suit.Jade), Is.Not.EqualTo(Card.Normal(7, Suit.Star)));
            Assert.That(Card.Dragon, Is.Not.EqualTo(Card.Phoenix));
        }
    }
}
