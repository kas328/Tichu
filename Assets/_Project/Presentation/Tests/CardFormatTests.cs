using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Presentation.Views;

namespace Tichu.Presentation.Tests
{
    public class CardFormatTests
    {
        [Test] public void Normal_card_label_has_rank_and_suit()
            => Assert.AreEqual("A\n♥", CardFormat.Label(Card.Normal(14, Suit.Star)));

        [Test] public void Special_card_label_is_korean()
            => Assert.AreEqual("용", CardFormat.Label(Card.Dragon));

        [Test] public void Red_suits_are_pagoda_and_star()
        {
            Assert.IsTrue(CardFormat.IsRed(Card.Normal(5, Suit.Star)));
            Assert.IsFalse(CardFormat.IsRed(Card.Normal(5, Suit.Jade)));
        }

        [Test] public void Key_is_distinct_per_card()
            => Assert.AreNotEqual(
                CardFormat.Key(Card.Normal(5, Suit.Star)),
                CardFormat.Key(Card.Normal(5, Suit.Jade)));
    }
}
