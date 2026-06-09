#nullable enable
using System.Collections.Generic;
using Tichu.Core.Cards;

namespace Tichu.Core.Game
{
    public sealed class PlayerSeat
    {
        public int SeatIndex { get; set; }
        public List<Card> Hand { get; set; }
        public bool IsOut { get; set; }
        /// <summary>1=first out, 2=second out, 0=not yet out.</summary>
        public int FinishOrder { get; set; }
        public TichuCall Call { get; set; }
        public List<Card> WonCards { get; set; }

        public PlayerSeat()
        {
            Hand = new List<Card>();
            WonCards = new List<Card>();
        }
    }
}
