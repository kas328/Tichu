#nullable enable
using Tichu.Core.Combinations;

namespace Tichu.Core.Game
{
    /// <summary>트릭 내 한 번의 제출 로그 엔트리.</summary>
    public sealed class Play
    {
        public int Seat { get; set; }
        public Combination? Combination { get; set; }
        public bool IsBombInterrupt { get; set; }
    }
}
