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

        public Trick()
        {
            History = new List<Play>();
        }
    }
}
