using System.Collections.Generic;
using Tichu.Core.Cards;
using Tichu.Core.Game;

namespace Tichu.Net
{
    /// <summary>
    /// 권위 GameState 를 한 좌석의 시야로 투영한다. 서버만 호출한다.
    /// 반환값은 깊은 복사된 값 스냅샷이라, 이후 서버 상태 변경이 이미 보낸 뷰에 새지 않는다
    /// (로컬 루프백에서 참조 공유로 마스킹이 무력화되는 경로를 막는다).
    /// </summary>
    public static class ViewProjector
    {
        private const int SeatCount = 4;

        public static PlayerView ProjectFor(GameState s, int viewer)
        {
            var v = new PlayerView
            {
                Viewer = viewer,
                Phase = s.Phase,
                Turn = s.Turn,
                Wish = s.Wish,
                TeamAScore = s.Scores.TeamA,
                TeamBScore = s.Scores.TeamB,
                TargetScore = s.Scores.TargetScore,
                DragonGiftWinner = s.TryGetPendingDragonGift(out int w) ? w : (int?)null,
                MyHand = new List<Card>(s.Seats[viewer].Hand),
            };

            for (int i = 0; i < SeatCount; i++)
            {
                var seat = s.Seats[i];
                v.HandCounts[i] = seat.Hand.Count;
                v.Calls[i] = seat.Call;
                v.IsOut[i] = seat.IsOut;
                v.FinishOrder[i] = seat.FinishOrder;
                v.WonCards[i] = new List<Card>(seat.WonCards);
            }

            v.CurrentTrick = s.CurrentTrick == null ? null : ProjectTrick(s.CurrentTrick);

            v.CompletedTricks = new List<TrickSummary>(s.CompletedTricks.Count);
            foreach (var t in s.CompletedTricks)
                v.CompletedTricks.Add(new TrickSummary
                {
                    TopOwnerSeat = t.TopOwnerSeat,
                    AccumulatedPoints = t.AccumulatedPoints,
                    WonByDragon = t.WonByDragon,
                    DragonGiftRecipient = t.DragonGiftRecipient,
                });

            return v;
        }

        private static TrickView ProjectTrick(Trick t)
        {
            var tv = new TrickView
            {
                LeadType = t.LeadType,
                LeadLength = t.LeadLength,
                TopOwnerSeat = t.TopOwnerSeat,
                AccumulatedPoints = t.AccumulatedPoints,
            };

            if (t.Top != null)
            {
                tv.TopType = t.Top.Type;
                tv.TopRank = t.Top.Rank;
                tv.TopLength = t.Top.Length;
                tv.TopCards = new List<Card>(t.Top.Cards);
            }

            tv.History = new List<PlayView>(t.History.Count);
            foreach (var p in t.History)
            {
                var pv = new PlayView
                {
                    Seat = p.Seat,
                    IsPass = p.Combination == null,
                    IsBombInterrupt = p.IsBombInterrupt,
                };
                if (p.Combination != null) pv.Cards = new List<Card>(p.Combination.Cards);
                tv.History.Add(pv);
            }

            return tv;
        }
    }
}
