using NUnit.Framework;
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
    }
}
