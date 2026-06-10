using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;

namespace Tichu.Core.Tests
{
    /// <summary>라운드 종료 판정(3아웃, 원-투, 진행중 트릭 마무리) 검증.</summary>
    public class OutAndRoundEndTests
    {
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

        // ── 3 아웃 → Scoring ──────────────────────────────────────────────────────

        [Test]
        public void Three_outs_transition_to_scoring()
        {
            // seat0, seat1 이미 아웃(1,2). seat2가 마지막 카드를 내면 3번째 아웃 → 라운드 종료.
            var s = PlayState(2,
                Hand(),
                Hand(),
                Hand(Card.Normal(9, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));
            s.Seats[0].IsOut = true; s.Seats[0].FinishOrder = 1;
            s.Seats[1].IsOut = true; s.Seats[1].FinishOrder = 2;

            var r = GameEngine.Apply(s, GameAction.Play(2, new List<Card> { Card.Normal(9, Suit.Jade) }));
            Assert.That(r.Ok, Is.True);
            Assert.That(s.Seats[2].IsOut, Is.True);
            Assert.That(s.Seats[2].FinishOrder, Is.EqualTo(3));
            Assert.That(s.Phase, Is.EqualTo(RoundPhase.Scoring));
        }

        // ── 원-투: 2번째 아웃에서 즉시 종료 ────────────────────────────────────────

        [Test]
        public void One_two_ends_at_second_out_when_partners()
        {
            // seat0 이미 1번 아웃. seat2(파트너)가 2번째로 아웃 → 즉시 Scoring.
            var s = PlayState(2,
                Hand(),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(9, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));
            s.Seats[0].IsOut = true; s.Seats[0].FinishOrder = 1;

            var r = GameEngine.Apply(s, GameAction.Play(2, new List<Card> { Card.Normal(9, Suit.Jade) }));
            Assert.That(r.Ok, Is.True);
            Assert.That(s.Seats[2].FinishOrder, Is.EqualTo(2));
            Assert.That(s.Phase, Is.EqualTo(RoundPhase.Scoring), "partners 1-2 ends round at 2nd out");
        }

        [Test]
        public void Opponent_second_out_does_not_trigger_one_two()
        {
            // seat0 이미 1번 아웃. seat1(상대)이 2번째 아웃 → 라운드 계속(Play).
            var s = PlayState(1,
                Hand(),
                Hand(Card.Normal(9, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));
            s.Seats[0].IsOut = true; s.Seats[0].FinishOrder = 1;

            var r = GameEngine.Apply(s, GameAction.Play(1, new List<Card> { Card.Normal(9, Suit.Jade) }));
            Assert.That(r.Ok, Is.True);
            Assert.That(s.Seats[1].FinishOrder, Is.EqualTo(2));
            Assert.That(s.Phase, Is.EqualTo(RoundPhase.Play), "opponent 2nd out does not end round");
        }

        // ── 진행중 트릭 마무리 ─────────────────────────────────────────────────────

        [Test]
        public void In_progress_trick_finalized_on_round_end()
        {
            // seat0 이미 아웃(1). seat1 아웃(2). seat2가 리드로 마지막 카드를 내며 3번째 아웃.
            // 트릭 소유자(seat2)가 아웃되어 트릭이 진행중인 채로 라운드 종료 → 마무리되어야.
            var s = PlayState(2,
                Hand(),
                Hand(),
                Hand(Card.Normal(13, Suit.Jade)),   // K=10점, 마지막 카드
                Hand(Card.Normal(5, Suit.Jade)));
            s.Seats[0].IsOut = true; s.Seats[0].FinishOrder = 1;
            s.Seats[1].IsOut = true; s.Seats[1].FinishOrder = 2;

            var r = GameEngine.Apply(s, GameAction.Play(2, new List<Card> { Card.Normal(13, Suit.Jade) }));
            Assert.That(r.Ok, Is.True);
            Assert.That(s.Phase, Is.EqualTo(RoundPhase.Scoring));
            Assert.That(s.CurrentTrick, Is.Null, "in-progress trick finalized");
            Assert.That(s.CompletedTricks.Count, Is.EqualTo(1), "no cards lost");
            Assert.That(s.CompletedTricks[0].TopOwnerSeat, Is.EqualTo(2));
            Assert.That(s.CompletedTricks[0].AccumulatedPoints, Is.EqualTo(10));
        }
    }
}
