using System.Collections.Generic;
using Tichu.Core;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;

namespace Tichu.GameFlow.Agents
{
    /// <summary>
    /// 결정적 랜덤 기준선 에이전트(AI 품질 비교용).
    /// 주입된 시드 RNG 로만 결정하며 비결정적 소스를 일체 쓰지 않는다.
    /// 모든 결정은 반드시 합법이다(DecideTurn 은 LegalMoves ∪ 패스, 폭탄은 LegalMoves 의 폭탄).
    /// </summary>
    public sealed class RandomAgent : IAgent
    {
        private Rng _rng;
        private readonly int _seat;

        public RandomAgent(ulong roundSeed, int seat)
        {
            _rng = new Rng(roundSeed ^ 0xA1A1_0000_0000_0001UL ^ (ulong)seat);
            _seat = seat;
        }

        /// <inheritdoc/>
        public bool CallGrandTichu(in DecisionContext ctx) => false;

        /// <inheritdoc/>
        public bool CallTichu(in DecisionContext ctx) => false;

        /// <inheritdoc/>
        public ExchangeChoice ChooseExchange(in DecisionContext ctx)
        {
            // 손패에서 서로 다른 3장을 무작위로 뽑는다(인덱스 셔플 일부).
            var hand = ctx.MyHand;
            int n = hand.Count;
            // Fisher-Yates 로 앞 3개 인덱스만 결정.
            var idx = new int[n];
            for (int i = 0; i < n; i++) idx[i] = i;
            for (int i = 0; i < 3 && i < n; i++)
            {
                int j = i + _rng.NextInt(n - i);
                (idx[i], idx[j]) = (idx[j], idx[i]);
            }
            return new ExchangeChoice(hand[idx[0]], hand[idx[1]], hand[idx[2]]);
        }

        /// <inheritdoc/>
        public TurnDecision DecideTurn(in DecisionContext ctx)
        {
            var moves = ctx.LegalMoves;
            bool canPass = ctx.CanPass;
            int optionCount = moves.Count + (canPass ? 1 : 0);
            if (optionCount == 0)
                return TurnDecision.Pass; // 이론상 도달 안 함(드라이버 방어용).

            int pick = optionCount > 1 ? _rng.NextInt(optionCount) : 0;
            if (pick < moves.Count)
                return TurnDecision.Play(moves[pick]);
            return TurnDecision.Pass;
        }

        /// <inheritdoc/>
        public Combination? DecideBomb(in DecisionContext ctx) => null;

        /// <inheritdoc/>
        public int ChooseDragonRecipient(in DecisionContext ctx)
            => _rng.NextInt(2) == 0 ? ctx.LeftSeat : ctx.RightSeat;
    }
}
