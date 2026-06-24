using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Tichu.Core.Combinations;
using Tichu.GameFlow.Agents;

namespace Tichu.Presentation
{
    /// <summary>
    /// 동기 다세계 <see cref="PimcAgent"/>를 <see cref="IDecisionAgent"/>로 감싸는 앱 어댑터.
    /// DecideTurn 만 백그라운드(UniTask.RunOnThreadPool)에서 anytime 탐색하고(메인스레드 비차단),
    /// 예산 CTS(CancelAfter budgetMs) 만료 → best-so-far, 외부 ct(폭탄인터럽트) → OCE 전파.
    /// 셋업·폭탄·용양도 등 나머지 결정은 즉시 위임(휴리스틱). 사람 관전 페이싱용 delay/fastForward 포함.
    /// </summary>
    public sealed class PimcDecisionAgent : IDecisionAgent
    {
        private readonly PimcAgent _pimc;
        private readonly PolicyConfig _config;
        private readonly int _budgetMs;
        private readonly int _delayMs;
        private readonly Func<bool> _fastForward;

        public PimcDecisionAgent(ulong roundSeed, int seat, PolicyConfig config,
            int budgetMs, int delayMs, Func<bool> fastForward = null)
        {
            _pimc = new PimcAgent(roundSeed, seat, config);
            _config = config;
            _budgetMs = budgetMs;
            _delayMs = delayMs;
            _fastForward = fastForward;
        }

        private UniTask DelayAsync(CancellationToken ct)
            => _fastForward != null && _fastForward() ? UniTask.CompletedTask : UniTask.Delay(_delayMs, cancellationToken: ct);

        public UniTask<bool> CallGrandTichuAsync(DecisionContext ctx, CancellationToken ct)
            => UniTask.FromResult(_pimc.CallGrandTichu(ctx));

        public UniTask<ExchangeChoice> ChooseExchangeAsync(DecisionContext ctx, CancellationToken ct)
            => UniTask.FromResult(_pimc.ChooseExchange(ctx));

        public UniTask<bool> CallTichuAsync(DecisionContext ctx, CancellationToken ct)
            => UniTask.FromResult(_pimc.CallTichu(ctx));

        public async UniTask<TurnDecision> DecideTurnAsync(DecisionContext ctx, CancellationToken ct)
        {
            await DelayAsync(ct);

            // Easy(탐색 OFF): 즉시 휴리스틱(스레드풀 우회).
            if (_config.Worlds <= 0)
                return _pimc.DecideTurn(ctx);

            var snap = ctx.State.Clone();   // 가변 공유 차단(백그라운드 진입 전).
            int seat = ctx.Seat;
            using var budgetCts = new CancellationTokenSource(_budgetMs);   // anytime 예산.
            var budget = budgetCts.Token;
            return await UniTask.RunOnThreadPool(
                () => _pimc.DecideTurnAnytime(new DecisionContext(snap, seat), budget, ct),
                cancellationToken: ct);
        }

        public async UniTask<Combination?> DecideBombAsync(DecisionContext ctx, CancellationToken ct)
        {
            var bomb = _pimc.DecideBomb(ctx);     // P2-B2: 휴리스틱(즉시).
            if (bomb != null) await DelayAsync(ct);
            return bomb;
        }

        public UniTask<int> ChooseDragonRecipientAsync(DecisionContext ctx, CancellationToken ct)
            => UniTask.FromResult(_pimc.ChooseDragonRecipient(ctx));
    }
}
