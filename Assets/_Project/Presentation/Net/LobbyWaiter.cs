using System;
using System.Collections.Generic;
using System.Diagnostics;
using Tichu.Net;

namespace Tichu.Presentation.Net
{
    /// <summary>대기 화면 한 장면(순수 값). Seats 는 채워진 칸 수 — 0 이면 4칸 영역을 숨긴다.</summary>
    public readonly struct LobbyDisplay : IEquatable<LobbyDisplay>
    {
        public readonly string Title;
        public readonly int Seats;
        public readonly string Detail;

        public LobbyDisplay(string title, int seats, string detail)
        {
            Title = title;
            Seats = seats;
            Detail = detail;
        }

        public static readonly LobbyDisplay Connecting = new LobbyDisplay("서버 접속 중…", 0, "");
        public static readonly LobbyDisplay Reconnecting = new LobbyDisplay("연결이 끊겼습니다", 0, "재접속 중… 같은 자리로 돌아갑니다");

        public bool Equals(LobbyDisplay o) => Title == o.Title && Seats == o.Seats && Detail == o.Detail;
        public override bool Equals(object obj) => obj is LobbyDisplay o && Equals(o);
        public override int GetHashCode() => (Title, Seats, Detail).GetHashCode();
        public override string ToString() => $"{Title} | {Seats} | {Detail}";
    }

    /// <summary>
    /// 네트워크 매치의 대기실. 매치가 시작되기 전 이벤트(Queued·Lobby·폴트)를 읽어 대기 상태와 내 좌석을
    /// 알아내고, 첫 매치 이벤트부터는 순서대로 Backlog 에 담아 TichuClient 에 넘긴다 —
    /// 마지막 Lobby 와 첫 StateUpdate 가 한 Drain 묶음에 섞여 와도 잃지 않기 위해서다.
    /// Queued 의 남은 초는 받은 시각부터 이 쪽에서 센다(서버는 대기열이 바뀔 때만 보낸다).
    /// 유니티 타입 무참조(EditMode 단위 테스트).
    /// </summary>
    public sealed class LobbyWaiter
    {
        private readonly Func<double> _now;
        private readonly bool _reconnecting;
        private double? _fillAt;

        public LobbyWaiter(bool reconnecting = false)
            : this(() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency, reconnecting) { }

        /// <param name="reconnecting">끊긴 판으로 돌아가는 접속이면 true — 화면은 매치가 시작될 때까지 재접속 장면.</param>
        public LobbyWaiter(Func<double> nowSeconds, bool reconnecting = false)
        {
            _now = nowSeconds;
            _reconnecting = reconnecting;
        }

        public int Seat { get; private set; } = -1;
        public int Connected { get; private set; }
        public int Required { get; private set; }
        public int Waiting { get; private set; }
        public bool Busy { get; private set; }
        public int Position { get; private set; }
        public bool Started { get; private set; }
        public int MatchTeamA { get; private set; }
        public int MatchTeamB { get; private set; }
        public string FaultReason { get; private set; }
        public List<ServerEvent> Backlog { get; } = new List<ServerEvent>();

        /// <summary>지금 대기 화면에 그릴 장면. 판정 순서: 재접속 → 방 배정 → 붐빔 → 대기열 → 접속 중.</summary>
        public LobbyDisplay Display
        {
            get
            {
                if (_reconnecting) return LobbyDisplay.Reconnecting;
                if (Required > 0) return new LobbyDisplay("곧 시작합니다", Math.Min(Connected, 4), "다른 플레이어 기다리는 중");
                if (Busy) return new LobbyDisplay("서버가 붐빕니다", 0, $"대기 순번 {Position} · 자리가 나면 자동으로 들어갑니다");
                if (_fillAt.HasValue)
                {
                    int left = Math.Max(0, (int)Math.Ceiling(_fillAt.Value - _now()));
                    return new LobbyDisplay("상대를 찾는 중", Math.Min(Waiting, 4), $"{left}초 뒤 빈자리는 AI로 채우고 시작합니다");
                }
                return LobbyDisplay.Connecting;
            }
        }

        /// <summary>내 팀 기준 누적 점수(뷰모델은 좌석 0 기준 — 홀수 좌석이면 B 가 내 팀).</summary>
        public void LocalScore(out int mine, out int theirs)
        {
            bool odd = (Seat & 1) == 1;
            mine = odd ? MatchTeamB : MatchTeamA;
            theirs = odd ? MatchTeamA : MatchTeamB;
        }

        public void Feed(IReadOnlyList<ServerEvent> events)
        {
            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                if (Started) { Backlog.Add(ev); continue; }

                switch (ev.Kind)
                {
                    case ServerEventKind.Queued:
                        Waiting = ev.Waiting;
                        Busy = ev.Busy;
                        Position = ev.Position;
                        _fillAt = ev.FillInSeconds.HasValue ? _now() + ev.FillInSeconds.Value : (double?)null;
                        break;
                    case ServerEventKind.Lobby:
                        Seat = ev.YourSeat;
                        Connected = ev.Connected;
                        Required = ev.Required;
                        MatchTeamA = ev.TeamA;
                        MatchTeamB = ev.TeamB;
                        break;
                    case ServerEventKind.SessionFaulted:
                        FaultReason = ev.Reason;
                        break;
                    default:
                        Started = true;
                        Backlog.Add(ev);
                        break;
                }
            }
        }
    }
}
