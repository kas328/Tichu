using System;

namespace Tichu.Net
{
    /// <summary>
    /// 권위 세션이 보는 전송 계층. 세션은 이 인터페이스만 알고, 뒤에
    /// 로컬 루프백 / WebSocket 서버(M2) 가 갈아끼워진다.
    /// </summary>
    public interface ITichuTransport
    {
        /// <summary>한 좌석에게만 이벤트를 보낸다.</summary>
        void SendTo(int seat, ServerEvent ev);

        /// <summary>클라이언트 액션이 도착했을 때 호출될 핸들러를 등록한다.</summary>
        void OnClientAction(Action<ClientAction> handler);
    }
}
