using System.Collections.Generic;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;

namespace Tichu.Core.Game
{
    public sealed class GameState
    {
        private const int SeatCount = 4;
        public RoundPhase Phase { get; set; }
        public PlayerSeat[] Seats { get; set; }
        public Trick? CurrentTrick { get; set; }
        /// <summary>완료되어 회수된 트릭들. Task 6의 점수 분배/용 양도 계산에 사용.</summary>
        public List<Trick> CompletedTricks { get; set; }
        public int Turn { get; set; }
        public int? Wish { get; set; }
        public ScoreBoard Scores { get; set; }
        public ulong RngSeed { get; set; }
        public Rng Rng { get; set; }

        /// <summary>셋업 페이즈(Deal8~Exchange 완료 전)의 임시 상태. Play 진입 후 null.</summary>
        internal RoundSetup? Setup { get; set; }

        public GameState()
        {
            Seats = new PlayerSeat[SeatCount];
            Scores = new ScoreBoard();
            CompletedTricks = new List<Trick>();
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
            clone.Seats = new PlayerSeat[SeatCount];
            for (int i = 0; i < SeatCount; i++)
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
                clone.CurrentTrick = CloneTrick(CurrentTrick);

            // Deep-copy completed tricks
            clone.CompletedTricks = new List<Trick>(CompletedTricks.Count);
            foreach (var t in CompletedTricks)
                clone.CompletedTricks.Add(CloneTrick(t));

            // Deep-copy setup (transient; null during Play)
            clone.Setup = Setup?.Clone();

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

            for (int i = 0; i < SeatCount; i++)
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

            h = Fnv(h, Prime, (ulong)CompletedTricks.Count);
            foreach (var t in CompletedTricks)
            {
                h = Fnv(h, Prime, (ulong)t.TopOwnerSeat);
                h = Fnv(h, Prime, (ulong)t.AccumulatedPoints);
                h = Fnv(h, Prime, t.WonByDragon ? 1UL : 0UL);
            }

            return h;
        }

        private static Trick CloneTrick(Trick src)
        {
            var t = new Trick
            {
                LeadType = src.LeadType,
                LeadLength = src.LeadLength,
                Top = src.Top,
                TopOwnerSeat = src.TopOwnerSeat,
                AccumulatedPoints = src.AccumulatedPoints,
                WonByDragon = src.WonByDragon
            };
            foreach (var p in src.History)
                t.History.Add(new Play { Seat = p.Seat, Combination = p.Combination, IsBombInterrupt = p.IsBombInterrupt });
            return t;
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
