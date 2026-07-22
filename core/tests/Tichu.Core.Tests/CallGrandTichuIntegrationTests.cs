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
            // 가중치 독립 라우팅 증명: σ∈(0,1). 임계값 0 이면 항상 콜, 1 이면 항상 패스 → CallNet 경로.
            Assert.That(new AiAgent(1, 0, useGrandCallNet: true, grandThreshold: 0.0).CallGrandTichu(new DecisionContext(s, 0)), Is.True);
            Assert.That(new AiAgent(1, 0, useGrandCallNet: true, grandThreshold: 1.0).CallGrandTichu(new DecisionContext(s, 0)), Is.False);
        }

        [Test]
        public void Flag_on_calls_grand_on_strong_hand_at_default_threshold()
        {
            // 강패(용+봉황+A+K+Q…)는 학습된 헤드에서도 P>0.5 → 기본 임계값(0.5)에서 콜.
            var s = StateWithGrandHand();
            Assert.That(new AiAgent(1, 0, useGrandCallNet: true).CallGrandTichu(new DecisionContext(s, 0)), Is.True);
        }
    }
}
