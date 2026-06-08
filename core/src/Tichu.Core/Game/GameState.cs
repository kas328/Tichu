using System.Collections.Generic;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;

namespace Tichu.Core.Game
{
    public sealed class GameState
    {
        public RoundPhase Phase { get; set; }
        public PlayerSeat[] Seats { get; set; }
        public Trick? CurrentTrick { get; set; }
        public int Turn { get; set; }
        public int? Wish { get; set; }
        public ScoreBoard Scores { get; set; }
        public ulong RngSeed { get; set; }
        public Rng Rng { get; set; }

        public GameState()
        {
            Seats = new PlayerSeat[4];
            Scores = new ScoreBoard();
        }

        /// <summary>Deep copy — fully independent from the original.</summary>
        public GameState Clone()
        {
            var clone = new GameState
            {
                Phase = Phase,
                Turn = Turn,
                Wish = Wish,
                RngSeed = RngSeed,
                Rng = Rng  // struct — value copy
            };

            // Deep-copy seats
            clone.Seats = new PlayerSeat[4];
            for (int i = 0; i < 4; i++)
            {
                var src = Seats[i];
                var dst = new PlayerSeat
                {
                    SeatIndex = src.SeatIndex,
                    IsOut = src.IsOut,
                    FinishOrder = src.FinishOrder,
                    Call = src.Call
                };
                dst.Hand.AddRange(src.Hand);
                dst.WonCards.AddRange(src.WonCards);
                clone.Seats[i] = dst;
            }

            // Deep-copy trick
            if (CurrentTrick != null)
            {
                var t = new Trick
                {
                    LeadType = CurrentTrick.LeadType,
                    LeadLength = CurrentTrick.LeadLength,
                    Top = CurrentTrick.Top,
                    TopOwnerSeat = CurrentTrick.TopOwnerSeat,
                    AccumulatedPoints = CurrentTrick.AccumulatedPoints,
                    WonByDragon = CurrentTrick.WonByDragon
                };
                foreach (var p in CurrentTrick.History)
                    t.History.Add(new Play { Seat = p.Seat, Combination = p.Combination, IsBombInterrupt = p.IsBombInterrupt });
                clone.CurrentTrick = t;
            }

            // Deep-copy scoreboard
            var sb = new ScoreBoard
            {
                TeamA = Scores.TeamA,
                TeamB = Scores.TeamB,
                TargetScore = Scores.TargetScore
            };
            foreach (var r in Scores.Rounds)
                sb.Rounds.Add(new RoundResult
                {
                    TeamACardPoints = r.TeamACardPoints,
                    TeamBCardPoints = r.TeamBCardPoints,
                    TeamATichuDelta = r.TeamATichuDelta,
                    TeamBTichuDelta = r.TeamBTichuDelta,
                    TeamATotal = r.TeamATotal,
                    TeamBTotal = r.TeamBTotal
                });
            clone.Scores = sb;

            return clone;
        }

        /// <summary>Deterministic FNV-1a 64-bit hash over a fixed field order.</summary>
        public ulong ComputeHash()
        {
            const ulong Offset = 14695981039346656037UL;
            const ulong Prime = 1099511628211UL;

            ulong h = Offset;

            h = Fnv(h, Prime, (ulong)(int)Phase);
            h = Fnv(h, Prime, (ulong)Turn);
            h = Fnv(h, Prime, Wish.HasValue ? (ulong)Wish.Value : ulong.MaxValue);

            for (int i = 0; i < 4; i++)
            {
                var s = Seats[i];
                h = Fnv(h, Prime, (ulong)s.SeatIndex);
                h = Fnv(h, Prime, s.IsOut ? 1UL : 0UL);
                h = Fnv(h, Prime, (ulong)s.FinishOrder);
                h = Fnv(h, Prime, (ulong)(int)s.Call);
                foreach (var c in s.Hand)
                {
                    h = Fnv(h, Prime, (ulong)c.Rank);
                    h = Fnv(h, Prime, (ulong)(int)c.Suit);
                    h = Fnv(h, Prime, (ulong)(int)c.Special);
                }
                foreach (var c in s.WonCards)
                {
                    h = Fnv(h, Prime, (ulong)c.Rank);
                    h = Fnv(h, Prime, (ulong)(int)c.Suit);
                    h = Fnv(h, Prime, (ulong)(int)c.Special);
                }
            }

            h = Fnv(h, Prime, (ulong)Scores.TeamA);
            h = Fnv(h, Prime, (ulong)Scores.TeamB);
            h = Fnv(h, Prime, (ulong)Scores.TargetScore);
            foreach (var r in Scores.Rounds)
            {
                h = Fnv(h, Prime, (ulong)r.TeamACardPoints);
                h = Fnv(h, Prime, (ulong)r.TeamBCardPoints);
                h = Fnv(h, Prime, (ulong)r.TeamATichuDelta);
                h = Fnv(h, Prime, (ulong)r.TeamBTichuDelta);
                h = Fnv(h, Prime, (ulong)r.TeamATotal);
                h = Fnv(h, Prime, (ulong)r.TeamBTotal);
            }

            if (CurrentTrick != null)
            {
                h = Fnv(h, Prime, (ulong)(int)CurrentTrick.LeadType);
                h = Fnv(h, Prime, (ulong)CurrentTrick.LeadLength);
                h = Fnv(h, Prime, (ulong)CurrentTrick.AccumulatedPoints);
                h = Fnv(h, Prime, CurrentTrick.WonByDragon ? 1UL : 0UL);
                h = Fnv(h, Prime, (ulong)CurrentTrick.TopOwnerSeat);
                if (CurrentTrick.Top != null)
                {
                    h = Fnv(h, Prime, (ulong)(int)CurrentTrick.Top.Type);
                    h = Fnv(h, Prime, (ulong)CurrentTrick.Top.Rank);
                }
            }

            return h;
        }

        private static ulong Fnv(ulong hash, ulong prime, ulong value)
        {
            // FNV-1a: XOR each byte of value then multiply
            for (int b = 0; b < 8; b++)
            {
                hash ^= (value & 0xFF);
                hash *= prime;
                value >>= 8;
            }
            return hash;
        }
    }
}
