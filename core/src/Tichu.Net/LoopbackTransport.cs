using System;
using System.Collections.Generic;

namespace Tichu.Net
{
    /// <summary>
    /// 같은 프로세스 전송. 좌석별 수신함에 이벤트를 쌓아 두고 Submit 으로 액션을 세션에 넘긴다.
    /// SendTo 는 세션 스레드에서, Submit/Inbox 는 호출자 스레드에서 올 수 있어 잠금을 쓴다.
    /// </summary>
    public sealed class LoopbackTransport : ITichuTransport
    {
        private const int SeatCount = 4;

        private readonly object _gate = new object();
        private readonly List<ServerEvent>[] _inbox;
        private Action<ClientAction>? _handler;

        public LoopbackTransport()
        {
            _inbox = new List<ServerEvent>[SeatCount];
            for (int i = 0; i < SeatCount; i++) _inbox[i] = new List<ServerEvent>();
        }

        /// <inheritdoc/>
        public void SendTo(int seat, ServerEvent ev)
        {
            lock (_gate) _inbox[seat].Add(ev);
        }

        /// <inheritdoc/>
        public void OnClientAction(Action<ClientAction> handler)
        {
            lock (_gate) _handler = handler;
        }

        /// <summary>좌석 수신함의 스냅샷(호출 시점까지 받은 이벤트 복사본). 검증용.</summary>
        public IReadOnlyList<ServerEvent> Inbox(int seat)
        {
            lock (_gate) return new List<ServerEvent>(_inbox[seat]);
        }

        /// <summary>
        /// 아직 가져가지 않은 이벤트를 꺼내고 수신함을 비운다. 폴링하는 클라이언트는
        /// 이걸 써야 한다 — Inbox 는 매번 전체를 복사하므로 반복 폴링이 2차식이 된다.
        /// </summary>
        public IReadOnlyList<ServerEvent> Drain(int seat)
        {
            lock (_gate)
            {
                var taken = new List<ServerEvent>(_inbox[seat]);
                _inbox[seat].Clear();
                return taken;
            }
        }

        /// <summary>클라이언트가 액션을 제출한다. 핸들러가 없으면 아무 일도 하지 않는다.</summary>
        public void Submit(ClientAction a)
        {
            Action<ClientAction>? h;
            lock (_gate) h = _handler;
            h?.Invoke(a);
        }
    }
}
