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
