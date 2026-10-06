using System.Collections.Generic;
using Tichu.Core.Game;

namespace Tichu.Net
{
    /// <summary>
    /// 서버 좌표 ↔ 클라이언트 로컬 좌표 변환. 테이블 UI 는 "나 = 좌석 0"(아래), 1 = 오른쪽,
    /// 2 = 파트너, 3 = 왼쪽을 가정하고 팀 A 를 우리 팀으로 그린다. 서버는 절대 좌석을 쓴다.
    /// 클라이언트 경계(TichuClient)에서 받을 때 ToLocal, 보낼 때 ToServer 를 적용하면
    /// 뷰모델·뷰는 어느 좌석에 앉든 좌석 0 경로 하나로 동작한다.
    ///
    /// 회전: local = (abs − mySeat + 4) % 4. 음수(예: RecipientSeat = −1 "없음")는 그대로.
    /// 내 좌석이 홀수면 로컬 팀 A(0·2) = 서버 팀 B 이므로 팀 단위 값도 맞바꾼다.
    /// 입력 객체는 건드리지 않고 새 객체를 만든다(같은 이벤트를 다른 좌석이 볼 수 있다).
    /// Lobby.YourSeat 는 회전하지 않는다 — 대기실이 회전 전에 읽는 절대 좌석 배정이다.
    /// </summary>
    public static class SeatRotation
    {
        private const int SeatCount = 4;

        public static int ToLocalSeat(int abs, int mySeat) => abs < 0 ? abs : (abs - mySeat + SeatCount) % SeatCount;

        public static int ToServerSeat(int local, int mySeat) => local < 0 ? local : (local + mySeat) % SeatCount;

        public static ServerEvent ToLocal(ServerEvent ev, int mySeat)
        {
            if (mySeat == 0) return ev;
            bool flip = (mySeat & 1) == 1;

            return new ServerEvent
            {
                Kind = ev.Kind,
                Reason = ev.Reason,
                ActionSeq = ev.ActionSeq,
                View = ev.View == null ? null : RotateView(ev.View, mySeat, flip),
                Round = ev.Round == null ? null : (flip ? Flip(ev.Round) : ev.Round),
                Applied = ev.Applied == null ? null : RotateApplied(ev.Applied, mySeat),
                WinningTeam = flip && ev.Kind == ServerEventKind.MatchEnded ? 1 - ev.WinningTeam : ev.WinningTeam,
                TeamA = flip ? ev.TeamB : ev.TeamA,
                TeamB = flip ? ev.TeamA : ev.TeamB,
            };
        }

        public static ClientAction ToServer(ClientAction a, int mySeat)
        {
            if (mySeat == 0) return a;

            return new ClientAction
            {
                Kind = a.Kind,
                Seat = ToServerSeat(a.Seat, mySeat),
                ActionSeq = a.ActionSeq,
                Cards = a.Cards,
                Rank = a.Rank,
                Wish = a.Wish,
                ToLeft = a.ToLeft,          // 교환 대상은 원래 상대 좌표(왼쪽/파트너/오른쪽)다
                ToPartner = a.ToPartner,
                ToRight = a.ToRight,
                RecipientSeat = a.Kind == ClientActionKind.GiveDragon
                    ? ToServerSeat(a.RecipientSeat, mySeat)
                    : a.RecipientSeat,
            };
        }

        private static PlayerView RotateView(PlayerView v, int my, bool flip)
        {
            var completed = new List<TrickSummary>(v.CompletedTricks.Count);
            foreach (var t in v.CompletedTricks)
                completed.Add(new TrickSummary
                {
                    TopOwnerSeat = ToLocalSeat(t.TopOwnerSeat, my),
                    AccumulatedPoints = t.AccumulatedPoints,
                    WonByDragon = t.WonByDragon,
                    DragonGiftRecipient = t.DragonGiftRecipient.HasValue
                        ? ToLocalSeat(t.DragonGiftRecipient.Value, my) : (int?)null,
                });

            return new PlayerView
            {
                Viewer = ToLocalSeat(v.Viewer, my),
                Phase = v.Phase,
                Turn = ToLocalSeat(v.Turn, my),
                Wish = v.Wish,
                DragonGiftWinner = v.DragonGiftWinner.HasValue ? ToLocalSeat(v.DragonGiftWinner.Value, my) : (int?)null,
                MyHand = v.MyHand,
                HandCounts = Permute(v.HandCounts, my),
                Calls = Permute(v.Calls, my),
                IsOut = Permute(v.IsOut, my),
                FinishOrder = Permute(v.FinishOrder, my),
                WonCards = Permute(v.WonCards, my),
                CurrentTrick = v.CurrentTrick == null ? null : RotateTrick(v.CurrentTrick, my),
                CompletedTricks = completed,
                TeamAScore = flip ? v.TeamBScore : v.TeamAScore,
                TeamBScore = flip ? v.TeamAScore : v.TeamBScore,
                TargetScore = v.TargetScore,
                Awaiting = v.Awaiting == null ? null : new Awaiting
                {
                    Kind = v.Awaiting.Kind,
                    Seat = ToLocalSeat(v.Awaiting.Seat, my),
                    DeadlineUtc = v.Awaiting.DeadlineUtc,
                    LegalMoves = v.Awaiting.LegalMoves,
                    CanPass = v.Awaiting.CanPass,
                },
            };
        }

        private static TrickView RotateTrick(TrickView t, int my)
        {
            var history = new List<PlayView>(t.History.Count);
            foreach (var p in t.History)
                history.Add(new PlayView
                {
                    Seat = ToLocalSeat(p.Seat, my),
                    IsPass = p.IsPass,
                    IsBombInterrupt = p.IsBombInterrupt,
                    Cards = p.Cards,
                });

            return new TrickView
            {
                LeadType = t.LeadType,
                LeadLength = t.LeadLength,
                Top = t.Top,
                TopOwnerSeat = ToLocalSeat(t.TopOwnerSeat, my),
                AccumulatedPoints = t.AccumulatedPoints,
                History = history,
            };
        }

        private static AppliedAction RotateApplied(AppliedAction a, int my) => new AppliedAction
        {
            Kind = a.Kind,
            Seat = ToLocalSeat(a.Seat, my),
            Cards = a.Cards,
            RecipientSeat = ToLocalSeat(a.RecipientSeat, my),
        };

        private static RoundResult Flip(RoundResult r) => new RoundResult
        {
            TeamACardPoints = r.TeamBCardPoints,
            TeamBCardPoints = r.TeamACardPoints,
            TeamATichuDelta = r.TeamBTichuDelta,
            TeamBTichuDelta = r.TeamATichuDelta,
            TeamATotal = r.TeamBTotal,
            TeamBTotal = r.TeamATotal,
        };

        private static T[] Permute<T>(T[] abs, int my)
        {
            var local = new T[abs.Length];
            for (int i = 0; i < abs.Length; i++) local[ToLocalSeat(i, my)] = abs[i];
            return local;
        }
    }
}
