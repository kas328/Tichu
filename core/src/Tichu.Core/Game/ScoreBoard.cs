using System.Collections.Generic;

namespace Tichu.Core.Game
{
    public sealed class ScoreBoard
    {
        public int TeamA { get; set; }
        public int TeamB { get; set; }
        public int TargetScore { get; set; }
        public List<RoundResult> Rounds { get; set; }

        public ScoreBoard()
        {
            TargetScore = 1000;
            Rounds = new List<RoundResult>();
        }
    }
}
