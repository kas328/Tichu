using System.Threading;
using Cysharp.Threading.Tasks;
using Tichu.Core.Combinations;
using Tichu.GameFlow.Agents;

namespace Tichu.Presentation
{
    /// <summary>
    /// UI 레이어가 구현하는 인간 입력 포트.
    /// HumanAgent는 이 포트에 요청을 위임하고 결과를 await한다.
    /// </summary>
    public interface IHumanInputPort
    {
        /// <summary>큰 티츄 선언 여부를 사용자에게 요청한다.</summary>
        UniTask<bool> RequestGrandTichuAsync(DecisionContext ctx, CancellationToken ct);

        /// <summary>교환 카드 선택을 사용자에게 요청한다.</summary>
        UniTask<ExchangeChoice> RequestExchangeAsync(DecisionContext ctx, CancellationToken ct);

        /// <summary>작은 티츄 선언 여부를 사용자에게 요청한다.</summary>
        UniTask<bool> RequestTichuAsync(DecisionContext ctx, CancellationToken ct);

        /// <summary>자기 턴의 행동(리드/팔로우/패스)을 사용자에게 요청한다.</summary>
        UniTask<TurnDecision> RequestTurnDecisionAsync(DecisionContext ctx, CancellationToken ct);

        /// <summary>오프턴 폭탄 인터럽트 여부를 사용자에게 요청한다. null=거절.</summary>
        UniTask<Combination?> RequestBombAsync(DecisionContext ctx, CancellationToken ct);

        /// <summary>용 양도 대상 좌석을 사용자에게 요청한다.</summary>
        UniTask<int> RequestDragonRecipientAsync(DecisionContext ctx, CancellationToken ct);
    }
}
