using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;

namespace Tichu.Core.Tests
{
    public class BombScannerTests
    {
        private static List<Card> Hand(params Card[] cards) => new List<Card>(cards);

        [Test]
        public void Four_of_a_kind_marks_all_four()
        {
            var hand = Hand(Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Sword),
                            Card.Normal(7, Suit.Pagoda), Card.Normal(7, Suit.Star), Card.Normal(2, Suit.Jade));
            var bombs = BombScanner.BombCards(hand);
            Assert.AreEqual(4, bombs.Count);
            Assert.IsTrue(bombs.Contains(Card.Normal(7, Suit.Jade)));
            Assert.IsFalse(bombs.Contains(Card.Normal(2, Suit.Jade)));
        }

        [Test]
        public void No_bomb_returns_empty()
        {
            var hand = Hand(Card.Normal(7, Suit.Jade), Card.Normal(8, Suit.Sword), Card.Normal(9, Suit.Pagoda));
            Assert.AreEqual(0, BombScanner.BombCards(hand).Count);
        }

        [Test]
        public void Straight_flush_five_marks_all_five()
        {
            var hand = Hand(Card.Normal(3, Suit.Star), Card.Normal(4, Suit.Star), Card.Normal(5, Suit.Star),
                            Card.Normal(6, Suit.Star), Card.Normal(7, Suit.Star), Card.Normal(9, Suit.Jade));
            var bombs = BombScanner.BombCards(hand);
            Assert.AreEqual(5, bombs.Count);
            Assert.IsFalse(bombs.Contains(Card.Normal(9, Suit.Jade)));
        }

        [Test]
        public void Straight_flush_six_marks_all_six()
        {
            var hand = Hand(Card.Normal(3, Suit.Star), Card.Normal(4, Suit.Star), Card.Normal(5, Suit.Star),
                            Card.Normal(6, Suit.Star), Card.Normal(7, Suit.Star), Card.Normal(8, Suit.Star));
            Assert.AreEqual(6, BombScanner.BombCards(hand).Count);
        }

        [Test]
        public void Four_consecutive_same_suit_is_not_bomb()
        {
            var hand = Hand(Card.Normal(3, Suit.Star), Card.Normal(4, Suit.Star),
                            Card.Normal(5, Suit.Star), Card.Normal(6, Suit.Star));
            Assert.AreEqual(0, BombScanner.BombCards(hand).Count);
        }

        [Test]
        public void Phoenix_never_in_bomb()
        {
            var hand = Hand(Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Sword),
                            Card.Normal(7, Suit.Pagoda), Card.Phoenix);
            var bombs = BombScanner.BombCards(hand);
            Assert.IsFalse(bombs.Contains(Card.Phoenix));
            Assert.AreEqual(0, bombs.Count);
        }

        [Test]
        public void Card_in_both_four_and_straightflush_counted_once()
        {
            var hand = Hand(
                Card.Normal(5, Suit.Star), Card.Normal(5, Suit.Jade), Card.Normal(5, Suit.Sword), Card.Normal(5, Suit.Pagoda),
                Card.Normal(3, Suit.Star), Card.Normal(4, Suit.Star), Card.Normal(6, Suit.Star), Card.Normal(7, Suit.Star));
            var bombs = BombScanner.BombCards(hand);
            Assert.AreEqual(8, bombs.Count);
            Assert.IsTrue(bombs.Contains(Card.Normal(5, Suit.Star)));
        }

        [Test]
        public void Mahjong_not_in_straight_flush()
        {
            var hand = Hand(Card.Mahjong, Card.Normal(2, Suit.Star), Card.Normal(3, Suit.Star),
                            Card.Normal(4, Suit.Star), Card.Normal(5, Suit.Star));
            Assert.AreEqual(0, BombScanner.BombCards(hand).Count);
        }
    }
}
