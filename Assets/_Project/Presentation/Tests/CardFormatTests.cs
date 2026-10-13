using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Presentation.Views;

namespace Tichu.Presentation.Tests
{
    public class CardFormatTests
    {
        [Test] public void Normal_card_label_has_rank_and_korean_suit()
            => Assert.AreEqual("A\n별", CardFormat.Label(Card.Normal(14, Suit.Star)));

        // Android 폰트에 ♠♣♥♦ 가 없어 무늬가 사라졌다 — 확실히 렌더되는 한글 한 글자로.
        [Test] public void Suit_names_are_one_korean_letter_each()
        {
            Assert.AreEqual("옥", CardFormat.SuitName(Suit.Jade));
            Assert.AreEqual("검", CardFormat.SuitName(Suit.Sword));
            Assert.AreEqual("탑", CardFormat.SuitName(Suit.Pagoda));
            Assert.AreEqual("별", CardFormat.SuitName(Suit.Star));
        }

        [Test] public void Phoenix_label_is_full_name()
            => Assert.AreEqual("봉황", CardFormat.Label(Card.Phoenix));

        [Test] public void Special_card_label_is_korean()
            => Assert.AreEqual("용", CardFormat.Label(Card.Dragon));

        [Test] public void Icon_key_is_suit_for_normal_and_kind_for_special()
        {
            Assert.AreEqual("Star", CardFormat.IconKey(Card.Normal(5, Suit.Star)));
            Assert.AreEqual("Jade", CardFormat.IconKey(Card.Normal(9, Suit.Jade)));
            Assert.AreEqual("Dragon", CardFormat.IconKey(Card.Dragon));
            Assert.AreEqual("Mahjong", CardFormat.IconKey(Card.Mahjong));
        }

        [Test] public void Key_is_distinct_per_card()
            => Assert.AreNotEqual(
                CardFormat.Key(Card.Normal(5, Suit.Star)),
                CardFormat.Key(Card.Normal(5, Suit.Jade)));
    }
}
