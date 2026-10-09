using Tichu.Net;

namespace Tichu.Server
{
    public interface IPlayerLink
    {
        int Id { get; }
        /// <summary>지금 이 연결의 메시지를 받는 곳(대기열 또는 방). 스레드 간 읽기 — 구현은 volatile.</summary>
        ILinkOwner? Owner { get; set; }
        /// <summary>큐에 넣기만 하고 막히지 않는다(잠금 안에서 불려도 된다).</summary>
        void Send(ServerEvent ev);
        /// <summary>남은 송신을 마친 뒤 정상 종료한다. 처음 호출만 유효.</summary>
        void Close(string reason);
    }
    public interface ILinkOwner
    {
        void Receive(IPlayerLink from, ClientAction a);
        void Left(IPlayerLink who);
    }
    public interface IRoom : ILinkOwner
    {
        /// <summary>AI 생성·Lobby 송신·세션 시작. 매치메이커 잠금 밖에서 한 번 불린다.</summary>
        void Start();
    }
}
