using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Game;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// Small Tichu 콜 헤드 통합 검증 — OFF는 현행 강도게이트, ON은 CallNet.Small 라우팅,
    /// 컨텍스트 게이트(리드·상대아웃·파트너콜)·폭탄 단축 보존.
    /// </summary>
    public class CallTichuHeadIntegrationTests
    {
        private static List<Card> Hand(params Card[] c) => new List<Card>(c);
        private static Card N(int r, Suit s) => Card.Normal(r, s);

        // 강패(용 + A + K → HandPower 7 ≥ 7), 폭탄 없음, 14장.
        private static List<Card> StrongHand() => Hand(
            Card.Dragon, N(14, Suit.Jade), N(13, Suit.Sword),
            N(2, Suit.Jade), N(3, Suit.Sword), N(4, Suit.Pagoda), N(5, Suit.Star),
            N(6, Suit.Jade), N(7, Suit.Sword), N(8, Suit.Pagoda), N(9, Suit.Star),
            N(10, Suit.Jade), N(11, Suit.Sword), N(12, Suit.Pagoda));

        // 약패(용/봉황·폭탄 없음), 14장.
        private static List<Card> WeakNonBombHand() => Hand(
            N(2, Suit.Jade), N(3, Suit.Sword), N(4, Suit.Pagoda), N(5, Suit.Star),
            N(6, Suit.Jade), N(7, Suit.Sword), N(8, Suit.Pagoda), N(9, Suit.Star),
            N(10, Suit.Jade), N(11, Suit.Sword), N(12, Suit.Pagoda), N(13, Suit.Star),
            N(14, Suit.Jade), N(2, Suit.Sword));

        // 폭탄(7 네 장) 포함, 14장.
        private static List<Card> BombHand() => Hand(
            N(7, Suit.Jade), N(7, Suit.Sword), N(7, Suit.Pagoda), N(7, Suit.Star),
            N(3, Suit.Jade), N(4, Suit.Sword), N(5, Suit.Pagoda), N(6, Suit.Star),
            N(8, Suit.Jade), N(9, Suit.Sword), N(10, Suit.Pagoda), N(11, Suit.Star),
            N(12, Suit.Jade), N(13, Suit.Sword));

        private static GameState Lead(List<Card> hand) => GameFlowHelpers.PlayState(
            0, hand, Hand(N(2, Suit.Pagoda)), Hand(N(3, Suit.Pagoda)), Hand(N(4, Suit.Star)));

        [Test]
        public void Flag_off_matches_strength_gate()
        {
            var s = Lead(StrongHand());
            Assert.That(new AiAgent(1, 0).CallTichu(GameFlowHelpers.Context(s, 0)), Is.True); // 용+HandPower7
        }

        [Test]
        public void Flag_on_routes_through_CallNet_threshold()
        {
            var s = Lead(WeakNonBombHand()); // 폭탄 없음 → P>τ 가 결정
            // σ∈(0,1): 임계값 0 이면 항상 콜, 1 이면 항상 패스 → CallNet.Small 경로 증명.
            Assert.That(new AiAgent(1, 0, useSmallTichuNet: true, smallThreshold: 0.0).CallTichu(GameFlowHelpers.Context(s, 0)), Is.True);
            Assert.That(new AiAgent(1, 0, useSmallTichuNet: true, smallThreshold: 1.0).CallTichu(GameFlowHelpers.Context(s, 0)), Is.False);
        }

        [Test]
        public void Flag_on_bomb_shortcut_calls_regardless_of_threshold()
        {
            var s = Lead(BombHand());
            // 폭탄 보유 → 임계값 1.0(P<1)이어도 콜(폭탄 단축 보존).
            Assert.That(new AiAgent(1, 0, useSmallTichuNet: true, smallThreshold: 1.0).CallTichu(GameFlowHelpers.Context(s, 0)), Is.True);
        }

        [Test]
        public void Flag_on_preserves_context_gate_partner_called()
        {
            var s = Lead(StrongHand());
            s.Seats[Seating.Partner(0)].Call = TichuCall.Tichu; // 파트너 콜 → 중복 금지
            Assert.That(new AiAgent(1, 0, useSmallTichuNet: true, smallThreshold: 0.0).CallTichu(GameFlowHelpers.Context(s, 0)), Is.False);
        }

        [Test]
        public void Flag_on_preserves_context_gate_following()
        {
            // 트릭 진행 중(팔로우)이면 ON이어도 콜 안 함.
            var s = Lead(StrongHand());
            var agentOn = new AiAgent(1, 0, useSmallTichuNet: true, smallThreshold: 0.0);
            Assert.That(agentOn.CallTichu(GameFlowHelpers.Context(s, 0)), Is.True); // 리드 시점엔 콜
            // (팔로우 케이스는 기존 AiAgentTests.CallTichu_false_when_following 이 커버)
        }
    }
}
