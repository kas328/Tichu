using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core;
using Tichu.Core.Cards;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    /// <summary>Pimc.Rollout + PimcAgent 탐색 배선 검증.</summary>
    public class PimcAgentTests
    {
        private static GameState FreshPlayState(int turn)
        {
            var deck = Deck.CreateStandard();
            var hands = new IReadOnlyList<Card>[4];
            for (int i = 0; i < 4; i++)
                hands[i] = deck.GetRange(i * 14, 14);
            return GameFlowHelpers.PlayState(turn, hands);
        }

        // ── Rollout 정확성 ───────────────────────────────────────────────────────────

        [Test]
        public void Rollout_completes_without_throwing_and_returns_signed_reward()
        {
            // 결정화한 완전정보 세계를 디폴트 정책으로 끝까지 플레이 → throw 없이 정수 보상.
            var src = FreshPlayState(0);
            var rng = new Rng(11UL);
            var world = Determinizer.Sample(src, 0, ref rng);

            int reward = Pimc.Rollout(world.Clone(), observerSeat: 0, policySeed: 777UL);

            // 한 판 카드점수 합은 100 + 티츄델타라 절대값이 비현실적으로 크지 않다(완주 증거).
            Assert.That(reward, Is.InRange(-600, 600));
        }

        [Test]
        public void Rollout_is_deterministic_for_same_world_and_seed()
        {
            var src = FreshPlayState(0);
            var rng = new Rng(11UL);
            var world = Determinizer.Sample(src, 0, ref rng);

            int a = Pimc.Rollout(world.Clone(), 0, 777UL);
            int b = Pimc.Rollout(world.Clone(), 0, 777UL);
            Assert.That(a, Is.EqualTo(b), "같은 세계+시드 → 같은 보상");
        }

        [Test]
        public void Rollout_reward_sign_is_relative_to_observer_team()
        {
            // 같은 세계를 팀0 관점(seat0)과 팀1 관점(seat1)으로 롤아웃하면 부호가 반대.
            var src = FreshPlayState(0);
            var rng = new Rng(11UL);
            var world = Determinizer.Sample(src, 0, ref rng);

            int teamA = Pimc.Rollout(world.Clone(), observerSeat: 0, policySeed: 777UL);
            int teamB = Pimc.Rollout(world.Clone(), observerSeat: 1, policySeed: 777UL);
            Assert.That(teamA, Is.EqualTo(-teamB), "관측 팀 부호로 보상이 뒤집혀야 한다");
        }

        // ── PimcAgent 탐색 배선 ──────────────────────────────────────────────────────

        [Test]
        public void DecideTurn_returns_a_legal_non_null_move_on_lead()
        {
            var s = FreshPlayState(0);                 // seat0 리드(트릭 없음)
            var ctx = GameFlowHelpers.Context(s, 0);
            var d = new PimcAgent(2024UL, 0).DecideTurn(ctx);

            Assert.That(d.IsPass, Is.False, "리드는 패스 불가 → 수를 내야 한다");
            Assert.That(d.Move, Is.Not.Null);
            // 반환 수는 합법수에 속해야 한다.
            bool legal = false;
            foreach (var m in ctx.LegalMoves)
                if (m.Rank == d.Move!.Rank && m.Type == d.Move!.Type && m.Length == d.Move!.Length) legal = true;
            Assert.That(legal, Is.True, "반환 수는 LegalMoves 안에 있어야 한다");
        }

        [Test]
        public void DecideTurn_is_deterministic_for_same_state_and_seed()
        {
            var s1 = FreshPlayState(0);
            var s2 = FreshPlayState(0);
            var d1 = new PimcAgent(55UL, 0).DecideTurn(GameFlowHelpers.Context(s1, 0));
            var d2 = new PimcAgent(55UL, 0).DecideTurn(GameFlowHelpers.Context(s2, 0));

            Assert.That(d1.IsPass, Is.EqualTo(d2.IsPass));
            if (!d1.IsPass)
            {
                Assert.That(d1.Move!.Rank, Is.EqualTo(d2.Move!.Rank));
                Assert.That(d1.Move!.Type, Is.EqualTo(d2.Move!.Type));
                Assert.That(d1.Move!.Length, Is.EqualTo(d2.Move!.Length));
            }
        }

        [Test]
        public void NonTurn_decisions_delegate_to_heuristic()
        {
            // 강한 8장 → CallGrandTichu 는 휴리스틱과 동일하게 true.
            var strong = new List<Card>
            {
                Card.Dragon, Card.Phoenix,
                Card.Normal(14, Suit.Jade), Card.Normal(14, Suit.Sword),
                Card.Normal(13, Suit.Pagoda), Card.Normal(13, Suit.Star),
                Card.Normal(3, Suit.Jade), Card.Normal(4, Suit.Sword)
            };
            var s = GameFlowHelpers.PlayState(0, strong,
                new List<Card> { Card.Normal(2, Suit.Jade) },
                new List<Card> { Card.Normal(2, Suit.Sword) },
                new List<Card> { Card.Normal(2, Suit.Pagoda) });
            var ctx = GameFlowHelpers.Context(s, 0);

            var pimc = new PimcAgent(1UL, 0).CallGrandTichu(ctx);
            var heur = new AiAgent(1UL, 0).CallGrandTichu(ctx);
            Assert.That(pimc, Is.EqualTo(heur), "비-턴 결정은 휴리스틱에 위임돼야 한다");
        }

        [Test]
        public void Four_PimcAgents_complete_a_round_without_throwing()
        {
            // 단일-세계 탐색이 재귀/폭주 없이 한 라운드를 완주하는지(배선 무결성).
            var s = FreshPlayState(0);
            var agents = new IAgent[]
            {
                new PimcAgent(900UL, 0), new PimcAgent(900UL, 1),
                new PimcAgent(900UL, 2), new PimcAgent(900UL, 3)
            };
            var outcome = new GameDriver(agents).RunRound(s);
            Assert.That(outcome.State.Phase, Is.EqualTo(RoundPhase.RoundEnd));
            Assert.That(outcome.Result, Is.Not.Null);
        }
    }
}
