using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.GameFlow;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// MoveOrder — AI 휴리스틱이 의존하는 결정적 정렬/선택 오라클 검증.
    /// Strength(높이), CheapestThatBeats(최소 오버킬), SmallestBomb, Lowest.
    /// </summary>
    public class MoveOrderTests
    {
        private static Combination Pair(int rank) =>
            CombinationRecognizer.Recognize(
                new[] { Card.Normal(rank, Suit.Jade), Card.Normal(rank, Suit.Sword) }, TrickContext.Lead);

        private static Combination Single(int rank) =>
            CombinationRecognizer.Recognize(new[] { Card.Normal(rank, Suit.Jade) }, TrickContext.Lead);

        private static Combination FourBomb(int rank) =>
            CombinationRecognizer.Recognize(new[]
            {
                Card.Normal(rank, Suit.Jade), Card.Normal(rank, Suit.Sword),
                Card.Normal(rank, Suit.Pagoda), Card.Normal(rank, Suit.Star)
            }, TrickContext.Lead);

        // ── Strength ────────────────────────────────────────────────────────────────

        [Test]
        public void Strength_orders_higher_pair_above_lower_pair()
        {
            Assert.That(MoveOrder.Strength(Pair(10)), Is.GreaterThan(MoveOrder.Strength(Pair(5))));
        }

        [Test]
        public void Strength_orders_higher_single_above_lower_single()
        {
            Assert.That(MoveOrder.Strength(Single(14)), Is.GreaterThan(MoveOrder.Strength(Single(2))));
        }

        // ── CheapestThatBeats: 최소 오버킬 ───────────────────────────────────────────

        [Test]
        public void CheapestThatBeats_picks_minimal_overkill()
        {
            // Top = 페어 7. 이길 수 있는 페어들 8/10/13 중 가장 낮은 8을 골라야 한다.
            var top = Pair(7);
            var moves = new List<Combination> { Pair(13), Pair(8), Pair(10) };

            var chosen = MoveOrder.CheapestThatBeats(moves, top);
            Assert.That(chosen, Is.Not.Null);
            Assert.That(chosen!.Rank, Is.EqualTo(Pair(8).Rank));
        }

        [Test]
        public void CheapestThatBeats_returns_null_when_nothing_beats()
        {
            var top = Pair(13);
            var moves = new List<Combination> { Pair(5), Pair(7) };
            Assert.That(MoveOrder.CheapestThatBeats(moves, top), Is.Null);
        }

        // ── SmallestBomb ────────────────────────────────────────────────────────────

        [Test]
        public void SmallestBomb_selects_lowest_strength_bomb()
        {
            var bombs = new List<Combination> { FourBomb(11), FourBomb(4), FourBomb(8) };
            var chosen = MoveOrder.Smallest(bombs);
            Assert.That(chosen, Is.Not.Null);
            Assert.That(chosen!.Rank, Is.EqualTo(FourBomb(4).Rank));
        }

        // ── Lowest (lead 선택용) ─────────────────────────────────────────────────────

        [Test]
        public void Lowest_prefers_low_strength()
        {
            var moves = new List<Combination> { Single(9), Single(3), Single(14) };
            var chosen = MoveOrder.Lowest(moves);
            Assert.That(chosen, Is.Not.Null);
            Assert.That(chosen!.Rank, Is.EqualTo(Single(3).Rank));
        }
    }
}
