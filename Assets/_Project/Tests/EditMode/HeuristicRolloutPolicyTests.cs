using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Game;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    public class HeuristicRolloutPolicyTests
    {
        private static List<Card> Hand(params Card[] c) => new List<Card>(c);
        private static Card N(int r, Suit s) => Card.Normal(r, s);

        // seat0 리드 상태(폭탄 없는 평이한 손).
        private static GameState LeadState()
        {
            return GameFlowHelpers.PlayState(0,
                Hand(N(3, Suit.Jade), N(5, Suit.Sword), N(9, Suit.Pagoda), N(11, Suit.Star)),
                Hand(N(4, Suit.Jade), N(6, Suit.Sword)),
                Hand(N(7, Suit.Jade), N(8, Suit.Sword)),
                Hand(N(2, Suit.Jade), N(10, Suit.Sword)));
        }

        [Test]
        public void Epsilon_zero_matches_AiAgent_decide_turn()
        {
            var s = LeadState();
            var ctx = GameFlowHelpers.Context(s, 0);
            var pol = new HeuristicRolloutPolicy(123UL, 0, 0.0);
            var ai = new AiAgent(123UL, 0);

            var dp = pol.DecideTurn(ctx);
            var da = ai.DecideTurn(ctx);
            Assert.That(dp.IsPass, Is.EqualTo(da.IsPass));
            Assert.That(dp.Move!.Rank, Is.EqualTo(da.Move!.Rank));
            Assert.That(dp.Move!.Type, Is.EqualTo(da.Move!.Type));
            Assert.That(dp.Move!.Length, Is.EqualTo(da.Move!.Length));
        }

        [Test]
        public void Epsilon_one_always_returns_a_legal_move_or_pass()
        {
            var s = LeadState();
            var ctx = GameFlowHelpers.Context(s, 0);
            var pol = new HeuristicRolloutPolicy(999UL, 0, 1.0);

            for (int i = 0; i < 50; i++)
            {
                var d = pol.DecideTurn(ctx);
                if (d.IsPass)
                {
                    Assert.That(ctx.CanPass, Is.True, "패스를 골랐으면 패스가 합법이어야 한다");
                }
                else
                {
                    bool legal = false;
                    foreach (var m in ctx.LegalMoves)
                        if (m.Rank == d.Move!.Rank && m.Type == d.Move!.Type && m.Length == d.Move!.Length) legal = true;
                    Assert.That(legal, Is.True, "무작위 수도 반드시 합법");
                }
            }
        }

        [Test]
        public void Is_deterministic_for_same_seed()
        {
            var s1 = LeadState();
            var s2 = LeadState();
            var a = new HeuristicRolloutPolicy(77UL, 0, 0.5);
            var b = new HeuristicRolloutPolicy(77UL, 0, 0.5);
            for (int i = 0; i < 20; i++)
            {
                var da = a.DecideTurn(GameFlowHelpers.Context(s1, 0));
                var db = b.DecideTurn(GameFlowHelpers.Context(s2, 0));
                Assert.That(da.IsPass, Is.EqualTo(db.IsPass));
                if (!da.IsPass)
                    Assert.That(da.Move!.Rank, Is.EqualTo(db.Move!.Rank));
            }
        }

        [Test]
        public void Non_turn_decisions_delegate_to_AiAgent()
        {
            // 강한 8장 → CallGrandTichu 위임 결과가 AiAgent 와 동일.
            var strong = Hand(Card.Dragon, Card.Phoenix,
                N(14, Suit.Jade), N(14, Suit.Sword), N(13, Suit.Pagoda), N(13, Suit.Star),
                N(3, Suit.Jade), N(4, Suit.Sword));
            var s = GameFlowHelpers.PlayState(0, strong,
                Hand(N(2, Suit.Jade)), Hand(N(2, Suit.Sword)), Hand(N(2, Suit.Pagoda)));
            var ctx = GameFlowHelpers.Context(s, 0);
            Assert.That(new HeuristicRolloutPolicy(1UL, 0, 0.9).CallGrandTichu(ctx),
                        Is.EqualTo(new AiAgent(1UL, 0).CallGrandTichu(ctx)));
        }
    }
}
