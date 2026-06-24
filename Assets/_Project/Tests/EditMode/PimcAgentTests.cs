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
    }
}
