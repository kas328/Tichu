using System.Threading;
using Cysharp.Threading.Tasks;
using Tichu.Core.Combinations;
using Tichu.GameFlow.Agents;

namespace Tichu.Presentation
{
    /// <summary>
    /// GameFlow IAgent의 비동기 미러. AsyncGameDriver가 이 인터페이스만 본다.
    /// AI와 인간 에이전트 모두 이 계약을 구현한다.
    /// </summary>
    public interface IDecisionAgent
    {
        /// <summary>큰 티츄 선언 여부. true=선언, false=패스.</summary>
        UniTask<bool> CallGrandTichuAsync(DecisionContext ctx, CancellationToken ct);

        /// <summary>교환 카드 선택.</summary>
        UniTask<ExchangeChoice> ChooseExchangeAsync(DecisionContext ctx, CancellationToken ct);

        /// <summary>작은 티츄 선언 여부(첫 패 내기 전).</summary>
        UniTask<bool> CallTichuAsync(DecisionContext ctx, CancellationToken ct);

        /// <summary>자기 턴의 행동: 리드/팔로우/패스 + 마작 소원.</summary>
        UniTask<TurnDecision> DecideTurnAsync(DecisionContext ctx, CancellationToken ct);

        /// <summary>오프턴 폭탄 인터럽트 여부. null=폭탄 거절.</summary>
        UniTask<Combination?> DecideBombAsync(DecisionContext ctx, CancellationToken ct);

        /// <summary>용으로 트릭을 먹었을 때 양도할 상대 좌석 번호.</summary>
        UniTask<int> ChooseDragonRecipientAsync(DecisionContext ctx, CancellationToken ct);
    }
}
