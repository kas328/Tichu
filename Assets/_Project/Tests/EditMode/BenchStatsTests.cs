using NUnit.Framework;
using Tichu.Core.Tests.Bench;

namespace Tichu.Core.Tests
{
    /// <summary>벤치 통계(Wilson 점수 신뢰구간 하한) 단위 검증 — 순수·결정적(기본 스위트).</summary>
    public class BenchStatsTests
    {
        [Test]
        public void WilsonLowerBound_known_values()
        {
            // 교과서/수계산 값(z=1.96, 95%).
            Assert.That(BenchStats.WilsonLowerBound(21, 30), Is.EqualTo(0.521).Within(0.01), "21/30");
            Assert.That(BenchStats.WilsonLowerBound(50, 100), Is.EqualTo(0.404).Within(0.01), "50/100");
            Assert.That(BenchStats.WilsonLowerBound(60, 100), Is.EqualTo(0.502).Within(0.01), "60/100");
        }

        [Test]
        public void WilsonLowerBound_edges_are_bounded()
        {
            Assert.That(BenchStats.WilsonLowerBound(0, 10), Is.GreaterThanOrEqualTo(0.0).And.LessThan(0.2));
            Assert.That(BenchStats.WilsonLowerBound(10, 10), Is.GreaterThan(0.65).And.LessThanOrEqualTo(1.0));
        }

        [Test]
        public void WilsonLowerBound_zero_n_is_zero()
        {
            Assert.That(BenchStats.WilsonLowerBound(0, 0), Is.EqualTo(0.0));
        }
    }
}
