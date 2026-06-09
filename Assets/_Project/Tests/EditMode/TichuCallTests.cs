#nullable enable
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Game;

namespace Tichu.Core.Tests
{
    /// <summary>큰 티츄 선언 / 거절 관련 GameEngine 동작 검증.</summary>
    public class TichuCallTests
    {
        [Test]
        public void CallGrandTichu_sets_call_to_grand_tichu()
        {
            var s = GameEngine.NewRound(1UL);
            GameEngine.Apply(s, GameAction.CallGrandTichu(2));

            Assert.That(s.Seats[2].Call, Is.EqualTo(TichuCall.GrandTichu));
        }

        [Test]
        public void DeclineGrandTichu_leaves_call_none()
        {
            var s = GameEngine.NewRound(1UL);
            GameEngine.Apply(s, GameAction.DeclineGrandTichu(3));

            Assert.That(s.Seats[3].Call, Is.EqualTo(TichuCall.None));
        }

        [Test]
        public void Each_seat_can_decide_exactly_once()
        {
            var s = GameEngine.NewRound(1UL);

            for (int seat = 0; seat < 4; seat++)
            {
                var first = GameEngine.Apply(s, GameAction.CallGrandTichu(seat));
                Assert.That(first.Ok, Is.True, $"seat {seat} first decision should be accepted");

                var second = GameEngine.Apply(s, GameAction.DeclineGrandTichu(seat));
                Assert.That(second.Ok, Is.False, $"seat {seat} second decision should be rejected");
            }
        }

        [Test]
        public void Mix_of_call_and_decline_all_four_advances_to_exchange()
        {
            var s = GameEngine.NewRound(1UL);

            GameEngine.Apply(s, GameAction.CallGrandTichu(0));
            GameEngine.Apply(s, GameAction.DeclineGrandTichu(1));
            GameEngine.Apply(s, GameAction.CallGrandTichu(2));
            GameEngine.Apply(s, GameAction.DeclineGrandTichu(3));

            Assert.That(s.Phase, Is.EqualTo(RoundPhase.Exchange));
            Assert.That(s.Seats[0].Call, Is.EqualTo(TichuCall.GrandTichu));
            Assert.That(s.Seats[1].Call, Is.EqualTo(TichuCall.None));
            Assert.That(s.Seats[2].Call, Is.EqualTo(TichuCall.GrandTichu));
            Assert.That(s.Seats[3].Call, Is.EqualTo(TichuCall.None));
        }

        // ── 작은 티츄 (Play 페이즈, 첫 카드 전) ──────────────────────────────────────

        /// <summary>전원 큰 티츄 거절 → 앞 3장 교환 → Play 페이즈 진입.</summary>
        private static GameState AdvanceToPlay(ulong seed = 1UL)
        {
            var s = GameEngine.NewRound(seed);
            for (int i = 0; i < 4; i++)
                GameEngine.Apply(s, GameAction.DeclineGrandTichu(i));
            for (int seat = 0; seat < 4; seat++)
            {
                var h = s.Seats[seat].Hand;
                GameEngine.Apply(s, GameAction.Exchange(seat,
                    new List<Card> { h[0] }, new List<Card> { h[1] }, new List<Card> { h[2] }));
            }
            return s;
        }

        [Test]
        public void Tichu_call_before_first_card_ok_and_rejected_after()
        {
            var s = AdvanceToPlay();
            int lead = s.Turn; // 마작 보유자

            // 첫 카드 전: 14장 손 → 작은 티츄 허용
            var ok = GameEngine.Apply(s, GameAction.CallTichu(lead));
            Assert.That(ok.Ok, Is.True);
            Assert.That(s.Seats[lead].Call, Is.EqualTo(TichuCall.Tichu));

            // 리드(마작 단독)로 첫 카드 한 장 냄
            var play = GameEngine.Apply(s, GameAction.Play(lead, new List<Card> { Card.Mahjong }));
            Assert.That(play.Ok, Is.True);

            // 이미 카드를 냈으므로 작은 티츄 거부
            var after = GameEngine.Apply(s, GameAction.CallTichu(lead));
            Assert.That(after.Ok, Is.False);
            Assert.That(after.RejectReason, Is.Not.Empty);
        }
    }
}
