using Tichu.Core.Game;

namespace Tichu.Net
{
    /// <summary>서버 → 클라이언트 이벤트 종류.</summary>
    public enum ServerEventKind
    {
        StateUpdate,
        ActionRejected,
        RoundScored,
        MatchEnded,
        SessionFaulted,
        Lobby
    }

    /// <summary>
    /// 서버가 좌석에 보내는 이벤트.
    /// ActionRejected 는 발신자에게만 보낸다 — 브로드캐스트하면 상대의 시도 자체가
    /// 정보가 되므로 기획서 7.7 이 금지한다.
    /// </summary>
    public sealed class ServerEvent
    {
        public ServerEventKind Kind { get; set; }

        /// <summary>StateUpdate: 수신 좌석의 시야.</summary>
        public PlayerView? View { get; set; }

        /// <summary>ActionRejected / SessionFaulted: 사유.</summary>
        public string? Reason { get; set; }

        /// <summary>ActionRejected: 거부된 액션의 ActionSeq.</summary>
        public long ActionSeq { get; set; }

        /// <summary>RoundScored: 라운드 정산 결과.</summary>
        public RoundResult? Round { get; set; }

        /// <summary>StateUpdate: 이 갱신을 유발한 액션. 라운드 시작·대기 알림엔 null.</summary>
        public AppliedAction? Applied { get; set; }

        /// <summary>MatchEnded: 승리 팀(0=A, 1=B)과 최종 누적 점수.</summary>
        public int WinningTeam { get; set; }
        public int TeamA { get; set; }
        public int TeamB { get; set; }

        /// <summary>Lobby: 이 연결에 배정된 절대 좌석. 회전하지 않는다(대기실이 회전 전에 읽는다).</summary>
        public int YourSeat { get; set; }

        /// <summary>Lobby: 접속한 인간 수 / 시작에 필요한 인간 수.</summary>
        public int Connected { get; set; }
        public int Required { get; set; }

        public static ServerEvent StateUpdate(PlayerView view, Awaiting? awaiting, AppliedAction? applied = null)
        {
            view.Awaiting = awaiting;
            return new ServerEvent { Kind = ServerEventKind.StateUpdate, View = view, Applied = applied };
        }

        public static ServerEvent ActionRejected(long actionSeq, string reason) =>
            new ServerEvent { Kind = ServerEventKind.ActionRejected, ActionSeq = actionSeq, Reason = reason };

        public static ServerEvent RoundScored(RoundResult round) =>
            new ServerEvent { Kind = ServerEventKind.RoundScored, Round = round };

        public static ServerEvent MatchEnded(int winningTeam, int teamA, int teamB) =>
            new ServerEvent
            {
                Kind = ServerEventKind.MatchEnded,
                WinningTeam = winningTeam, TeamA = teamA, TeamB = teamB
            };

        public static ServerEvent SessionFaulted(string reason) =>
            new ServerEvent { Kind = ServerEventKind.SessionFaulted, Reason = reason };

        public static ServerEvent Lobby(int yourSeat, int connected, int required) =>
            new ServerEvent { Kind = ServerEventKind.Lobby, YourSeat = yourSeat, Connected = connected, Required = required };
    }
}
