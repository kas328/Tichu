using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;

namespace Tichu.Core.Tests
{
    /// <summary>TrickComparer 단위 테스트: ContextFor 및 Beats(candidate, trick) 검증.</summary>
    public class TrickCompareTests
    {
        // ---------- 헬퍼 ----------
        private static Combination Lead(params Card[] cards) =>
            CombinationRecognizer.Recognize(cards, TrickContext.Lead);

        /// <summary>Top을 지정해 트릭 객체를 만든다.</summary>
        private static Trick MakeTrick(Combination top, int seat = 0)
        {
            return new Trick
            {
                Top = top,
                LeadType = top.Type,
                LeadLength = top.Length,
                TopOwnerSeat = seat
            };
        }

        // ---------- ContextFor ----------

        [Test]
        public void ContextFor_lead_when_no_top()
        {
            var trick = new Trick(); // Top == null
            var ctx = TrickComparer.ContextFor(trick);
            Assert.That(ctx.IsLead, Is.True);
        }

        [Test]
        public void ContextFor_null_trick_returns_lead()
        {
#pragma warning disable CS8625
            var ctx = TrickComparer.ContextFor(null);
#pragma warning restore CS8625
            Assert.That(ctx.IsLead, Is.True);
        }

        [Test]
        public void ContextFor_single_top_sets_topIsSingle_and_rank()
        {
            // K 단독 (Rank = 13×2 = 26)
            var kSingle = Lead(Card.Normal(13, Suit.Jade));
            var trick = MakeTrick(kSingle);

            var ctx = TrickComparer.ContextFor(trick);

            Assert.That(ctx.IsLead, Is.False);
            Assert.That(ctx.TopIsSingle, Is.True);
            Assert.That(ctx.CurrentSingleRankScaled, Is.EqualTo(kSingle.Rank)); // 26
        }

        [Test]
        public void ContextFor_non_single_top_has_topIsSingle_false()
        {
            // 페어 9
            var pair = Lead(Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Star));
            var trick = MakeTrick(pair);

            var ctx = TrickComparer.ContextFor(trick);

            Assert.That(ctx.IsLead, Is.False);
            Assert.That(ctx.TopIsSingle, Is.False);
            Assert.That(ctx.CurrentSingleRankScaled, Is.EqualTo(0));
        }

        // ---------- Beats ----------

        [Test]
        public void Following_same_type_higher_rank_beats_top()
        {
            // Top: 페어 9 / 후보: 페어 J → true; 페어 7 → false
            var top9 = Lead(Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Star));
            var trick = MakeTrick(top9);

            var pairJ = Lead(Card.Normal(11, Suit.Jade), Card.Normal(11, Suit.Star));
            var pair7 = Lead(Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Star));

            Assert.That(TrickComparer.Beats(pairJ, trick), Is.True);
            Assert.That(TrickComparer.Beats(pair7, trick), Is.False);
        }

        [Test]
        public void Lower_or_different_type_does_not_beat()
        {
            // Top: 페어 9 / 후보: 단독 K → false (타입/장수 불일치)
            var top9 = Lead(Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Star));
            var trick = MakeTrick(top9);

            var singleK = Lead(Card.Normal(13, Suit.Jade));

            Assert.That(TrickComparer.Beats(singleK, trick), Is.False);
        }

        [Test]
        public void Bomb_interrupts_non_bomb_trick()
        {
            // Top: 페어 A (비폭탄) / 후보: 포카드 7 (FourBomb) → true
            var pairA = Lead(Card.Normal(14, Suit.Jade), Card.Normal(14, Suit.Star));
            var trick = MakeTrick(pairA);

            var bomb7 = Lead(
                Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Star),
                Card.Normal(7, Suit.Sword), Card.Normal(7, Suit.Pagoda));

            Assert.That(TrickComparer.Beats(bomb7, trick), Is.True);
        }

        [Test]
        public void Phoenix_single_uses_prev_single_rank_from_trick()
        {
            // Top: K 단독 (Rank=26). ContextFor → TopIsSingle=true, CurrentSingleRankScaled=26.
            // 그 문맥으로 봉황 단독 인식 → Rank=27(K+0.5).
            var kSingle = Lead(Card.Normal(13, Suit.Jade));
            var trick = MakeTrick(kSingle);

            var ctx = TrickComparer.ContextFor(trick);
            Assert.That(ctx.TopIsSingle, Is.True);
            Assert.That(ctx.CurrentSingleRankScaled, Is.EqualTo(26));

            var phoenixCombo = CombinationRecognizer.Recognize(new[] { Card.Phoenix }, ctx);
            Assert.That(phoenixCombo.Rank, Is.EqualTo(27));

            Assert.That(TrickComparer.Beats(phoenixCombo, trick), Is.True);
        }

        [Test]
        public void Phoenix_cannot_beat_dragon_single()
        {
            // Top: 용 단독 (Rank=30)
            var dragonSingle = Lead(Card.Dragon);
            var trick = MakeTrick(dragonSingle);

            var ctx = TrickComparer.ContextFor(trick);
            // 봉황 단독 인식: Rank = 30+1 = 31 (구조상)
            var phoenixCombo = CombinationRecognizer.Recognize(new[] { Card.Phoenix }, ctx);

            // CombinationComparer의 봉황-vs-용 가드로 false여야 함
            Assert.That(TrickComparer.Beats(phoenixCombo, trick), Is.False);
        }

        [Test]
        public void Beats_returns_false_for_null_or_invalid_candidate()
        {
            var pair = Lead(Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Star));
            var trick = MakeTrick(pair);

#pragma warning disable CS8625
            Assert.That(TrickComparer.Beats(null, trick), Is.False);
#pragma warning restore CS8625
            Assert.That(TrickComparer.Beats(Combination.Invalid, trick), Is.False);
        }

        [Test]
        public void Beats_returns_false_when_trick_has_no_top()
        {
            var trick = new Trick(); // Top == null (리드 상황)
            var pair = Lead(Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Star));
            Assert.That(TrickComparer.Beats(pair, trick), Is.False);
        }
    }
}
