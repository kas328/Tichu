using System.Threading;
using Cysharp.Threading.Tasks;
using Tichu.Core.Combinations;
using Tichu.GameFlow.Agents;

namespace Tichu.Presentation
{
    /// ⚠️ 라이브 경로가 아니다. 테이블 UI 는 2026-10-05 부터 MatchSession + TichuClient 를
    /// 쓴다. 이 타입은 동기↔비동기 드라이버 동일성 오라클(AsyncGameDriverTests)을 위해
    /// 남아 있다. 삭제는 그 오라클을 대체한 뒤에 별도로 한다.
    /// <summary>
    /// IHumanInputPort 에 모든 결정을 위임하는 인간 에이전트.
    /// UI 대기·취소는 포트가 처리하며, 이 클래스는 단순 위임만 수행한다.
    /// </summary>
    public sealed class HumanAgent : IDecisionAgent
    {
        private readonly IHumanInputPort _input;

        /// <param name="input">UI 입력을 기다리는 포트.</param>
        public HumanAgent(IHumanInputPort input)
        {
            _input = input;
        }

        /// <inheritdoc/>
        public UniTask<bool> CallGrandTichuAsync(DecisionContext ctx, CancellationToken ct)
            => _input.RequestGrandTichuAsync(ctx, ct);

        /// <inheritdoc/>
        public UniTask<ExchangeChoice> ChooseExchangeAsync(DecisionContext ctx, CancellationToken ct)
            => _input.RequestExchangeAsync(ctx, ct);

        /// <inheritdoc/>
        public UniTask<bool> CallTichuAsync(DecisionContext ctx, CancellationToken ct)
            => _input.RequestTichuAsync(ctx, ct);

        /// <inheritdoc/>
        public UniTask<TurnDecision> DecideTurnAsync(DecisionContext ctx, CancellationToken ct)
            => _input.RequestTurnDecisionAsync(ctx, ct);

        /// <inheritdoc/>
        public UniTask<Combination?> DecideBombAsync(DecisionContext ctx, CancellationToken ct)
            => _input.RequestBombAsync(ctx, ct);

        /// <inheritdoc/>
        public UniTask<int> ChooseDragonRecipientAsync(DecisionContext ctx, CancellationToken ct)
            => _input.RequestDragonRecipientAsync(ctx, ct);
    }
}
