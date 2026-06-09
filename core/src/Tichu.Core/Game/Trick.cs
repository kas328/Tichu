using System.Collections.Generic;
using Tichu.Core.Combinations;

namespace Tichu.Core.Game
{
    public sealed class Trick
    {
        public CombinationType LeadType { get; set; }
        public int LeadLength { get; set; }
        public Combination? Top { get; set; }
        public int TopOwnerSeat { get; set; }
        public List<Play> History { get; set; }
        public int AccumulatedPoints { get; set; }
        public bool WonByDragon { get; set; }
        /// <summary>용 단독으로 이긴 트릭의 점수를 양도받을 상대 좌석. 양도 전까지 null.</summary>
        public int? DragonGiftRecipient { get; set; }

        public Trick()
        {
            History = new List<Play>();
        }
    }
}
