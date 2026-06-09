using System;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// 결정성 시나리오 테스트용 에이전트.
    /// 각 메서드는 대응하는 Fn 델리게이트가 설정되어 있으면 그것을 호출하고,
    /// null이면 항상 합법적인 안전 기본값을 반환한다.
    /// </summary>
    internal sealed class ScriptedAgent : IAgent
    {
        // ── 주입 가능한 델리게이트 ────────────────────────────────────────────────

        public Func<DecisionContext, bool>?         CallGrandTichuFn         { get; set; }
        public Func<DecisionContext, ExchangeChoice>? ChooseExchangeFn       { get; set; }
        public Func<DecisionContext, bool>?         CallTichuFn              { get; set; }
        public Func<DecisionContext, TurnDecision>? DecideTurnFn             { get; set; }
        public Func<DecisionContext, Combination?>? DecideBombFn             { get; set; }
        public Func<DecisionContext, int>?          ChooseDragonRecipientFn  { get; set; }

        // ── IAgent 구현 ──────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public bool CallGrandTichu(in DecisionContext ctx)
            => CallGrandTichuFn != null ? CallGrandTichuFn(ctx) : false;

        /// <inheritdoc/>
        public bool CallTichu(in DecisionContext ctx)
            => CallTichuFn != null ? CallTichuFn(ctx) : false;

        /// <inheritdoc/>
        public ExchangeChoice ChooseExchange(in DecisionContext ctx)
        {
            if (ChooseExchangeFn != null) return ChooseExchangeFn(ctx);

            // 기본: 손패 앞 3장(서로 다른 카드) 를 Left/Partner/Right 순서로 선택.
            var hand = ctx.MyHand;
            return new ExchangeChoice(hand[0], hand[1], hand[2]);
        }

        /// <inheritdoc/>
        public TurnDecision DecideTurn(in DecisionContext ctx)
        {
            if (DecideTurnFn != null) return DecideTurnFn(ctx);

            // 기본: 합법수가 있으면 첫 번째 수를 낸다. 없으면 패스(가능한 경우).
            var moves = ctx.LegalMoves;
            if (moves.Count > 0)
                return TurnDecision.Play(moves[0]);
            // 합법수가 없고 패스 가능한 경우(팔로우 + 소원 미강제)
            return TurnDecision.Pass;
        }

        /// <inheritdoc/>
        public Combination? DecideBomb(in DecisionContext ctx)
            => DecideBombFn != null ? DecideBombFn(ctx) : null;

        /// <inheritdoc/>
        public int ChooseDragonRecipient(in DecisionContext ctx)
            => ChooseDragonRecipientFn != null
                ? ChooseDragonRecipientFn(ctx)
                : (ctx.Seat + 1) % 4;
    }
}
