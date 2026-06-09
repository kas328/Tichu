#nullable enable
using NUnit.Framework;
using System.Collections.Generic;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;

namespace Tichu.Core.Tests
{
    public class GameStateTests
    {
        // ── helpers ─────────────────────────────────────────────────────────────

        private static PlayerSeat MakeSeat(int index)
        {
            var s = new PlayerSeat { SeatIndex = index };
            return s;
        }

        private static GameState MakeState()
        {
            var seats = new PlayerSeat[4];
            for (int i = 0; i < 4; i++) seats[i] = MakeSeat(i);

            // give seat 0 two cards
            seats[0].Hand.Add(Card.Normal(5, Suit.Jade));
            seats[0].Hand.Add(Card.Normal(10, Suit.Star));
            seats[0].WonCards.Add(Card.Normal(13, Suit.Pagoda));

            var trick = new Trick();
            trick.History.Add(new Play
            {
                Seat = 1,
                Combination = new Combination(CombinationType.Single,
                    new[] { Card.Normal(7, Suit.Jade) }, 1, 14, 0),
                IsBombInterrupt = false
            });
            trick.AccumulatedPoints = 0;
            trick.LeadType = CombinationType.Single;
            trick.LeadLength = 1;

            var scores = new ScoreBoard();
            scores.TeamA = 100;
            scores.TeamB = 200;
            scores.Rounds.Add(new RoundResult
            {
                TeamACardPoints = 60,
                TeamBCardPoints = 40,
                TeamATichuDelta = 100,
                TeamBTichuDelta = 0,
                TeamATotal = 160,
                TeamBTotal = 40
            });

            return new GameState
            {
                Phase = RoundPhase.Play,
                Seats = seats,
                CurrentTrick = trick,
                Turn = 1,
                Wish = null,
                Scores = scores,
                RngSeed = 42UL,
                Rng = new Rng(42UL)
            };
        }

        // ── Clone tests ──────────────────────────────────────────────────────────

        [Test]
        public void Clone_produces_independent_deep_copy()
        {
            var original = MakeState();
            var clone = original.Clone();

            // Mutate original — clone must not change
            original.Seats[0].Hand.Add(Card.Normal(2, Suit.Jade));
            original.Scores.TeamA = 999;
            original.CurrentTrick!.History.Add(new Play { Seat = 2 });

            Assert.That(clone.Seats[0].Hand.Count, Is.EqualTo(2),
                "clone hand should still have 2 cards");
            Assert.That(clone.Scores.TeamA, Is.EqualTo(100),
                "clone TeamA score should be unchanged");
            Assert.That(clone.CurrentTrick!.History.Count, Is.EqualTo(1),
                "clone trick history should still have 1 entry");

            // Mutate clone — original must not change
            clone.Seats[1].Hand.Add(Card.Normal(3, Suit.Sword));
            clone.Scores.TeamB = 1;

            Assert.That(original.Seats[1].Hand.Count, Is.EqualTo(0),
                "original seat1 hand should be unaffected");
            Assert.That(original.Scores.TeamB, Is.EqualTo(200),
                "original TeamB score should be unchanged");
        }

        [Test]
        public void Clone_deep_copies_won_cards_and_rounds()
        {
            var original = MakeState();
            var clone = original.Clone();

            original.Seats[0].WonCards.Add(Card.Dragon);
            original.Scores.Rounds.Add(new RoundResult { TeamATotal = 50 });

            Assert.That(clone.Seats[0].WonCards.Count, Is.EqualTo(1),
                "clone won cards count should be unchanged");
            Assert.That(clone.Scores.Rounds.Count, Is.EqualTo(1),
                "clone rounds count should be unchanged");
        }

        [Test]
        public void Clone_field_level_independence_for_rounds_and_play()
        {
            var original = MakeState();
            var clone = original.Clone();

            // Mutate a field of the original's RoundResult — clone must not change
            original.Scores.Rounds[0].TeamATotal = 999;
            Assert.That(clone.Scores.Rounds[0].TeamATotal, Is.EqualTo(160),
                "clone RoundResult.TeamATotal must be independent of original");

            // Mutate a field of the original's Play — clone must not change
            original.CurrentTrick!.History[0].Seat = 3;
            Assert.That(clone.CurrentTrick!.History[0].Seat, Is.EqualTo(1),
                "clone Play.Seat must be independent of original");

            // And vice-versa: mutating clone does not affect original
            clone.Scores.Rounds[0].TeamBTotal = 777;
            Assert.That(original.Scores.Rounds[0].TeamBTotal, Is.EqualTo(40),
                "original RoundResult.TeamBTotal must be independent of clone");
        }

        [Test]
        public void ComputeHash_differs_on_target_score_change()
        {
            var s1 = MakeState();
            var s2 = MakeState();
            s2.Scores.TargetScore = 500;

            Assert.That(s1.ComputeHash(), Is.Not.EqualTo(s2.ComputeHash()),
                "states differing only in TargetScore must produce different hashes");
        }

        // ── ComputeHash tests ────────────────────────────────────────────────────

        [Test]
        public void ComputeHash_is_stable_for_equal_states()
        {
            var s1 = MakeState();
            var s2 = MakeState();

            Assert.That(s1.ComputeHash(), Is.EqualTo(s2.ComputeHash()),
                "identical states must produce the same hash");

            // Change one card in s2
            s2.Seats[0].Hand[0] = Card.Normal(6, Suit.Jade); // was 5J

            Assert.That(s1.ComputeHash(), Is.Not.EqualTo(s2.ComputeHash()),
                "states differing in one card must produce different hashes");
        }

        [Test]
        public void ComputeHash_differs_on_phase_change()
        {
            var s1 = MakeState();
            var s2 = MakeState();
            s2.Phase = RoundPhase.Scoring;

            Assert.That(s1.ComputeHash(), Is.Not.EqualTo(s2.ComputeHash()));
        }

        // ── Seating tests ────────────────────────────────────────────────────────

        [Test]
        public void Seating_team_and_partner()
        {
            Assert.That(Seating.TeamOf(0), Is.EqualTo(Seating.TeamOf(2)));
            Assert.That(Seating.TeamOf(1), Is.EqualTo(Seating.TeamOf(3)));
            Assert.That(Seating.TeamOf(0), Is.Not.EqualTo(Seating.TeamOf(1)));

            Assert.That(Seating.Partner(0), Is.EqualTo(2));
            Assert.That(Seating.Partner(1), Is.EqualTo(3));
            Assert.That(Seating.Partner(2), Is.EqualTo(0));
            Assert.That(Seating.Partner(3), Is.EqualTo(1));
        }

        [Test]
        public void NextActive_skips_out_seats()
        {
            var seats = new PlayerSeat[4];
            for (int i = 0; i < 4; i++) seats[i] = MakeSeat(i);

            seats[2].IsOut = true;
            Assert.That(Seating.NextActive(seats, 1), Is.EqualTo(3));

            seats[3].IsOut = true;
            Assert.That(Seating.NextActive(seats, 1), Is.EqualTo(0));
        }

        // ── GameAction factory tests ─────────────────────────────────────────────

        [Test]
        public void GameAction_factories_set_kind_and_seat()
        {
            var a = GameAction.CallGrandTichu(0);
            Assert.That(a.Kind, Is.EqualTo(GameActionKind.CallGrandTichu));
            Assert.That(a.Seat, Is.EqualTo(0));

            var b = GameAction.DeclineGrandTichu(2);
            Assert.That(b.Kind, Is.EqualTo(GameActionKind.DeclineGrandTichu));

            var c = GameAction.Pass(3);
            Assert.That(c.Kind, Is.EqualTo(GameActionKind.Pass));

            var cards = new[] { Card.Normal(5, Suit.Jade) };
            var d = GameAction.Play(1, cards);
            Assert.That(d.Kind, Is.EqualTo(GameActionKind.Play));
            Assert.That(d.Cards, Is.EqualTo(cards));
            Assert.That(d.Wish, Is.Null);

            var e = GameAction.Play(1, cards, 7);
            Assert.That(e.Wish, Is.EqualTo(7));
        }

        [Test]
        public void GameAction_exchange_stores_three_sets()
        {
            var left = new[] { Card.Normal(2, Suit.Jade) };
            var partner = new[] { Card.Normal(3, Suit.Sword) };
            var right = new[] { Card.Normal(4, Suit.Star) };
            var ex = GameAction.Exchange(0, left, partner, right);
            Assert.That(ex.Kind, Is.EqualTo(GameActionKind.Exchange));
            Assert.That(ex.ExchangeToLeft, Is.EqualTo(left));
            Assert.That(ex.ExchangePartner, Is.EqualTo(partner));
            Assert.That(ex.ExchangeToRight, Is.EqualTo(right));
        }

        // ── ApplyResult tests ────────────────────────────────────────────────────

        [Test]
        public void ApplyResult_accepted_and_reject()
        {
            var ok = ApplyResult.Accepted;
            Assert.That(ok.Ok, Is.True);
            Assert.That(ok.RejectReason, Is.Null);

            var no = ApplyResult.Reject("bad move");
            Assert.That(no.Ok, Is.False);
            Assert.That(no.RejectReason, Is.EqualTo("bad move"));
        }
    }
}
