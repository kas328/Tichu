using System.Collections.Generic;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;

namespace Tichu.Net
{
    /// <summary>
    /// 클라이언트가 보낸 카드 집합을 서버의 합법수 하나로 확정한다.
    /// 합법수 목록에서 찾는 방식이라 봉황 대체·소원 강제·폭탄 인터럽트 같은 규칙이
    /// 자동으로 지켜지고, 조합 인식도 엔진과 항상 일치한다.
    /// </summary>
    public static class MoveResolver
    {
        /// <summary>
        /// 카드 집합이 일치하는 합법수를 돌려준다. 일치하는 수가 없으면 null(불법).
        ///
        /// Rank 가 0이 아니면 그 값까지 일치해야 한다 — 클라이언트의 뷰가 낡아 다른 조합을
        /// 의도한 경우를 잡는 선택적 검증이다. 모호성 해소용이 아니다: Recognize 는 카드
        /// 다중집합의 순수 함수라 두 합법수가 같은 카드 집합을 가질 수 없고, 그 불변식은
        /// MoveResolverTests.No_two_legal_moves_share_a_card_multiset 가 지킨다.
        /// </summary>
        public static Combination? ResolveMove(GameState s, int seat, ClientAction a)
        {
            if (a.Cards == null || a.Cards.Count == 0) return null;

            var moves = LegalMoveGenerator.LegalMoves(s, seat);
            for (int i = 0; i < moves.Count; i++)
            {
                var m = moves[i];
                if (!SameCardMultiset(m.Cards, a.Cards)) continue;
                if (a.Rank != 0 && m.Rank != a.Rank) return null; // 의도 불일치 — 적용하지 않는다
                return m;
            }
            return null;
        }

        /// <summary>두 카드 모음이 같은 다중집합인가(순서 무관, 중복 개수 포함).</summary>
        private static bool SameCardMultiset(IReadOnlyList<Card> x, IReadOnlyList<Card> y)
        {
            if (x.Count != y.Count) return false;

            var used = new bool[y.Count];
            for (int i = 0; i < x.Count; i++)
            {
                bool found = false;
                for (int j = 0; j < y.Count; j++)
                {
                    if (used[j] || !y[j].Equals(x[i])) continue;
                    used[j] = true;
                    found = true;
                    break;
                }
                if (!found) return false;
            }
            return true;
        }
    }
}
