using System.Collections.Generic;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;

namespace Tichu.Net
{
    /// <summary>
    /// 클라이언트가 보낸 카드 집합을 서버의 합법수 하나로 확정한다.
    /// 합법수 목록에서 찾는 방식이라 봉황 대체·소원 강제·폭탄 인터럽트 같은 규칙이
    /// 자동으로 지켜진다. 조합 인식은 1차(카드 신원 정확 일치) 경로에서는 엔진과 항상
    /// 일치하고, 2차(랭크 등가) 경로에서는 Rank·점수가 일치한다 — Type 이 다를 수 있는
    /// 유일한 경우(스트레이트플러시 폭탄 vs 평범한 스트레이트)는 1차에서 이미 걸러지므로
    /// 2차까지 내려오면 Type 도 항상 일치한다(ResolveMove 의 증명 주석 참고).
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
        ///
        /// 2패스 구조: 1차는 카드 신원이 정확히 같은 템플릿만 찾는다. 1차에서 하나도 못
        /// 찾았을 때만 2차(랭크 등가)로 완화한다.
        ///
        /// 왜 1차가 먼저여야 하는가 — 스트레이트플러시 폭탄 사고: 생성기는 같은 랭크 구간에
        /// 대해 무늬 무관 평범 스트레이트(GenerateStraights, 랭크당 bag.Cards[r][0] 하나만
        /// 대표로 사용)와 실제 한 무늬로만 이루어진 스트레이트플러시 폭탄(GenerateStraightFlushBombs)
        /// 을 **따로** 낼 수 있다. 손에 그 구간의 한 랭크에 다른 무늬 중복이 있으면 두 합법수가
        /// 같은 랭크 다중집합을 가지면서 Type 이 다르다(하나는 Straight, 하나는
        /// StraightFlushBomb). 랭크 등가만으로 매칭했다면 생성 순서상 먼저인 쪽(Straight,
        /// EnumerateCandidates 의 GenerateStraights 가 GenerateStraightFlushBombs 보다 먼저
        /// 호출됨)이 이겨, 클라이언트가 보유한 진짜 무늬 그대로의 폭탄을 제출해도 Type=Straight
        /// 로 확정돼 IsBomb 이 false 가 된다.
        ///
        /// 증명 — 2차(랭크 등가)에서는 이 혼동이 재발할 수 없다: 2차에 도달했다는 것은 클라이언트
        /// 카드와 카드 신원이 정확히 같은 템플릿이 1차에 하나도 없었다는 뜻이다. 만약 클라이언트
        /// 카드가 진짜 스트레이트플러시 폭탄(보유한 무늬 그대로의 연속런)을 이룬다면,
        /// GenerateStraightFlushBombs 가 정확히 그 카드들을 냈을 것이고 1차에서 이미 매치됐을
        /// 것이다 — 모순. 그러므로 2차에 남는 건 플러시가 아닌 수의 무늬-변형뿐이고, 첫
        /// 랭크-등가 템플릿이 올바른 Type 이다. 포카드 폭탄도 네 무늬를 전부 요구하므로 등가라면
        /// 반드시 카드 신원도 같다 — 2차에서 폭탄 Type 을 잘못 내보낼 여지가 없다.
        /// </summary>
        public static Combination? ResolveMove(GameState s, int seat, ClientAction a)
        {
            if (a.Cards == null || a.Cards.Count == 0) return null;

            // 랭크 등가는 보유하지 않은 카드를 낼 구멍을 연다(7♠ 만 들고 7♥ 를 보내도
            // 랭크 다중집합은 같다). 엔진의 HandContainsAll 에 맡기면 드라이버가 통보 없이
            // 버리므로, 검증 관문인 이 자리에서 막는다.
            if (!HandContainsAll(s.Seats[seat].Hand, a.Cards)) return null;

            var moves = LegalMoveGenerator.LegalMoves(s, seat);

            // 1차: 카드 신원이 정확히 같은 템플릿. 존재하면 Type 이 항상 올바르므로(엔진이
            // 낸 합법수 그 자체) 무조건 이 패스를 우선한다.
            for (int i = 0; i < moves.Count; i++)
            {
                var m = moves[i];
                if (!ExactCardMultiset(m.Cards, a.Cards)) continue;
                if (a.Rank != 0 && m.Rank != a.Rank) return null; // 의도 불일치 — 적용하지 않는다
                return m;
            }

            // 2차: 1차에서 아무것도 못 찾았을 때만 랭크 등가로 완화한다(클래스 주석의 증명 참고).
            for (int i = 0; i < moves.Count; i++)
            {
                var m = moves[i];
                // 생성기는 랭크당 한 장만 내보내므로(GenerateSingles) 카드 신원 일치만 보면
                // 같은 랭크의 다른 무늬를 고른 사람의 멀쩡한 수가 거부된다. 랭크 다중집합이
                // 같으면 받아들이고, 카드만 클라이언트 것으로 바꿔 적용한다 — 랭크가 같으니
                // 소원 강제·봉황 해석·점수는 합법수 그대로다(Type 은 위 증명대로 이 패스에선
                // 항상 m.Type 과 일치하는 유일한 후보다).
                var cards = SubstituteCards(m.Cards, a.Cards);
                if (cards == null) continue;
                if (a.Rank != 0 && m.Rank != a.Rank) return null; // 의도 불일치 — 적용하지 않는다
                return new Combination(m.Type, cards, m.Length, m.Rank, m.PointsInPlay);
            }
            return null;
        }

        /// <summary>두 카드 모음이 카드 신원까지 정확히 같은 다중집합인가(순서 무관, 중복 개수 포함).</summary>
        private static bool ExactCardMultiset(IReadOnlyList<Card> template, IReadOnlyList<Card> client)
        {
            if (template.Count != client.Count) return false;

            var pool = new List<Card>(client);
            for (int i = 0; i < template.Count; i++)
            {
                int j = pool.IndexOf(template[i]);
                if (j < 0) return false;
                pool.RemoveAt(j);
            }
            return true;
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
