using System;

namespace Tichu.Core
{
    /// <summary>시드 주입식 결정적 PRNG(SplitMix64). 서버/클라/AI/리플레이 비트 동일성 보장.</summary>
    public struct Rng
    {
        private ulong _state;

        public Rng(ulong seed) { _state = seed; }

        public ulong NextULong()
        {
            _state += 0x9E3779B97F4A7C15UL;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>[0, maxExclusive) 정수. 셔플용(56장 규모에서 모듈로 편향 무시 가능).</summary>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            return (int)(NextULong() % (ulong)maxExclusive);
        }
    }
}
