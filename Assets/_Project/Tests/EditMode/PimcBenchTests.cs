using NUnit.Framework;
using Tichu.Core.Tests.Bench;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// AI-vs-AI 벤치(무거움). ⚠️[Explicit]+[Category("Bench")] — 기본 스위트에서 절대 실행 안 됨
    /// (메인스레드 점유 → MCP-stuck 회피). Unity Test Runner 에서 명시 선택해 실행하거나
    /// 헤드리스/백그라운드로 돌린다. 결과는 TestContext 로그로 보고.
    /// </summary>
    public class PimcBenchTests
    {
        [Test, Explicit, Category("Bench")]
        public void Normal_pimc_vs_heuristic_mirrored()
        {
            const int pairs = 25; // 50 라운드(~7분, 8초/라운드)
            var r = PimcBench.RunMirrored(pairs, 100000UL, PolicyConfig.Normal);
            double avg = (double)r.PimcDiffSum / r.Rounds;
            double wilsonLower = BenchStats.WilsonLowerBound(r.PimcWins, r.Rounds);

            TestContext.WriteLine(
                $"[Bench] Normal PIMC vs AiAgent | rounds={r.Rounds} avgDiff={avg:F1} " +
                $"wins={r.PimcWins}/{r.Rounds} ({100.0 * r.PimcWins / r.Rounds:F1}%) wilsonLower={wilsonLower:F3}");

            // 안전 게이트(낮은 N에서도 견고): 평균 우세. DoD(wilsonLower>0.5)는 로그로 판단/대량 확정.
            Assert.That(avg, Is.GreaterThan(0.0), "PIMC 평균 팀점수차가 양수여야(휴리스틱 우위)");
        }
    }
}
