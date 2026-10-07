using System.Collections.Generic;
using Tichu.Net;

namespace Tichu.Presentation.Net
{
    /// <summary>
    /// 네트워크 매치의 대기실. 매치가 시작되기 전 이벤트(Lobby·폴트)를 읽어 내 좌석과 인원을
    /// 알아내고, 첫 매치 이벤트부터는 순서대로 Backlog 에 담아 TichuClient 에 넘긴다 —
    /// 마지막 Lobby 와 첫 StateUpdate 가 한 Drain 묶음에 섞여 와도 잃지 않기 위해서다.
    /// 유니티 타입 무참조(EditMode 단위 테스트).
    /// </summary>
    public sealed class LobbyWaiter
    {
        public int Seat { get; private set; } = -1;
        public int Connected { get; private set; }
        public int Required { get; private set; }
        public bool Started { get; private set; }
        public string FaultReason { get; private set; }
        public List<ServerEvent> Backlog { get; } = new List<ServerEvent>();

        public string StatusText => Required > 0
            ? $"다른 플레이어 기다리는 중 {Connected}/{Required}"
            : "서버 접속 중…";

        public void Feed(IReadOnlyList<ServerEvent> events)
        {
            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                if (Started) { Backlog.Add(ev); continue; }

                switch (ev.Kind)
                {
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
