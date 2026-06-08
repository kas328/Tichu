using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;

namespace Tichu.Core.Tests
{
    /// <summary>Play 페이즈(트릭 루프, 개, 용, 작은 티츄) 검증.</summary>
    public class PlayPhaseTests
    {
        // ── 헬퍼 ────────────────────────────────────────────────────────────────

        /// <summary>지정한 손패로 Play 페이즈 상태를 직접 구성한다(셋업 우회).</summary>
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

        // ── 마작 보유자 선공 ──────────────────────────────────────────────────────

        [Test]
        public void Mahjong_holder_leads_first()
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

            int mahjongSeat = -1;
            for (int i = 0; i < 4; i++)
                if (s.Seats[i].Hand.Contains(Card.Mahjong)) mahjongSeat = i;

            Assert.That(s.Phase, Is.EqualTo(RoundPhase.Play));
            Assert.That(s.Turn, Is.EqualTo(mahjongSeat));
        }

        // ── 트릭 루프 ─────────────────────────────────────────────────────────────

        [Test]
        public void Lead_then_all_pass_collects_trick_to_leader()
        {
            var s = PlayState(0,
                Hand(Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Sword), Card.Normal(9, Suit.Star)),
                Hand(Card.Normal(3, Suit.Jade), Card.Normal(4, Suit.Sword)),
                Hand(Card.Normal(3, Suit.Pagoda), Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Sword), Card.Normal(4, Suit.Pagoda)));

            var r0 = GameEngine.Apply(s, GameAction.Play(0,
                new List<Card> { Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Sword) }));
            Assert.That(r0.Ok, Is.True);
            Assert.That(s.CurrentTrick, Is.Not.Null);

            Assert.That(GameEngine.Apply(s, GameAction.Pass(1)).Ok, Is.True);
            Assert.That(GameEngine.Apply(s, GameAction.Pass(2)).Ok, Is.True);
            Assert.That(GameEngine.Apply(s, GameAction.Pass(3)).Ok, Is.True);

            Assert.That(s.CurrentTrick, Is.Null, "trick should be collected");
            Assert.That(s.CompletedTricks.Count, Is.EqualTo(1));
            Assert.That(s.CompletedTricks[0].TopOwnerSeat, Is.EqualTo(0));
            Assert.That(s.CompletedTricks[0].AccumulatedPoints, Is.EqualTo(0));
            Assert.That(s.Turn, Is.EqualTo(0), "winner leads next");
        }

        [Test]
        public void Higher_play_takes_over_then_passes_collect_to_new_top()
        {
            // 모든 좌석에 여분 카드를 두어 아무도 아웃되지 않게 한다(턴이 소유자에게 복귀하는 경로 검증).
            var s = PlayState(0,
                Hand(Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Sword), Card.Normal(2, Suit.Pagoda)),
                Hand(Card.Normal(13, Suit.Jade), Card.Normal(13, Suit.Sword), Card.Normal(2, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Pagoda), Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Sword), Card.Normal(4, Suit.Pagoda)));

            Assert.That(GameEngine.Apply(s, GameAction.Play(0,
                new List<Card> { Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Sword) })).Ok, Is.True);
            Assert.That(GameEngine.Apply(s, GameAction.Play(1,
                new List<Card> { Card.Normal(13, Suit.Jade), Card.Normal(13, Suit.Sword) })).Ok, Is.True);
            Assert.That(GameEngine.Apply(s, GameAction.Pass(2)).Ok, Is.True);
            Assert.That(GameEngine.Apply(s, GameAction.Pass(3)).Ok, Is.True);
            Assert.That(GameEngine.Apply(s, GameAction.Pass(0)).Ok, Is.True);

            Assert.That(s.CurrentTrick, Is.Null);
            Assert.That(s.CompletedTricks.Count, Is.EqualTo(1));
            Assert.That(s.CompletedTricks[0].TopOwnerSeat, Is.EqualTo(1), "new top owner wins");
            Assert.That(s.Turn, Is.EqualTo(1));
        }

        [Test]
        public void Accumulated_points_correct()
        {
            // 단독 점수가 Top이 바뀔 때마다 누적된다: 10(10) + K(10) = 20.
            // seat0, seat1에 여분 카드를 두어 아웃되지 않게 한다(턴 복귀 경로).
            var s = PlayState(0,
                Hand(Card.Normal(10, Suit.Jade), Card.Normal(2, Suit.Pagoda)),     // 10=10
                Hand(Card.Normal(13, Suit.Sword), Card.Normal(2, Suit.Jade)),      // K=10
                Hand(Card.Normal(3, Suit.Pagoda)),
                Hand(Card.Normal(4, Suit.Star)));

            Assert.That(GameEngine.Apply(s, GameAction.Play(0,
                new List<Card> { Card.Normal(10, Suit.Jade) })).Ok, Is.True);   // +10
            Assert.That(GameEngine.Apply(s, GameAction.Play(1,
                new List<Card> { Card.Normal(13, Suit.Sword) })).Ok, Is.True);  // +10 (K beats 10)
            Assert.That(GameEngine.Apply(s, GameAction.Pass(2)).Ok, Is.True);
            Assert.That(GameEngine.Apply(s, GameAction.Pass(3)).Ok, Is.True);
            Assert.That(GameEngine.Apply(s, GameAction.Pass(0)).Ok, Is.True);

            Assert.That(s.CompletedTricks.Count, Is.EqualTo(1));
            Assert.That(s.CompletedTricks[0].AccumulatedPoints, Is.EqualTo(20),
                "K(10) + 10(10) accumulates to 20");
        }

        [Test]
        public void Bomb_can_be_played_out_of_turn_to_take_trick()
        {
            // seat0 leads single A; seat2 bombs out of turn (four 6s)
            var s = PlayState(0,
                Hand(Card.Normal(14, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(6, Suit.Jade), Card.Normal(6, Suit.Sword),
                     Card.Normal(6, Suit.Pagoda), Card.Normal(6, Suit.Star)),
                Hand(Card.Normal(4, Suit.Jade)));

            Assert.That(GameEngine.Apply(s, GameAction.Play(0,
                new List<Card> { Card.Normal(14, Suit.Jade) })).Ok, Is.True);
            // It's seat1's turn, but seat2 bombs
            var rb = GameEngine.Apply(s, GameAction.Play(2, new List<Card>
            {
                Card.Normal(6, Suit.Jade), Card.Normal(6, Suit.Sword),
                Card.Normal(6, Suit.Pagoda), Card.Normal(6, Suit.Star)
            }));
            Assert.That(rb.Ok, Is.True);
            Assert.That(s.CurrentTrick!.TopOwnerSeat, Is.EqualTo(2));
            var lastPlay = s.CurrentTrick.History.Last();
            Assert.That(lastPlay.Seat, Is.EqualTo(2));
            Assert.That(lastPlay.IsBombInterrupt, Is.True);
        }

        // ── 개 ────────────────────────────────────────────────────────────────────

        [Test]
        public void Dog_passes_lead_to_partner_no_trick()
        {
            var s = PlayState(0,
                Hand(Card.Dog, Card.Normal(9, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            var r = GameEngine.Apply(s, GameAction.Play(0, new List<Card> { Card.Dog }));
            Assert.That(r.Ok, Is.True);
            Assert.That(s.CurrentTrick, Is.Null, "dog forms no trick");
            Assert.That(s.Turn, Is.EqualTo(2), "lead passes to partner");
            Assert.That(s.Seats[0].Hand.Contains(Card.Dog), Is.False, "dog removed from hand");
            Assert.That(s.CompletedTricks.Count, Is.EqualTo(0), "no trick recorded");
            // 개는 점수 더미에 들어가지 않는다
            Assert.That(s.Seats.All(seat => !seat.WonCards.Contains(Card.Dog)), Is.True);
        }

        [Test]
        public void Dog_to_next_when_partner_out()
        {
            var s = PlayState(0,
                Hand(Card.Dog, Card.Normal(9, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));
            s.Seats[2].IsOut = true;
            s.Seats[2].FinishOrder = 1;

            var r = GameEngine.Apply(s, GameAction.Play(0, new List<Card> { Card.Dog }));
            Assert.That(r.Ok, Is.True);
            Assert.That(s.Turn, Is.EqualTo(3), "partner out → next active");
        }

        // ── 용 ────────────────────────────────────────────────────────────────────

        [Test]
        public void Dragon_single_wins_and_sets_WonByDragon()
        {
            var s = PlayState(0,
                Hand(Card.Dragon, Card.Normal(9, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            Assert.That(GameEngine.Apply(s, GameAction.Play(0, new List<Card> { Card.Dragon })).Ok, Is.True);
            Assert.That(GameEngine.Apply(s, GameAction.Pass(1)).Ok, Is.True);
            Assert.That(GameEngine.Apply(s, GameAction.Pass(2)).Ok, Is.True);
            Assert.That(GameEngine.Apply(s, GameAction.Pass(3)).Ok, Is.True);

            Assert.That(s.CompletedTricks.Count, Is.EqualTo(1));
            Assert.That(s.CompletedTricks[0].WonByDragon, Is.True);
            Assert.That(s.CompletedTricks[0].TopOwnerSeat, Is.EqualTo(0));
        }

        // ── 봉황 ──────────────────────────────────────────────────────────────────

        [Test]
        public void Phoenix_single_follow_beats_via_trick_context()
        {
            var s = PlayState(0,
                Hand(Card.Normal(13, Suit.Jade)),
                Hand(Card.Phoenix, Card.Normal(2, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            Assert.That(GameEngine.Apply(s, GameAction.Play(0,
                new List<Card> { Card.Normal(13, Suit.Jade) })).Ok, Is.True);
            var r = GameEngine.Apply(s, GameAction.Play(1, new List<Card> { Card.Phoenix }));
            Assert.That(r.Ok, Is.True, "phoenix (K+0.5) beats K single");
            Assert.That(s.CurrentTrick!.TopOwnerSeat, Is.EqualTo(1));
        }

        // ── 작은 티츄 ──────────────────────────────────────────────────────────────

        [Test]
        public void Out_sets_finish_order()
        {
            var s = PlayState(0,
                Hand(Card.Normal(9, Suit.Jade)),          // last card
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            Assert.That(GameEngine.Apply(s, GameAction.Play(0,
                new List<Card> { Card.Normal(9, Suit.Jade) })).Ok, Is.True);
            Assert.That(s.Seats[0].IsOut, Is.True);
            Assert.That(s.Seats[0].FinishOrder, Is.EqualTo(1));
        }

        [Test]
        public void Trick_completes_when_winner_went_out_on_winning_play()
        {
            // seat0 leads single A as last card → goes out and is the Top owner.
            // 턴은 그에게 닿을 수 없으므로, 남은 살아있는 좌석 전원 패스로 완료되어야 한다.
            var s = PlayState(0,
                Hand(Card.Normal(14, Suit.Jade)),   // last card
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            Assert.That(GameEngine.Apply(s, GameAction.Play(0,
                new List<Card> { Card.Normal(14, Suit.Jade) })).Ok, Is.True);
            Assert.That(s.Seats[0].IsOut, Is.True);
            // 트릭은 아직 진행 중(소유자가 아웃됨): 살아있는 3명이 패스해야 완료.
            Assert.That(s.CurrentTrick, Is.Not.Null);
            Assert.That(GameEngine.Apply(s, GameAction.Pass(1)).Ok, Is.True);
            Assert.That(GameEngine.Apply(s, GameAction.Pass(2)).Ok, Is.True);
            Assert.That(s.CurrentTrick, Is.Not.Null, "still one active seat to respond");
            Assert.That(GameEngine.Apply(s, GameAction.Pass(3)).Ok, Is.True);

            Assert.That(s.CurrentTrick, Is.Null);
            Assert.That(s.CompletedTricks.Count, Is.EqualTo(1));
            Assert.That(s.CompletedTricks[0].TopOwnerSeat, Is.EqualTo(0));
            // 승자(seat0)가 아웃이므로 다음 리드는 NextActive(0)=1.
            Assert.That(s.Turn, Is.EqualTo(1));
        }

        // ── 거부 케이스 ─────────────────────────────────────────────────────────────

        [Test]
        public void Pass_on_own_lead_is_rejected()
        {
            var s = PlayState(0,
                Hand(Card.Normal(9, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            var r = GameEngine.Apply(s, GameAction.Pass(0));
            Assert.That(r.Ok, Is.False);
            Assert.That(r.RejectReason, Is.Not.Empty);
        }

        [Test]
        public void Play_out_of_turn_nonbomb_is_rejected()
        {
            var s = PlayState(0,
                Hand(Card.Normal(9, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            Assert.That(GameEngine.Apply(s, GameAction.Play(0,
                new List<Card> { Card.Normal(9, Suit.Jade) })).Ok, Is.True);
            // seat1's turn; seat2 plays a non-bomb single out of turn
            var r = GameEngine.Apply(s, GameAction.Play(2, new List<Card> { Card.Normal(4, Suit.Jade) }));
            Assert.That(r.Ok, Is.False);
            Assert.That(r.RejectReason, Is.Not.Empty);
        }

        [Test]
        public void Play_card_not_in_hand_is_rejected()
        {
            var s = PlayState(0,
                Hand(Card.Normal(9, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            var r = GameEngine.Apply(s, GameAction.Play(0,
                new List<Card> { Card.Normal(14, Suit.Star) }));
            Assert.That(r.Ok, Is.False);
            Assert.That(r.RejectReason, Is.Not.Empty);
        }

        // ── 마작 소원 ─────────────────────────────────────────────────────────────

        [Test]
        public void Mahjong_lead_with_wish_sets_state_wish()
        {
            // seat0이 마작 단독으로 리드하며 소원 7 설정.
            var s = PlayState(0,
                Hand(Card.Mahjong),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            var r = GameEngine.Apply(s, GameAction.Play(0, new List<Card> { Card.Mahjong }, wish: 7));
            Assert.That(r.Ok, Is.True);
            Assert.That(s.Wish, Is.EqualTo(7));
        }

        [Test]
        public void Mahjong_lead_with_out_of_range_wish_does_not_set()
        {
            // 소원 범위는 2~14 — 범위 밖(1)이면 s.Wish 가 null 유지.
            var s = PlayState(0,
                Hand(Card.Mahjong),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            var r = GameEngine.Apply(s, GameAction.Play(0, new List<Card> { Card.Mahjong }, wish: 1));
            Assert.That(r.Ok, Is.True);
            Assert.That(s.Wish, Is.Null);
        }

        // ── Clone 독립성 ───────────────────────────────────────────────────────────

        [Test]
        public void Clone_deep_copies_completed_tricks()
        {
            var s = PlayState(0,
                Hand(Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Sword)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            GameEngine.Apply(s, GameAction.Play(0,
                new List<Card> { Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Sword) }));
            GameEngine.Apply(s, GameAction.Pass(1));
            GameEngine.Apply(s, GameAction.Pass(2));
            GameEngine.Apply(s, GameAction.Pass(3));

            Assert.That(s.CompletedTricks.Count, Is.EqualTo(1));

            var clone = s.Clone();
            Assert.That(clone.CompletedTricks.Count, Is.EqualTo(1));
            Assert.That(clone.CompletedTricks, Is.Not.SameAs(s.CompletedTricks));
            Assert.That(clone.CompletedTricks[0], Is.Not.SameAs(s.CompletedTricks[0]));

            // 클론의 트릭 리스트를 비워도 원본은 영향 없어야
            clone.CompletedTricks.Clear();
            Assert.That(s.CompletedTricks.Count, Is.EqualTo(1));
        }
    }
}
