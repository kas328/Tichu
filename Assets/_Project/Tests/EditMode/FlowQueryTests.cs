using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;
using Tichu.GameFlow;

namespace Tichu.Core.Tests
{
    /// <summary>FlowQuery — 다음 단계 디스패치 + 폭탄 창 시계 순 열거 검증.</summary>
    public class FlowQueryTests
    {
        // ── 헬퍼 ────────────────────────────────────────────────────────────────

        private static GameState PlayState(int turn, params List<Card>[] hands)
        {
            var s = new GameState
            {
                Phase = RoundPhase.Play,
                Turn = turn,
                CurrentTrick = null
            };
            for (int i = 0; i < 4; i++)
            {
                s.Seats[i] = new PlayerSeat { SeatIndex = i };
                s.Seats[i].Hand.AddRange(hands[i]);
            }
            return s;
        }

        private static List<Card> Hand(params Card[] cards) => new List<Card>(cards);

        // ── Next() ─────────────────────────────────────────────────────────────

        [Test]
        public void Next_on_fresh_round_is_grand_tichu()
        {
            var s = GameEngine.NewRound(1UL);
            var step = FlowQuery.Next(s);
            Assert.That(step.Kind, Is.EqualTo(StepKind.GrandTichu));
            Assert.That(step.Seat, Is.EqualTo(-1));
        }

        [Test]
        public void Next_after_grand_decisions_is_exchange()
        {
            var s = GameEngine.NewRound(1UL);
            for (int i = 0; i < 4; i++)
                GameEngine.Apply(s, GameAction.DeclineGrandTichu(i));

            Assert.That(s.Phase, Is.EqualTo(RoundPhase.Exchange));
            var step = FlowQuery.Next(s);
            Assert.That(step.Kind, Is.EqualTo(StepKind.Exchange));
            Assert.That(step.Seat, Is.EqualTo(-1));
        }

        [Test]
        public void Next_after_exchanges_is_play_at_turn()
        {
            var s = GameEngine.NewRound(1UL);
            for (int i = 0; i < 4; i++)
                GameEngine.Apply(s, GameAction.DeclineGrandTichu(i));
            for (int seat = 0; seat < 4; seat++)
            {
                var h = s.Seats[seat].Hand;
                GameEngine.Apply(s, GameAction.Exchange(seat,
                    new List<Card> { h[0] }, new List<Card> { h[1] }, new List<Card> { h[2] }));
            }

            Assert.That(s.Phase, Is.EqualTo(RoundPhase.Play));
            var step = FlowQuery.Next(s);
            Assert.That(step.Kind, Is.EqualTo(StepKind.Play));
            Assert.That(step.Seat, Is.EqualTo(s.Turn));
        }

        [Test]
        public void Next_when_scoring()
        {
            var s = new GameState { Phase = RoundPhase.Scoring };
            for (int i = 0; i < 4; i++)
                s.Seats[i] = new PlayerSeat { SeatIndex = i };

            var step = FlowQuery.Next(s);
            Assert.That(step.Kind, Is.EqualTo(StepKind.Scoring));
            Assert.That(step.Seat, Is.EqualTo(-1));
        }

        // ── SeatsWithLegalBomb() ───────────────────────────────────────────────

        [Test]
        public void SeatsWithLegalBomb_empty_on_lead()
        {
            // CurrentTrick==null → 리드 상황이므로 오프턴 폭탄 창 없음.
            var s = PlayState(0,
                Hand(Card.Normal(6, Suit.Jade), Card.Normal(6, Suit.Sword),
                     Card.Normal(6, Suit.Pagoda), Card.Normal(6, Suit.Star)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            var result = FlowQuery.SeatsWithLegalBomb(s);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void SeatsWithLegalBomb_lists_offturn_bomb_holders_clockwise()
        {
            // Turn=0, non-bomb single (9♠) on top.
            // seat1: 폭탄 없음 (single 3♠)
            // seat2: 4장 6 폭탄 보유 → listed (clockwise order: Turn+1=1, Turn+2=2, Turn+3=3)
            // seat3: 4장 7 폭탄 보유 → listed
            // seat0 (Turn): excluded
            // 기대 순서: seat2 before seat3 (clockwise from Turn+1: 1,2,3 → seat1 no bomb, seat2 bomb, seat3 bomb)
            var s = PlayState(0,
                Hand(Card.Normal(9, Suit.Jade), Card.Normal(2, Suit.Pagoda)),           // seat0 (turn)
                Hand(Card.Normal(3, Suit.Sword)),                                         // seat1, no bomb
                Hand(Card.Normal(6, Suit.Jade), Card.Normal(6, Suit.Sword),
                     Card.Normal(6, Suit.Pagoda), Card.Normal(6, Suit.Star),
                     Card.Normal(2, Suit.Jade)),                                          // seat2, 4-of-a-kind bomb
                Hand(Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Sword),
                     Card.Normal(7, Suit.Pagoda), Card.Normal(7, Suit.Star),
                     Card.Normal(2, Suit.Sword)));                                        // seat3, 4-of-a-kind bomb

            // seat0 plays a single 9 to establish a non-bomb trick
            GameEngine.Apply(s, GameAction.Play(0, new List<Card> { Card.Normal(9, Suit.Jade) }));
            Assert.That(s.CurrentTrick, Is.Not.Null, "trick must be active");
            // Turn is now seat1 (normal flow)

            var result = FlowQuery.SeatsWithLegalBomb(s);

            // seat0 (original Turn before play) is not s.Turn anymore — s.Turn is seat1 after the lead.
            // The spec says: skip s.Turn itself (current turn holder) and IsOut seats.
            // After the play, s.Turn == 1 (seat1's turn to follow).
            // So clockwise from seat2: visit 2, 3, 0; seat1 (s.Turn) skipped.
            // seat2 has bomb, seat3 has bomb, seat0 no bomb in remaining hand.
            Assert.That(result, Has.No.Member(s.Turn), "current turn holder excluded");
            Assert.That(result, Has.Member(2));
            Assert.That(result, Has.Member(3));
            // Clockwise order from (Turn+1)%4: Turn=1, so visit 2,3,0 (skip seat1).
            var resultList = result.ToList();
            int idxSeat2 = resultList.IndexOf(2);
            int idxSeat3 = resultList.IndexOf(3);
            Assert.That(idxSeat2, Is.LessThan(idxSeat3), "seat2 before seat3 in clockwise order");
        }

        [Test]
        public void SeatsWithLegalBomb_excludes_out_seats()
        {
            // seat2 is out, seat3 has a bomb; seat2's bomb should not appear.
            var s = PlayState(0,
                Hand(Card.Normal(9, Suit.Jade), Card.Normal(2, Suit.Pagoda)),
                Hand(Card.Normal(3, Suit.Sword)),
                Hand(Card.Normal(6, Suit.Jade), Card.Normal(6, Suit.Sword),
                     Card.Normal(6, Suit.Pagoda), Card.Normal(6, Suit.Star)),  // seat2: bomb but out
                Hand(Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Sword),
                     Card.Normal(7, Suit.Pagoda), Card.Normal(7, Suit.Star),
                     Card.Normal(2, Suit.Sword)));                              // seat3: bomb

            s.Seats[2].IsOut = true;
            s.Seats[2].FinishOrder = 1;

            GameEngine.Apply(s, GameAction.Play(0, new List<Card> { Card.Normal(9, Suit.Jade) }));
            Assert.That(s.CurrentTrick, Is.Not.Null);

            var result = FlowQuery.SeatsWithLegalBomb(s);
            Assert.That(result, Has.No.Member(2), "out seat excluded");
            Assert.That(result, Has.Member(3));
        }

        // ── PendingDragonGift() ───────────────────────────────────────────────

        [Test]
        public void Next_returns_dragon_gift_when_pending()
        {
            // 용 단독으로 트릭을 이기면 PendingDragonGiftWinner 가 설정된다.
            var s = PlayState(0,
                Hand(Card.Dragon, Card.Normal(9, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            GameEngine.Apply(s, GameAction.Play(0, new List<Card> { Card.Dragon }));
            GameEngine.Apply(s, GameAction.Pass(1));
            GameEngine.Apply(s, GameAction.Pass(2));
            GameEngine.Apply(s, GameAction.Pass(3));

            Assert.That(s.TryGetPendingDragonGift(out int expectedWinner), Is.True, "must be pending");

            var step = FlowQuery.Next(s);
            Assert.That(step.Kind, Is.EqualTo(StepKind.DragonGift));
            Assert.That(step.Seat, Is.EqualTo(expectedWinner));
        }

        [Test]
        public void SeatsWithLegalBomb_order_is_clockwise_not_ascending()
        {
            // Turn=1 initially; seat1 plays a single to push Turn to 2.
            // After the play: s.Turn==2, CurrentTrick is active.
            // Bomb holders among off-turn seats: seat3 (4-of-a-kind 6) and seat0 (4-of-a-kind 7).
            // Clockwise from (2+1)%4=3: visit seat3, seat0, seat1 — skip seat2 (Turn).
            // Expected order: [3, 0].
            // A buggy ascending-0..3 loop (skipping Turn=2) would give [0, 3] — test catches that.
            var s = PlayState(1,
                Hand(Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Sword),
                     Card.Normal(7, Suit.Pagoda), Card.Normal(7, Suit.Star)),  // seat0: 4-of-a-kind bomb
                Hand(Card.Normal(9, Suit.Jade), Card.Normal(2, Suit.Pagoda)), // seat1: leads single 9, no bomb
                Hand(Card.Normal(3, Suit.Sword)),                              // seat2 (will be Turn): no bomb
                Hand(Card.Normal(6, Suit.Jade), Card.Normal(6, Suit.Sword),
                     Card.Normal(6, Suit.Pagoda), Card.Normal(6, Suit.Star))); // seat3: 4-of-a-kind bomb

            // seat1 leads a single → Turn advances to seat2.
            GameEngine.Apply(s, GameAction.Play(1, new List<Card> { Card.Normal(9, Suit.Jade) }));
            Assert.That(s.CurrentTrick, Is.Not.Null, "trick must be active");
            Assert.That(s.Turn, Is.EqualTo(2), "Turn must be 2 for the clockwise-vs-ascending distinction");

            var result = FlowQuery.SeatsWithLegalBomb(s);
            var resultList = result.ToList();

            Assert.That(resultList, Has.No.Member(2), "current Turn (seat2) excluded");
            Assert.That(resultList, Has.Member(3));
            Assert.That(resultList, Has.Member(0));
            int idxSeat3 = resultList.IndexOf(3);
            int idxSeat0 = resultList.IndexOf(0);
            Assert.That(idxSeat3, Is.LessThan(idxSeat0),
                "clockwise from Turn+1=3: seat3 must come before seat0; ascending order would give [0,3]");
        }

        [Test]
        public void PendingDragonGift_false_when_not_pending()
        {
            var s = PlayState(0,
                Hand(Card.Normal(9, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            bool pending = FlowQuery.PendingDragonGift(s, out int winner);
            Assert.That(pending, Is.False);
            Assert.That(winner, Is.EqualTo(-1));
        }
    }
}
