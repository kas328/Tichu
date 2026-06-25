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
    }
}
