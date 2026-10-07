using NUnit.Framework;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// 난이도별 1수 예산표. 서버와 유니티가 같은 표를 쓴다(티어 사다리 불변식을 두 벌로 들지 않는다).
    /// 값은 P2-G 티어 정합성 결정(Easy=탐색OFF, 나머지 900 통일 — 관전 지연과 겹쳐 체감 0)이다.
    /// </summary>
    public class PimcBudgetTests
    {
        [TestCase(Difficulty.Easy, 0)]
        [TestCase(Difficulty.Normal, 900)]
        [TestCase(Difficulty.Hard, 900)]
        [TestCase(Difficulty.Expert, 900)]
        public void Budget_matches_tier_table(Difficulty d, int expectedMs)
        {
            Assert.That(PimcBudget.For(d), Is.EqualTo(expectedMs));
        }

        [Test]
        public void Budget_is_non_decreasing_across_tiers()
        {
            Assert.That(PimcBudget.For(Difficulty.Easy), Is.LessThanOrEqualTo(PimcBudget.For(Difficulty.Normal)));
            Assert.That(PimcBudget.For(Difficulty.Normal), Is.LessThanOrEqualTo(PimcBudget.For(Difficulty.Hard)));
            Assert.That(PimcBudget.For(Difficulty.Hard), Is.LessThanOrEqualTo(PimcBudget.For(Difficulty.Expert)));
        }
    }
}
