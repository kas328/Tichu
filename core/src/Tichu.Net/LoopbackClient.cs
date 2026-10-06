using System.Collections.Generic;

namespace Tichu.Net
{
    /// <summary>같은 프로세스 루프백을 좌석 하나에 묶은 클라이언트 전송. 싱글플레이가 쓴다.</summary>
    public sealed class LoopbackClient : IClientTransport
    {
        private readonly LoopbackTransport _transport;
        private readonly int _seat;

        public LoopbackClient(LoopbackTransport transport, int seat)
        {
            _transport = transport;
            _seat = seat;
        }

        public IReadOnlyList<ServerEvent> Drain() => _transport.Drain(_seat);

        public void Submit(ClientAction a) => _transport.Submit(a);
    }
}
