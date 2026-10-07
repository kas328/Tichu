using System.Collections.Generic;

namespace Tichu.Net
{
    /// <summary>
    /// 클라이언트가 보는 전송 계층. 서버 쪽 ITichuTransport 의 짝이다 — 클라이언트는 자기 좌석
    /// 하나만 다루므로 좌석 인자가 없다. 뒤에 LoopbackClient(싱글) / WebSocketClientTransport(M2) 가 온다.
    /// Drain 은 메인 스레드 폴링용이고, Submit 은 막히지 않고 즉시 돌아와야 한다.
    /// </summary>
    public interface IClientTransport
    {
        /// <summary>아직 가져가지 않은 이벤트를 도착 순서대로 꺼낸다.</summary>
        IReadOnlyList<ServerEvent> Drain();

        /// <summary>액션을 서버로 보낸다.</summary>
        void Submit(ClientAction a);
    }
}
