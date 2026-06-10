using System.Collections.Generic;
using Tichu.Core.Combinations;

namespace Tichu.GameFlow
{
    /// <summary>
    /// AI 휴리스틱이 "가장 낮은 / 최소 오버킬로 이기는 / 가장 작은 폭탄" 같은 선택을 할 때
    /// 의지할 단일한 결정적 정렬 오라클. LegalMoves 는 순서가 없으므로 여기서 총순서를 정의한다.
    /// (튜닝된 값이 아니라, 안정적이고 결정적인 비교자가 목적이다.)
    /// 공개 API: Strength, Lowest, Smallest, CheapestThatBeats.
    /// </summary>
    public static class MoveOrder
    {

        /// <summary>
        /// "얼마나 높은/강한 수인가"의 총순서 프록시.
        /// 1차: c.Rank(×2 스케일; 봉황 단독의 홀수 Rank 는 정수 사이에 정확히 놓인다),
        /// 2차: Length, 3차: Type. Dog/Mahjong 은 낮은 Rank 라 자연히 하단에 온다.
        /// </summary>
        public static int Strength(Combination c)
        {
            // Rank 는 최대 ~30(A=28, 용=30), Length 최대 14, Type 최대 8.
            // 충돌 없는 사전식 정렬을 위해 자리수를 분리한다.
            return (c.Rank * 256) + (c.Length * 16) + (int)c.Type;
        }

        /// <summary>가장 낮은(약한) 수. 없으면 null.</summary>
        public static Combination? Lowest(IReadOnlyList<Combination> moves)
        {
            Combination? best = null;
            int bestKey = int.MaxValue;
            for (int i = 0; i < moves.Count; i++)
            {
                int key = Strength(moves[i]);
                if (key < bestKey) { bestKey = key; best = moves[i]; }
            }
            return best;
        }

        /// <summary>
        /// 가장 작은(약한) 조합. 폭탄 후보 중 최소 폭탄 선택에 쓴다. 없으면 null.
        /// 내부적으로 Lowest(Strength 기준)와 동일; "smallest bomb" 호출 지점을 위한 이름.
        /// </summary>
        public static Combination? Smallest(IReadOnlyList<Combination> moves) => Lowest(moves);

        /// <summary>
        /// top 을 이기는 수들 중 강도가 가장 낮은(최소 오버킬) 비폭탄 수.
        /// moves 는 이미 합법(=top 을 이기는) 목록을 가정하지만, 방어적으로 Strength 로 한 번 더 거른다.
        /// 이기는 수가 없으면 null.
        /// </summary>
        public static Combination? CheapestThatBeats(IReadOnlyList<Combination> moves, Combination top)
        {
            int topStrength = Strength(top);
            Combination? best = null;
            int bestKey = int.MaxValue;
            for (int i = 0; i < moves.Count; i++)
            {
                var m = moves[i];
                int key = Strength(m);
                if (key <= topStrength) continue; // top 을 이기지 못하는 수 제외
                if (key < bestKey) { bestKey = key; best = m; }
            }
            return best;
        }
    }
}
