using Tichu.Core.Cards;

namespace Tichu.Core.Combinations
{
    public static class CombinationComparer
    {
        /// <summary>candidate가 현재 top을 이기는가(추종 상황). top이 null이면 비교 불요.</summary>
        public static bool Beats(Combination candidate, Combination top)
        {
            if (candidate == null || candidate.Type == CombinationType.Invalid) return false;
            if (top == null || top.Type == CombinationType.Invalid) return false;

            // 봉황 단독은 용 단독을 못 이김.
            if (candidate.Type == CombinationType.Single && top.Type == CombinationType.Single
                && candidate.Cards[0].Special == SpecialKind.Phoenix
                && top.Cards[0].Special == SpecialKind.Dragon)
                return false;

            bool cb = candidate.IsBomb, tb = top.IsBomb;
            if (cb && !tb) return true;
            if (!cb && tb) return false;

            if (cb && tb) // 폭탄 vs 폭탄
            {
                bool cSf = candidate.Type == CombinationType.StraightFlushBomb;
                bool tSf = top.Type == CombinationType.StraightFlushBomb;
                if (cSf && !tSf) return true;     // 스플 > 포카드
                if (!cSf && tSf) return false;    // 포카드 < 스플
                if (!cSf && !tSf) return candidate.Rank > top.Rank;          // 포카드끼리
                if (candidate.Length != top.Length) return candidate.Length > top.Length; // 스플: 길이 우선
                return candidate.Rank > top.Rank;
            }

            // 비폭탄 vs 비폭탄: 타입·장수 일치 + 값↑
            if (candidate.Type != top.Type) return false;
            if (candidate.Length != top.Length) return false;
            return candidate.Rank > top.Rank;
        }
    }
}
