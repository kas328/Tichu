using System;
using Tichu.Core.Cards;

namespace Tichu.Core.Combinations
{
    public static class CombinationRecognizer
    {
        public static Combination Recognize(ReadOnlySpan<Card> cards, TrickContext ctx)
        {
            int n = cards.Length;
            if (n == 0) return Combination.Invalid;
            if (n == 1) return RecognizeSingle(cards[0], ctx);

            // 멀티카드 판별은 Task 6~8에서 확장.
            return Combination.Invalid;
        }

        private static Combination RecognizeSingle(Card c, TrickContext ctx)
        {
            if (c.Special == SpecialKind.Phoenix)
            {
                int rank = (ctx.IsLead || !ctx.TopIsSingle) ? 3 : ctx.CurrentSingleRankScaled + 1;
                return new Combination(CombinationType.Single, new[] { c }, 1, rank, c.Points);
            }
            // 일반/마작/용/개: Rank 원본값 ×2 (개=0).
            return new Combination(CombinationType.Single, new[] { c }, 1, c.Rank * 2, c.Points);
        }
    }
}
