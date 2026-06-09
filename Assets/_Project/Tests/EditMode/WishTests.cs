#nullable enable
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;

namespace Tichu.Core.Tests
{
    /// <summary>소원(Wish) 강제·해제 검증 (기획서 11.7).</summary>
    public class WishTests
    {
        private static List<Card> Hand(params Card[] cards) => new List<Card>(cards);

        private static GameState LeadState(int turn, params List<Card>[] hands)
        {
            var s = new GameState { Phase = RoundPhase.Play, Turn = turn, CurrentTrick = null };
            for (int i = 0; i < 4; i++)
            {
                s.Seats[i] = new PlayerSeat { SeatIndex = i };
                s.Seats[i].Hand.AddRange(hands[i]);
            }
            return s;
        }

        private static bool IncludesRank(Combination m, int rank) =>
            m.Cards.Any(c => !c.IsSpecial && c.Rank == rank) ||
            m.Cards.Any(c => c.Special == SpecialKind.Mahjong && rank == 1);

        // ── 강제(만족 가능) ─────────────────────────────────────────────────────

        [Test]
        public void Wish_forces_inclusion_when_satisfiable_on_lead()
        {
            // 소원 7. 손에 7이 있다 → 리드 시 7 포함 수만 합법.
            var s = LeadState(0,
                Hand(Card.Normal(7, Suit.Jade), Card.Normal(9, Suit.Star), Card.Normal(13, Suit.Pagoda)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));
            s.Wish = 7;

            var moves = LegalMoveGenerator.LegalMoves(s, 0);
            Assert.That(moves.Count, Is.GreaterThan(0));
            Assert.That(moves.All(m => IncludesRank(m, 7)), Is.True, "all legal leads include a 7");
        }

        [Test]
        public void Wish_forces_inclusion_when_following_no_pass()
        {
            // Top = 5단독. seat1에 7단독(이김) + 7포함 외 다른 수.
            var s = LeadState(0,
                Hand(Card.Normal(5, Suit.Jade)),
                Hand(Card.Normal(7, Suit.Sword), Card.Normal(9, Suit.Star), Card.Normal(2, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)));
            Assert.That(GameEngine.Apply(s, GameAction.Play(0, new List<Card> { Card.Normal(5, Suit.Jade) })).Ok, Is.True);
            s.Wish = 7;

            Assert.That(s.Turn, Is.EqualTo(1));
            var moves = LegalMoveGenerator.LegalMoves(s, 1);
            Assert.That(moves.Count, Is.GreaterThan(0));
            Assert.That(moves.All(m => IncludesRank(m, 7)), Is.True);
            Assert.That(LegalMoveGenerator.CanPass(s, 1), Is.False, "must satisfy wish, cannot pass");
        }

        // ── 강제 안됨(만족 불가) ─────────────────────────────────────────────────

        [Test]
        public void Wish_not_forced_when_unsatisfiable()
        {
            // 소원 7. 손에 7 없음 → 평소대로.
            var s = LeadState(0,
                Hand(Card.Normal(9, Suit.Star), Card.Normal(13, Suit.Pagoda)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));
            s.Wish = 7;

            var moves = LegalMoveGenerator.LegalMoves(s, 0);
            Assert.That(moves.Count, Is.GreaterThan(0));
            Assert.That(moves.Any(m => !IncludesRank(m, 7)), Is.True, "no restriction when wish unsatisfiable");
        }

        [Test]
        public void Wish_not_forced_when_following_cannot_beat_with_wished_rank()
        {
            // Top = K단독. seat1에 7단독(못이김) + A단독(이김). 7로는 이길 수 없으므로 강제 없음.
            var s = LeadState(0,
                Hand(Card.Normal(13, Suit.Jade)),
                Hand(Card.Normal(7, Suit.Sword), Card.Normal(14, Suit.Star), Card.Normal(2, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)));
            Assert.That(GameEngine.Apply(s, GameAction.Play(0, new List<Card> { Card.Normal(13, Suit.Jade) })).Ok, Is.True);
            s.Wish = 7;

            Assert.That(s.Turn, Is.EqualTo(1));
            var moves = LegalMoveGenerator.LegalMoves(s, 1);
            // 7 단독은 K를 못 이기므로 합법수 없음 → 강제 불가 → A단독 등 평소대로 + 패스 가능.
            Assert.That(moves.Any(m => m.Type == CombinationType.Single && m.Rank == 14 * 2), Is.True);
            Assert.That(LegalMoveGenerator.CanPass(s, 1), Is.True, "wish unsatisfiable → pass allowed");
        }

        // ── 해제 ────────────────────────────────────────────────────────────────

        [Test]
        public void Wish_cleared_after_satisfying_play()
        {
            var s = LeadState(0,
                Hand(Card.Normal(7, Suit.Jade), Card.Normal(9, Suit.Star)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));
            s.Wish = 7;

            var r = GameEngine.Apply(s, GameAction.Play(0, new List<Card> { Card.Normal(7, Suit.Jade) }));
            Assert.That(r.Ok, Is.True);
            Assert.That(s.Wish, Is.Null, "wish cleared once satisfied");
        }

        [Test]
        public void Wish_not_cleared_when_play_does_not_include_wished_rank_and_unsatisfiable()
        {
            // 소원 7, 손에 7 없음 → 9 단독 내도 무방, 소원은 그대로 유지.
            var s = LeadState(0,
                Hand(Card.Normal(9, Suit.Star), Card.Normal(13, Suit.Pagoda)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));
            s.Wish = 7;

            var r = GameEngine.Apply(s, GameAction.Play(0, new List<Card> { Card.Normal(9, Suit.Star) }));
            Assert.That(r.Ok, Is.True);
            Assert.That(s.Wish, Is.EqualTo(7), "wish persists until satisfied");
        }

        // ── Apply 강제(IsLegal ⟺ Apply) ─────────────────────────────────────────

        [Test]
        public void Apply_rejects_play_violating_enforceable_wish()
        {
            // 소원 7, 손에 7 있음 → 7 없는 수(9 단독)는 Apply가 거부해야 한다.
            var s = LeadState(0,
                Hand(Card.Normal(7, Suit.Jade), Card.Normal(9, Suit.Star)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));
            s.Wish = 7;

            var r = GameEngine.Apply(s, GameAction.Play(0, new List<Card> { Card.Normal(9, Suit.Star) }));
            Assert.That(r.Ok, Is.False, "non-7 play rejected when 7 is playable");
            Assert.That(s.Wish, Is.EqualTo(7), "rejected play leaves wish intact");
        }

        [Test]
        public void Apply_rejects_pass_violating_enforceable_wish()
        {
            // Top = 5단독. seat1 손에 7단독(이김) → 패스 불가.
            var s = LeadState(0,
                Hand(Card.Normal(5, Suit.Jade)),
                Hand(Card.Normal(7, Suit.Sword), Card.Normal(2, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)));
            Assert.That(GameEngine.Apply(s, GameAction.Play(0, new List<Card> { Card.Normal(5, Suit.Jade) })).Ok, Is.True);
            s.Wish = 7;

            var r = GameEngine.Apply(s, GameAction.Pass(1));
            Assert.That(r.Ok, Is.False, "pass rejected when wish satisfiable");
        }

        [Test]
        public void Phoenix_alone_does_not_satisfy_wish()
        {
            // 봉황은 소원 랭크로 간주되지 않는다.
            // 손: Phoenix + 9(소원 7 아님) → 소원 7 강제 불가 → Phoenix 단독도 합법수에 포함.
            var s = LeadState(0,
                Hand(Card.Phoenix, Card.Normal(9, Suit.Star)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));
            s.Wish = 7;

            var moves = LegalMoveGenerator.LegalMoves(s, 0);

            // 소원 만족 가능한 수가 없으므로 강제 없음 → Phoenix 단독이 합법수에 있어야 한다.
            Assert.That(moves.Any(m => m.Type == CombinationType.Single && m.Cards[0].Special == SpecialKind.Phoenix),
                Is.True, "Phoenix single is legal when wish cannot be satisfied");

            // Phoenix 단독이 IsLegal 통과.
            var phoenixSingle = moves.First(m => m.Type == CombinationType.Single && m.Cards[0].Special == SpecialKind.Phoenix);
            Assert.That(LegalMoveGenerator.IsLegal(s, 0, phoenixSingle, out var reason), Is.True,
                $"Phoenix single should be legal: {reason}");

            // Apply도 수락.
            var clone = s.Clone();
            var r = GameEngine.Apply(clone, GameAction.Play(0, new List<Card> { Card.Phoenix }));
            Assert.That(r.Ok, Is.True, "Apply accepts Phoenix single when wish is unsatisfiable");
        }

        [Test]
        public void Wish_cleared_by_bomb_interrupt_including_wished_rank()
        {
            // Top = A 단독(seat0). seat2가 7포카드 폭탄(턴 아님)으로 인터럽트 → 소원7 해제.
            var s = LeadState(0,
                Hand(Card.Normal(14, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Sword),
                     Card.Normal(7, Suit.Pagoda), Card.Normal(7, Suit.Star)),
                Hand(Card.Normal(4, Suit.Jade)));
            Assert.That(GameEngine.Apply(s, GameAction.Play(0, new List<Card> { Card.Normal(14, Suit.Jade) })).Ok, Is.True);
            s.Wish = 7;

            var r = GameEngine.Apply(s, GameAction.Play(2, new List<Card>
            {
                Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Sword),
                Card.Normal(7, Suit.Pagoda), Card.Normal(7, Suit.Star)
            }));
            Assert.That(r.Ok, Is.True);
            Assert.That(s.Wish, Is.Null, "bomb satisfying the wish clears it");
        }
    }
}
