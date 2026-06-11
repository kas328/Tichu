using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Tichu.Core.Combinations;
using Tichu.GameFlow.Agents;

namespace Tichu.Presentation.Tests
{
    /// <summary>
    /// 결정성 시나리오 테스트용 비동기 에이전트(<see cref="ScriptedAgent"/>의 미러).
    /// 각 메서드는 대응하는 Fn 델리게이트가 설정되어 있으면 그것을 호출하고,
    /// null이면 동기 ScriptedAgent와 동일한 안전 기본값을 UniTask.FromResult 로 즉시 반환한다.
    /// </summary>
    internal sealed class ScriptedDecisionAgent : IDecisionAgent
    {
        // ── 주입 가능한 델리게이트 ────────────────────────────────────────────────

        public Func<DecisionContext, bool>?           CallGrandTichuFn        { get; set; }
        public Func<DecisionContext, ExchangeChoice>? ChooseExchangeFn        { get; set; }
        public Func<DecisionContext, bool>?           CallTichuFn             { get; set; }
        public Func<DecisionContext, TurnDecision>?   DecideTurnFn            { get; set; }
        public Func<DecisionContext, Combination?>?   DecideBombFn            { get; set; }
        public Func<DecisionContext, int>?            ChooseDragonRecipientFn { get; set; }

        // ── IDecisionAgent 구현 ──────────────────────────────────────────────────

        /// <inheritdoc/>
        public UniTask<bool> CallGrandTichuAsync(DecisionContext ctx, CancellationToken ct)
            => UniTask.FromResult(CallGrandTichuFn != null ? CallGrandTichuFn(ctx) : false);

        /// <inheritdoc/>
        public UniTask<ExchangeChoice> ChooseExchangeAsync(DecisionContext ctx, CancellationToken ct)
        {
            if (ChooseExchangeFn != null) return UniTask.FromResult(ChooseExchangeFn(ctx));

            // 기본: 손패 앞 3장(서로 다른 카드) 를 Left/Partner/Right 순서로 선택.
            var hand = ctx.MyHand;
            return UniTask.FromResult(new ExchangeChoice(hand[0], hand[1], hand[2]));
        }

        /// <inheritdoc/>
        public UniTask<bool> CallTichuAsync(DecisionContext ctx, CancellationToken ct)
            => UniTask.FromResult(CallTichuFn != null ? CallTichuFn(ctx) : false);

        /// <inheritdoc/>
        public UniTask<TurnDecision> DecideTurnAsync(DecisionContext ctx, CancellationToken ct)
        {
            if (DecideTurnFn != null) return UniTask.FromResult(DecideTurnFn(ctx));

            // 기본: 합법수가 있으면 첫 번째 수를 낸다. 없으면 패스(가능한 경우).
            var moves = ctx.LegalMoves;
            if (moves.Count > 0)
                return UniTask.FromResult(TurnDecision.Play(moves[0]));
            // 합법수가 없고 패스 가능한 경우(팔로우 + 소원 미강제)
            return UniTask.FromResult(TurnDecision.Pass);
        }

        /// <inheritdoc/>
        public UniTask<Combination?> DecideBombAsync(DecisionContext ctx, CancellationToken ct)
            => UniTask.FromResult(DecideBombFn != null ? DecideBombFn(ctx) : null);

        /// <inheritdoc/>
        public UniTask<int> ChooseDragonRecipientAsync(DecisionContext ctx, CancellationToken ct)
            => UniTask.FromResult(ChooseDragonRecipientFn != null
                ? ChooseDragonRecipientFn(ctx)
                : (ctx.Seat + 1) % 4);
    }
}
