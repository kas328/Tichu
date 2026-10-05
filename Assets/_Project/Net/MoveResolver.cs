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

            // 랭크 등가는 보유하지 않은 카드를 낼 구멍을 연다(7♠ 만 들고 7♥ 를 보내도
            // 랭크 다중집합은 같다). 엔진의 HandContainsAll 에 맡기면 드라이버가 통보 없이
            // 버리므로, 검증 관문인 이 자리에서 막는다.
            if (!HandContainsAll(s.Seats[seat].Hand, a.Cards)) return null;

            var moves = LegalMoveGenerator.LegalMoves(s, seat);
            for (int i = 0; i < moves.Count; i++)
            {
                var m = moves[i];
                // 생성기는 랭크당 한 장만 내보내므로(GenerateSingles) 카드 신원 일치만 보면
                // 같은 랭크의 다른 무늬를 고른 사람의 멀쩡한 수가 거부된다. 랭크 다중집합이
                // 같으면 받아들이고, 카드만 클라이언트 것으로 바꿔 적용한다 — 랭크가 같으니
                // 소원 강제·봉황 해석·점수·폭탄 판정은 합법수 그대로다.
                var cards = SubstituteCards(m.Cards, a.Cards);
                if (cards == null) continue;
                if (a.Rank != 0 && m.Rank != a.Rank) return null; // 의도 불일치 — 적용하지 않는다
                return new Combination(m.Type, cards, m.Length, m.Rank, m.PointsInPlay);
            }
            return null;
        }

        /// <summary>
        /// template 과 랭크 다중집합이 같은지 보고, 같으면 template 의 순서에 맞춰 client 카드로
        /// 바꾼 목록을 돌려준다. 다르면 null.
        ///
        /// 순서를 template 에 맞추는 이유는 합법수의 카드 순서(생성기가 정한 순서)를 그대로
        /// 보존하기 위함이다 — 물리 카드만 교체하고 조합의 모양은 건드리지 않는다.
        /// 특수 카드는 Special 이 유일하므로 (Rank, Special) 비교로 자동 구분된다.
        /// </summary>
        private static List<Card>? SubstituteCards(IReadOnlyList<Card> template, IReadOnlyList<Card> client)
        {
            if (template.Count != client.Count) return null;

            var pool = new List<Card>(client);
            var result = new List<Card>(template.Count);
            for (int i = 0; i < template.Count; i++)
            {
                var t = template[i];
                int found = -1;
                for (int j = 0; j < pool.Count; j++)
                {
                    if (pool[j].Rank != t.Rank || pool[j].Special != t.Special) continue;
                    found = j;
                    break;
                }
                if (found < 0) return null;
                result.Add(pool[found]);
                pool.RemoveAt(found);
            }
            return result;
        }

        /// <summary>cards 전부가 hand 에 있는가(중복 개수 포함). 카드 신원으로 비교한다.</summary>
        private static bool HandContainsAll(IReadOnlyList<Card> hand, IReadOnlyList<Card> cards)
        {
            var pool = new List<Card>(hand);
            for (int i = 0; i < cards.Count; i++)
            {
                int j = pool.IndexOf(cards[i]);
                if (j < 0) return false;
                pool.RemoveAt(j);
            }
            return true;
        }
    }
}
