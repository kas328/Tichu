namespace Tichu.Core.Game
{
    public sealed class RoundResult
    {
        public int TeamACardPoints { get; set; }
        public int TeamBCardPoints { get; set; }
        public int TeamATichuDelta { get; set; }
        public int TeamBTichuDelta { get; set; }
        public int TeamATotal { get; set; }
        public int TeamBTotal { get; set; }
    }
}
