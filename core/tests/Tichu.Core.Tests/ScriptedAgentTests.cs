using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Game;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    /// <summary>ScriptedAgent 기본값과 주입 오버라이드, GameFlowHelpers 스모크 테스트.</summary>
    [TestFixture]
    public class ScriptedAgentTests
    {
        private static readonly ScriptedAgent DefaultAgent = new ScriptedAgent();

        // ── 큰 티츄 / 작은 티츄 기본값 ─────────────────────────────────────────────

        [Test]
        public void Default_declines_grand_tichu()
        {
            var s = GameFlowHelpers.GrandState(
                Hand(Card.Normal(2, Suit.Jade), Card.Normal(3, Suit.Jade),
                     Card.Normal(4, Suit.Jade), Card.Normal(5, Suit.Jade),
                     Card.Normal(6, Suit.Jade), Card.Normal(7, Suit.Jade),
                     Card.Normal(8, Suit.Jade), Card.Normal(9, Suit.Jade)),
                Hand(Card.Normal(2, Suit.Sword), Card.Normal(3, Suit.Sword),
                     Card.Normal(4, Suit.Sword), Card.Normal(5, Suit.Sword),
                     Card.Normal(6, Suit.Sword), Card.Normal(7, Suit.Sword),
                     Card.Normal(8, Suit.Sword), Card.Normal(9, Suit.Sword)),
                Hand(Card.Normal(2, Suit.Pagoda), Card.Normal(3, Suit.Pagoda),
                     Card.Normal(4, Suit.Pagoda), Card.Normal(5, Suit.Pagoda),
                     Card.Normal(6, Suit.Pagoda), Card.Normal(7, Suit.Pagoda),
                     Card.Normal(8, Suit.Pagoda), Card.Normal(9, Suit.Pagoda)),
                Hand(Card.Normal(2, Suit.Star), Card.Normal(3, Suit.Star),
                     Card.Normal(4, Suit.Star), Card.Normal(5, Suit.Star),
                     Card.Normal(6, Suit.Star), Card.Normal(7, Suit.Star),
                     Card.Normal(8, Suit.Star), Card.Normal(9, Suit.Star)));

            var ctx = GameFlowHelpers.Context(s, 0);
            Assert.That(DefaultAgent.CallGrandTichu(in ctx), Is.False);
        }

        [Test]
        public void Default_declines_small_tichu()
        {
            var s = GameFlowHelpers.PlayState(0,
                Hand(Card.Normal(9, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            var ctx = GameFlowHelpers.Context(s, 0);
            Assert.That(DefaultAgent.CallTichu(in ctx), Is.False);
        }

        // ── DecideTurn 기본값 ──────────────────────────────────────────────────────

        [Test]
        public void Default_decide_turn_leads_first_legal_on_lead()
        {
            // seat0 의 턴, CurrentTrick==null(리드) → LegalMoves 가 있으면 Play 반환.
            var s = GameFlowHelpers.PlayState(0,
                Hand(Card.Normal(9, Suit.Jade), Card.Normal(7, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            var ctx = GameFlowHelpers.Context(s, 0);
            Assert.That(ctx.LegalMoves.Count, Is.GreaterThan(0), "리드 상황에 합법수 있어야");
            var decision = DefaultAgent.DecideTurn(in ctx);
            Assert.That(decision.IsPass, Is.False, "리드는 Pass 불가");
            Assert.That(decision.Move, Is.Not.Null);
        }

        [Test]
        public void Default_decide_turn_passes_when_following_and_can_pass()
        {
            // seat0 이 9를 리드한 뒤 seat1 의 차례: CanPass=true, 기본은 Pass.
            var s = GameFlowHelpers.PlayState(0,
                Hand(Card.Normal(9, Suit.Jade), Card.Normal(2, Suit.Pagoda)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            // seat0 가 9를 내면 트릭이 생기고 Turn=1 이 된다.
            GameEngine.Apply(s, GameAction.Play(0, Hand(Card.Normal(9, Suit.Jade))));
            Assert.That(s.CurrentTrick, Is.Not.Null);
            Assert.That(s.Turn, Is.EqualTo(1));

            var ctx = GameFlowHelpers.Context(s, 1);
            Assert.That(ctx.CanPass, Is.True);
            var decision = DefaultAgent.DecideTurn(in ctx);
            Assert.That(decision.IsPass, Is.True);
        }

        // ── ChooseExchange 기본값 ─────────────────────────────────────────────────

        [Test]
        public void Default_exchange_returns_three_distinct_cards_from_hand()
        {
            // GrandState 로 교환 페이즈 직전(GrandTichuDecision) 상태를 얻어
            // DecisionContext 로 테스트한다.
            var s = GameFlowHelpers.GrandState(
                Hand(Card.Normal(2, Suit.Jade), Card.Normal(3, Suit.Jade),
                     Card.Normal(4, Suit.Jade), Card.Normal(5, Suit.Jade),
                     Card.Normal(6, Suit.Jade), Card.Normal(7, Suit.Jade),
                     Card.Normal(8, Suit.Jade), Card.Normal(9, Suit.Jade)),
                Hand(Card.Normal(2, Suit.Sword), Card.Normal(3, Suit.Sword),
                     Card.Normal(4, Suit.Sword), Card.Normal(5, Suit.Sword),
                     Card.Normal(6, Suit.Sword), Card.Normal(7, Suit.Sword),
                     Card.Normal(8, Suit.Sword), Card.Normal(9, Suit.Sword)),
                Hand(Card.Normal(2, Suit.Pagoda), Card.Normal(3, Suit.Pagoda),
                     Card.Normal(4, Suit.Pagoda), Card.Normal(5, Suit.Pagoda),
                     Card.Normal(6, Suit.Pagoda), Card.Normal(7, Suit.Pagoda),
                     Card.Normal(8, Suit.Pagoda), Card.Normal(9, Suit.Pagoda)),
                Hand(Card.Normal(2, Suit.Star), Card.Normal(3, Suit.Star),
                     Card.Normal(4, Suit.Star), Card.Normal(5, Suit.Star),
                     Card.Normal(6, Suit.Star), Card.Normal(7, Suit.Star),
                     Card.Normal(8, Suit.Star), Card.Normal(9, Suit.Star)));

            var ctx = GameFlowHelpers.Context(s, 0);
            var choice = DefaultAgent.ChooseExchange(in ctx);

            // 셋 모두 손패에 있어야 한다.
            Assert.That(ctx.MyHand, Does.Contain(choice.ToLeft));
            Assert.That(ctx.MyHand, Does.Contain(choice.ToPartner));
            Assert.That(ctx.MyHand, Does.Contain(choice.ToRight));

            // 셋 모두 서로 달라야 한다.
            Assert.That(choice.ToLeft,    Is.Not.EqualTo(choice.ToPartner));
            Assert.That(choice.ToPartner, Is.Not.EqualTo(choice.ToRight));
            Assert.That(choice.ToLeft,    Is.Not.EqualTo(choice.ToRight));
        }

        // ── DecideBomb 기본값 ─────────────────────────────────────────────────────

        [Test]
        public void Default_decide_bomb_is_null()
        {
            var s = GameFlowHelpers.PlayState(0,
                Hand(Card.Normal(6, Suit.Jade), Card.Normal(6, Suit.Sword),
                     Card.Normal(6, Suit.Pagoda), Card.Normal(6, Suit.Star)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            var ctx = GameFlowHelpers.Context(s, 0);
            Assert.That(DefaultAgent.DecideBomb(in ctx), Is.Null);
        }

        // ── 주입 오버라이드 ────────────────────────────────────────────────────────

        [Test]
        public void Injected_fn_overrides_default_for_dragon_recipient()
        {
            var agent = new ScriptedAgent
            {
                ChooseDragonRecipientFn = ctx => ctx.RightSeat
            };

            var s = GameFlowHelpers.PlayState(0,
                Hand(Card.Normal(9, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            var ctx = GameFlowHelpers.Context(s, 0);
            Assert.That(agent.ChooseDragonRecipient(in ctx), Is.EqualTo(ctx.RightSeat));
        }

        [Test]
        public void Injected_grand_tichu_fn_overrides_default()
        {
            var agent = new ScriptedAgent { CallGrandTichuFn = _ => true };
            var s = GameFlowHelpers.PlayState(0,
                Hand(Card.Normal(9, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            var ctx = GameFlowHelpers.Context(s, 0);
            Assert.That(agent.CallGrandTichu(in ctx), Is.True);
        }

        // ── GameFlowHelpers 스모크 테스트 ──────────────────────────────────────────

        [Test]
        public void PlayState_builds_correct_phase_turn_hands()
        {
            var h0 = Hand(Card.Normal(9,  Suit.Jade));
            var h1 = Hand(Card.Normal(3,  Suit.Jade));
            var h2 = Hand(Card.Normal(4,  Suit.Jade));
            var h3 = Hand(Card.Normal(5,  Suit.Jade));

            var s = GameFlowHelpers.PlayState(turn: 0, h0, h1, h2, h3);

            Assert.That(s.Phase,        Is.EqualTo(RoundPhase.Play));
            Assert.That(s.Turn,         Is.EqualTo(0));
            Assert.That(s.CurrentTrick, Is.Null);
            // Play 페이즈에서 Setup은 null — GameEngine.Apply(Play)의 불변식이다.
            // 테스트 어셈블리에서는 internal Setup에 직접 접근 불가 → LegalMoves 정상 작동으로 간접 확인.
            Assert.That(s.Seats[0].Hand, Is.EquivalentTo(h0));
            Assert.That(s.Seats[1].Hand, Is.EquivalentTo(h1));
            Assert.That(s.Seats[2].Hand, Is.EquivalentTo(h2));
            Assert.That(s.Seats[3].Hand, Is.EquivalentTo(h3));
            // LegalMoves 호출이 예외 없이 작동하면 상태가 유효하다.
            var ctx = GameFlowHelpers.Context(s, 0);
            Assert.That(ctx.LegalMoves, Is.Not.Null);
        }

        [Test]
        public void GrandState_builds_grand_tichu_decision_phase()
        {
            var hand = Hand(Card.Normal(2, Suit.Jade), Card.Normal(3, Suit.Jade),
                            Card.Normal(4, Suit.Jade), Card.Normal(5, Suit.Jade),
                            Card.Normal(6, Suit.Jade), Card.Normal(7, Suit.Jade),
                            Card.Normal(8, Suit.Jade), Card.Normal(9, Suit.Jade));

            var s = GameFlowHelpers.GrandState(hand, hand, hand, hand);

            Assert.That(s.Phase, Is.EqualTo(RoundPhase.GrandTichuDecision));
            // GrandTichuDecision 단계: Setup은 internal이라 직접 접근 불가.
            // 유효성은 GameEngine.Apply(DeclineGrandTichu) 수락으로 간접 확인.
            var result = GameEngine.Apply(s, GameAction.DeclineGrandTichu(0));
            Assert.That(result.Ok, Is.True, "GrandTichuDecision 페이즈 액션이 수락되어야");
            for (int i = 0; i < 4; i++)
                Assert.That(s.Seats[i].Hand.Count, Is.GreaterThanOrEqualTo(8));
        }

        // ── 보조 ────────────────────────────────────────────────────────────────

        private static List<Card> Hand(params Card[] cards) => new List<Card>(cards);
    }
}
