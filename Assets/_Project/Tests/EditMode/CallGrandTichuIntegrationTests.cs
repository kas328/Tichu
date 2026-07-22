using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Game;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// B1 콜 헤드 통합 검증(AiAgent 게이트) — OFF는 HandPower, ON은 CallNet 임계값 경로.
    /// PolicyConfig/PimcAgent 배선은 Unity 전용(EditMode에서 검증).
    /// </summary>
    public class CallGrandTichuIntegrationTests
    {
        // 강패(HandPower = A2*2 + 용4 + 봉황3 + K1 = 12 ≥ 10) 8장을 좌석 0에 직접 구성.
        private static GameState StateWithGrandHand()
        {
            var s = GameEngine.NewRound(1);
            var hand = s.Seats[0].Hand;
            hand.Clear();
            hand.Add(Card.Normal(14, Suit.Jade));
            hand.Add(Card.Normal(14, Suit.Sword));
            hand.Add(Card.Dragon);
            hand.Add(Card.Phoenix);
            hand.Add(Card.Normal(13, Suit.Jade));
            hand.Add(Card.Normal(12, Suit.Jade));
            hand.Add(Card.Normal(9, Suit.Jade));
            hand.Add(Card.Normal(8, Suit.Jade));
            return s;
        }

        [Test]
        public void Flag_off_matches_HandPower_gate()
        {
            var s = StateWithGrandHand();
            var agent = new AiAgent(1, 0); // 기본 OFF
            Assert.That(agent.CallGrandTichu(new DecisionContext(s, 0)), Is.True); // HandPower 12 ≥ 10
        }

        [Test]
        public void Flag_on_routes_through_CallNet_threshold()
        {
            var s = StateWithGrandHand();
            // 플레이스홀더 가중치 → Predict=0.5. 임계값<0.5 면 콜, >0.5 면 패스 → CallNet 경로 증명.
            Assert.That(new AiAgent(1, 0, useGrandCallNet: true, grandThreshold: 0.49).CallGrandTichu(new DecisionContext(s, 0)), Is.True);
            Assert.That(new AiAgent(1, 0, useGrandCallNet: true, grandThreshold: 0.51).CallGrandTichu(new DecisionContext(s, 0)), Is.False);
        }
    }
}
