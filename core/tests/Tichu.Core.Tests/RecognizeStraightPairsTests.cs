using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;

namespace Tichu.Core.Tests
{
    public class RecognizeStraightPairsTests
    {
        private static Combination R(params Card[] cards) =>
            CombinationRecognizer.Recognize(cards, TrickContext.Lead);

        private static Card N(int r) => Card.Normal(r, Suit.Jade);     // 문양 섞기용
        private static Card M(int r, Suit s) => Card.Normal(r, s);

        [Test]
        public void Straight_of_five_mixed_suits()
        {
            var c = R(M(5, Suit.Jade), M(6, Suit.Star), M(7, Suit.Sword), M(8, Suit.Pagoda), M(9, Suit.Jade));
            Assert.That(c.Type, Is.EqualTo(CombinationType.Straight));
            Assert.That(c.Length, Is.EqualTo(5));
            Assert.That(c.Rank, Is.EqualTo(18)); // top 9 *2
        }

        [Test]
        public void Straight_can_start_at_mahjong()
        {
            var c = R(Card.Mahjong, M(2, Suit.Jade), M(3, Suit.Star), M(4, Suit.Sword), M(5, Suit.Pagoda));
            Assert.That(c.Type, Is.EqualTo(CombinationType.Straight));
            Assert.That(c.Rank, Is.EqualTo(10)); // top 5 *2
        }

        [Test]
        public void Straight_with_phoenix_filling_internal_gap()
        {
            // 5,6,_,8,9 + 봉황 → 7 메움
            var c = R(M(5, Suit.Jade), M(6, Suit.Star), M(8, Suit.Sword), M(9, Suit.Pagoda), Card.Phoenix);
            Assert.That(c.Type, Is.EqualTo(CombinationType.Straight));
            Assert.That(c.Length, Is.EqualTo(5));
            Assert.That(c.Rank, Is.EqualTo(18)); // top 9 *2
        }

        [Test]
        public void Straight_with_phoenix_extending_top()
        {
            // 5,6,7,8 + 봉황 (gap 없음) → 9로 상단 확장
            var c = R(M(5, Suit.Jade), M(6, Suit.Star), M(7, Suit.Sword), M(8, Suit.Pagoda), Card.Phoenix);
            Assert.That(c.Type, Is.EqualTo(CombinationType.Straight));
            Assert.That(c.Rank, Is.EqualTo(18)); // 상단 확장 9 *2
        }

        [Test]
        public void Four_cards_is_too_short_for_straight()
        {
            Assert.That(R(M(5, Suit.Jade), M(6, Suit.Star), M(7, Suit.Sword), M(8, Suit.Pagoda)).Type,
                        Is.Not.EqualTo(CombinationType.Straight));
        }

        [Test]
        public void Straight_phoenix_extends_bottom_when_top_is_ace()
        {
            // 11,12,13,14 + 봉황: 상단(15) 불가 → 하단 확장(10) → 10~14, top=14
            var c = R(M(11, Suit.Jade), M(12, Suit.Star), M(13, Suit.Sword), M(14, Suit.Pagoda), Card.Phoenix);
            Assert.That(c.Type, Is.EqualTo(CombinationType.Straight));
            Assert.That(c.Length, Is.EqualTo(5));
            Assert.That(c.Rank, Is.EqualTo(28)); // top 14 *2
        }

        [Test]
        public void Straight_with_duplicate_rank_is_invalid()
        {
            Assert.That(R(M(5, Suit.Jade), M(5, Suit.Star), M(6, Suit.Sword), M(7, Suit.Pagoda), M(8, Suit.Jade)).Type,
                        Is.EqualTo(CombinationType.Invalid));
        }

        [Test]
        public void Consecutive_pairs_two()
        {
            var c = R(M(5, Suit.Jade), M(5, Suit.Star), M(6, Suit.Sword), M(6, Suit.Pagoda));
            Assert.That(c.Type, Is.EqualTo(CombinationType.ConsecutivePairs));
            Assert.That(c.Length, Is.EqualTo(4));
            Assert.That(c.Rank, Is.EqualTo(12)); // top pair 6 *2
        }

        [Test]
        public void Consecutive_pairs_with_phoenix()
        {
            // 5,5,6 + 봉황 → 봉황이 6의 짝 → (5,5)(6,6)
            var c = R(M(5, Suit.Jade), M(5, Suit.Star), M(6, Suit.Sword), Card.Phoenix);
            Assert.That(c.Type, Is.EqualTo(CombinationType.ConsecutivePairs));
            Assert.That(c.Rank, Is.EqualTo(12)); // top 6 *2
        }

        [Test]
        public void Non_consecutive_pairs_is_invalid()
        {
            Assert.That(R(M(5, Suit.Jade), M(5, Suit.Star), M(8, Suit.Sword), M(8, Suit.Pagoda)).Type,
                        Is.EqualTo(CombinationType.Invalid));
        }

        [Test]
        public void Consecutive_pairs_phoenix_cannot_fix_three_singles()
        {
            // 5,6,7 + 봉황: 봉황 1장으로 단수 3개를 페어로 못 만듦 → Invalid
            Assert.That(R(M(5, Suit.Jade), M(6, Suit.Star), M(7, Suit.Sword), Card.Phoenix).Type,
                        Is.EqualTo(CombinationType.Invalid));
        }

        [Test]
        public void Consecutive_pairs_phoenix_with_gap_is_invalid()
        {
            // 5,5,8 + 봉황: 봉황이 8을 페어로 만들어도 5와 8 사이가 비어 연속 아님 → Invalid
            Assert.That(R(M(5, Suit.Jade), M(5, Suit.Star), M(8, Suit.Sword), Card.Phoenix).Type,
                        Is.EqualTo(CombinationType.Invalid));
        }
    }
}
