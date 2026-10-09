using System;
using System.Collections.Generic;
using System.Diagnostics;
using Tichu.Net;

namespace Tichu.Presentation.Net
{
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
        private double? _fillAt;

        public LobbyWaiter() : this(() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency) { }

        public LobbyWaiter(Func<double> nowSeconds) { _now = nowSeconds; }

        public int Seat { get; private set; } = -1;
        public int Connected { get; private set; }
        public int Required { get; private set; }
        public int Waiting { get; private set; }
        public bool Busy { get; private set; }
        public int Position { get; private set; }
        public bool Started { get; private set; }
        public string FaultReason { get; private set; }
        public List<ServerEvent> Backlog { get; } = new List<ServerEvent>();

        public string StatusText
        {
            get
            {
                if (Required > 0) return $"다른 플레이어 기다리는 중 {Connected}/{Required}";
                if (Busy) return $"서버가 붐빕니다 · 대기 순번 {Position}";
                if (_fillAt.HasValue)
                {
                    int left = Math.Max(0, (int)Math.Ceiling(_fillAt.Value - _now()));
                    return $"상대를 찾는 중 · {Waiting}명 대기 · {left}초 뒤 AI와 시작";
                }
                return "서버 접속 중…";
            }
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
