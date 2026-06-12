using System.Threading;
using Cysharp.Threading.Tasks;
using Tichu.Core.Combinations;
using Tichu.GameFlow.Agents;

namespace Tichu.Presentation
{
    /// <summary>
    /// 앱 전용 어댑터: 동기 <see cref="AiAgent"/>를 감싸되 플레이성 결정(차례·폭탄·용양도)에
    /// 딜레이를 줘서 사람이 AI 플레이를 따라가며 카운팅할 수 있게 한다.
    /// 결정 자체는 AiAgent 와 동일(시드 동일). 셋업 결정(큰/작은티츄·교환)은 즉시.
    /// 테스트/오라클은 딜레이 없는 <see cref="AiDecisionAgent"/>를 쓴다.
    /// </summary>
    public sealed class DelayedAiDecisionAgent : IDecisionAgent
    {
        private readonly AiAgent _inner;
        private readonly int _delayMs;
        private readonly System.Func<bool> _fastForward; // true 반환 시 딜레이 건너뜀(스킵). null=항상 딜레이.

        public DelayedAiDecisionAgent(ulong roundSeed, int seat, int delayMs, System.Func<bool> fastForward = null)
        {
            _inner = new AiAgent(roundSeed, seat);
            _delayMs = delayMs;
            _fastForward = fastForward;
        }

        private UniTask DelayAsync(CancellationToken ct)
            => _fastForward != null && _fastForward() ? UniTask.CompletedTask : UniTask.Delay(_delayMs, cancellationToken: ct);

        public UniTask<bool> CallGrandTichuAsync(DecisionContext ctx, CancellationToken ct)
            => UniTask.FromResult(_inner.CallGrandTichu(ctx));

        public UniTask<ExchangeChoice> ChooseExchangeAsync(DecisionContext ctx, CancellationToken ct)
            => UniTask.FromResult(_inner.ChooseExchange(ctx));

        public UniTask<bool> CallTichuAsync(DecisionContext ctx, CancellationToken ct)
            => UniTask.FromResult(_inner.CallTichu(ctx));

        public async UniTask<TurnDecision> DecideTurnAsync(DecisionContext ctx, CancellationToken ct)
        {
            await DelayAsync(ct);
            return _inner.DecideTurn(ctx);
        }

        public async UniTask<Combination?> DecideBombAsync(DecisionContext ctx, CancellationToken ct)
        {
            // 폭탄 창은 매 좌석 검사되므로, 실제로 폭탄을 낼 때만 딜레이(아니면 즉시).
            var bomb = _inner.DecideBomb(ctx);
            if (bomb != null) await DelayAsync(ct);
            return bomb;
        }

        public async UniTask<int> ChooseDragonRecipientAsync(DecisionContext ctx, CancellationToken ct)
        {
            await DelayAsync(ct);
            return _inner.ChooseDragonRecipient(ctx);
        }
    }
}
