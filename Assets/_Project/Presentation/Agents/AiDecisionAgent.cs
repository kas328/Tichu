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
    /// 동기 <see cref="AiAgent"/>를 <see cref="IDecisionAgent"/> 비동기 계약으로 감싸는 어댑터.
    /// 각 결정은 UniTask.FromResult 로 즉시 완료된다(ct 무시).
    /// 결정성·휴리스틱·시드는 내부 AiAgent 가 그대로 유지한다.
    /// </summary>
    public sealed class AiDecisionAgent : IDecisionAgent
    {
        private readonly AiAgent _inner;

        public AiDecisionAgent(ulong roundSeed, int seat)
        {
            _inner = new AiAgent(roundSeed, seat);
        }

        public UniTask<bool> CallGrandTichuAsync(DecisionContext ctx, CancellationToken ct)
            => UniTask.FromResult(_inner.CallGrandTichu(ctx));

        public UniTask<ExchangeChoice> ChooseExchangeAsync(DecisionContext ctx, CancellationToken ct)
            => UniTask.FromResult(_inner.ChooseExchange(ctx));

        public UniTask<bool> CallTichuAsync(DecisionContext ctx, CancellationToken ct)
            => UniTask.FromResult(_inner.CallTichu(ctx));

        public UniTask<TurnDecision> DecideTurnAsync(DecisionContext ctx, CancellationToken ct)
            => UniTask.FromResult(_inner.DecideTurn(ctx));

        public UniTask<Combination?> DecideBombAsync(DecisionContext ctx, CancellationToken ct)
            => UniTask.FromResult(_inner.DecideBomb(ctx));

        public UniTask<int> ChooseDragonRecipientAsync(DecisionContext ctx, CancellationToken ct)
            => UniTask.FromResult(_inner.ChooseDragonRecipient(ctx));
    }
}
