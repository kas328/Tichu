using NUnit.Framework;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    public class PolicyConfigTests
    {
        [Test]
        public void For_maps_each_difficulty_to_distinct_preset()
        {
            var easy = PolicyConfig.For(Difficulty.Easy);
            var normal = PolicyConfig.For(Difficulty.Normal);

            Assert.That(easy.Worlds, Is.EqualTo(0), "Easy 는 탐색 OFF");
            Assert.That(normal.Worlds, Is.GreaterThan(0), "Normal 은 다세계");
            Assert.That(normal.RolloutsPerWorld, Is.GreaterThan(0));
            Assert.That(easy.Epsilon, Is.GreaterThan(normal.Epsilon), "Easy 가 더 무작위(블런더)");
        }

        [Test]
        public void Worlds_are_monotonic_non_decreasing_across_tiers()
        {
            int e = PolicyConfig.For(Difficulty.Easy).Worlds;
            int n = PolicyConfig.For(Difficulty.Normal).Worlds;
            int h = PolicyConfig.For(Difficulty.Hard).Worlds;
            int x = PolicyConfig.For(Difficulty.Expert).Worlds;
            Assert.That(e, Is.LessThanOrEqualTo(n));
            Assert.That(n, Is.LessThanOrEqualTo(h));
            Assert.That(h, Is.LessThanOrEqualTo(x));
        }

        [Test]
        public void Ctor_sets_fields()
        {
            var c = new PolicyConfig(7, 3, 0.2);
            Assert.That(c.Worlds, Is.EqualTo(7));
            Assert.That(c.RolloutsPerWorld, Is.EqualTo(3));
            Assert.That(c.Epsilon, Is.EqualTo(0.2).Within(1e-9));
            Assert.That(c.UseReachProb, Is.False, "기본 false");
        }

        [Test]
        public void UseReachProb_off_for_all_tiers_verified_ineffective_at_16_worlds()
        {
            // P2-D 검증: reach-prob는 16세계에서도 무효과(재현시 +25/R→−13/R 부호 뒤집힘,
            // 풀링 동률) → Hard/Expert 도 OFF. ReachWeight 코드는 비활성 보존(재실험 여지).
            Assert.That(PolicyConfig.For(Difficulty.Easy).UseReachProb, Is.False);
            Assert.That(PolicyConfig.For(Difficulty.Normal).UseReachProb, Is.False);
            Assert.That(PolicyConfig.For(Difficulty.Hard).UseReachProb, Is.False);
            Assert.That(PolicyConfig.For(Difficulty.Expert).UseReachProb, Is.False);
        }

        [Test]
        public void UseCallerAggression_on_for_pimc_tiers()
        {
            // 콜러 패스억제(+22/R 검증). P2-G 정합성: 강도 양의 전술이라 전 PIMC 티어 ON(티어 역전 방지).
            // Easy(탐색 OFF)는 무의미(false). ctor 기본 false(비트불변 경로).
            Assert.That(PolicyConfig.For(Difficulty.Easy).UseCallerAggression, Is.False);
            Assert.That(PolicyConfig.For(Difficulty.Normal).UseCallerAggression, Is.True);
            Assert.That(PolicyConfig.For(Difficulty.Hard).UseCallerAggression, Is.True);
            Assert.That(PolicyConfig.For(Difficulty.Expert).UseCallerAggression, Is.True);
            Assert.That(new PolicyConfig(4, 2, 0.10).UseCallerAggression, Is.False, "기본 false(비트불변 경로)");
        }

        [Test]
        public void Worlds_form_ascending_ladder_normal_hard_expert()
        {
            // P2-G 정합성(A안): Normal 16 고정, Hard/Expert 상향 사다리(16<20<24). 모두 900ms
            // 관전 딜레이에 흡수돼 체감 무지연. 세계수는 약한 레버지만 단조 증가로 티어 역전을 구조적 차단.
            int n = PolicyConfig.For(Difficulty.Normal).Worlds;
            int h = PolicyConfig.For(Difficulty.Hard).Worlds;
            int x = PolicyConfig.For(Difficulty.Expert).Worlds;
            Assert.That(n, Is.EqualTo(16));
            Assert.That(h, Is.GreaterThan(n), "Hard 는 Normal 보다 많은 세계");
            Assert.That(x, Is.GreaterThan(h), "Expert 는 Hard 보다 많은 세계");
        }

        [Test]
        public void UseOpponentThreatBlock_on_for_pimc_tiers()
        {
            // D1 상대-위협 블록 가드: PIMC 티어(Normal/Hard/Expert) ON(정확성 바닥 — 티어 역전 방지).
            // Easy(탐색 OFF)는 가드 미도달이라 무의미(false). ctor 기본 false(비트불변 경로).
            Assert.That(PolicyConfig.For(Difficulty.Easy).UseOpponentThreatBlock, Is.False, "Easy 는 탐색 OFF라 무관");
            Assert.That(PolicyConfig.For(Difficulty.Normal).UseOpponentThreatBlock, Is.True);
            Assert.That(PolicyConfig.For(Difficulty.Hard).UseOpponentThreatBlock, Is.True);
            Assert.That(PolicyConfig.For(Difficulty.Expert).UseOpponentThreatBlock, Is.True);
            Assert.That(new PolicyConfig(4, 2, 0.10).UseOpponentThreatBlock, Is.False, "기본 false");
        }

        [Test]
        public void Normal_promoted_to_16_world_strong_preset()
        {
            // P2-F: 기본 강화 — 4세계 EV 노이즈 → 16세계. caller(+22/R) 보존.
            var n = PolicyConfig.For(Difficulty.Normal);
            Assert.That(n.Worlds, Is.EqualTo(16), "P2-F 기본 강화: 16세계(EV 노이즈↓)");
            Assert.That(n.RolloutsPerWorld, Is.EqualTo(4));
            Assert.That(n.Epsilon, Is.EqualTo(0.05).Within(1e-9));
            Assert.That(n.UseReachProb, Is.False);
            Assert.That(n.UseCallerAggression, Is.True, "콜러 패스억제(+22/R) 보존");
        }
    }
}
