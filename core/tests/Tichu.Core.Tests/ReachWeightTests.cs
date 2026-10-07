using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Game;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    public class ReachWeightTests
    {
        private static List<Card> H(params Card[] c) => new List<Card>(c);
        private static Card N(int r, Suit s) => Card.Normal(r, s);

        [Test]
        public void HandStrength_counts_high_cards()
        {
            int strong = ReachWeight.HandStrength(H(Card.Dragon, Card.Phoenix, N(14, Suit.Jade), N(13, Suit.Sword)));
            int weak = ReachWeight.HandStrength(H(N(2, Suit.Jade), N(3, Suit.Sword), N(4, Suit.Pagoda), N(5, Suit.Star)));
            Assert.That(strong, Is.GreaterThan(weak));
            Assert.That(weak, Is.EqualTo(0));
            Assert.That(strong, Is.EqualTo(4 + 3 + 2 + 1));
        }

        [Test]
        public void WorldWeight_is_one_when_no_calls()
        {
            var s = GameFlowHelpers.PlayState(0,
                H(N(2, Suit.Jade)), H(N(3, Suit.Jade)), H(N(4, Suit.Jade)), H(N(5, Suit.Jade)));
            Assert.That(ReachWeight.WorldWeight(s, 0), Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void WorldWeight_higher_when_caller_has_strong_hand()
        {
            var strongWorld = GameFlowHelpers.PlayState(0,
                H(N(2, Suit.Jade)), H(Card.Dragon, N(14, Suit.Sword)), H(N(4, Suit.Jade)), H(N(5, Suit.Jade)));
            var weakWorld = GameFlowHelpers.PlayState(0,
                H(N(2, Suit.Jade)), H(N(13, Suit.Sword), N(4, Suit.Sword)), H(N(4, Suit.Jade)), H(N(5, Suit.Jade)));
            strongWorld.Seats[1].Call = TichuCall.Tichu;
            weakWorld.Seats[1].Call = TichuCall.Tichu;

            double wStrong = ReachWeight.WorldWeight(strongWorld, 0);
            double wWeak = ReachWeight.WorldWeight(weakWorld, 0);
            Assert.That(wStrong, Is.GreaterThan(wWeak), "강한 손 배정이 콜과 더 일관 → 가중↑");
            Assert.That(wWeak, Is.GreaterThan(1.0), "콜 있으면 1.0 초과");
        }

        [Test]
        public void WorldWeight_observer_call_ignored()
        {
            var s = GameFlowHelpers.PlayState(0,
                H(Card.Dragon), H(N(3, Suit.Jade)), H(N(4, Suit.Jade)), H(N(5, Suit.Jade)));
            s.Seats[0].Call = TichuCall.GrandTichu; // 관측자 자신 → 무시
            Assert.That(ReachWeight.WorldWeight(s, 0), Is.EqualTo(1.0).Within(1e-9));
        }
    }
}
