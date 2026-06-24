using System;

namespace Tichu.Core.Tests.Bench
{
    /// <summary>
    /// AI 벤치 통계. 승률의 Wilson 점수 신뢰구간 하한(연속성 보정 없음).
    /// DoD 게이트: 하한 &gt; 0.5 이면 통계적 유의 우위.
    /// </summary>
    public static class BenchStats
    {
        /// <summary>승률 p̂=wins/n 의 Wilson 95%(기본 z=1.96) 신뢰구간 하한. n==0 → 0.</summary>
        public static double WilsonLowerBound(int wins, int n, double z = 1.96)
        {
            if (n <= 0) return 0.0;
            double phat = (double)wins / n;
            double z2 = z * z;
            double denom = 1.0 + z2 / n;
            double center = (phat + z2 / (2.0 * n)) / denom;
            double margin = (z * Math.Sqrt(phat * (1.0 - phat) / n + z2 / (4.0 * (double)n * n))) / denom;
            double lower = center - margin;
            // 확률 신뢰구간 하한은 [0,1] — 부동소수 반올림(예: -2.8e-17) 클램프.
            return lower < 0.0 ? 0.0 : (lower > 1.0 ? 1.0 : lower);
        }
    }
}
