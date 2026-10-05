using Tichu.GameFlow.Agents;

namespace Tichu.Presentation.ViewModel
{
    /// <summary>인간 플레이어에게 요청 중인 결정의 종류.</summary>
    public enum DecisionKind
    {
        GrandTichu,
        Exchange,
        Tichu,
        Turn,
        Bomb,
        DragonRecipient
    }

    /// ⚠️ 라이브 경로가 아니다. 테이블 UI 는 2026-10-05 부터 MatchSession + TichuClient 를
    /// 쓴다. 이 타입은 동기↔비동기 드라이버 동일성 오라클(AsyncGameDriverTests)을 위해
    /// 남아 있다. 삭제는 그 오라클을 대체한 뒤에 별도로 한다.
    /// <summary>
    /// 현재 대기 중인 결정 요청.
    /// Kind + Context 를 묶어 UI가 어떤 입력 패널을 표시해야 하는지 알 수 있게 한다.
    /// </summary>
    public sealed class DecisionRequest
    {
        /// <summary>결정의 종류.</summary>
        public DecisionKind Kind { get; }

        /// <summary>이 결정의 컨텍스트(손패, 합법 수, 패스 가능 여부 등).</summary>
        public DecisionContext Context { get; }

        public DecisionRequest(DecisionKind kind, DecisionContext context)
        {
            Kind = kind;
            Context = context;
        }
    }
}
