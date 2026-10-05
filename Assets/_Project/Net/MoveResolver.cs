using System.Collections.Generic;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;

namespace Tichu.Net
{
    /// <summary>
    /// 클라이언트가 보낸 카드 집합을 서버의 합법수 하나로 확정한다.
    /// 합법수 목록에서 찾는 방식이라 봉황 대체·소원 강제·폭탄 인터럽트 같은 규칙이
    /// 자동으로 지켜진다. 조합 인식도 엔진과 항상 일치한다 — 1차(카드 신원 정확 일치)
    /// 경로는 생성기가 낸 합법수를 그대로 돌려주고, 2차(랭크 등가) 경로는 치환한 카드를
    /// CombinationRecognizer 로 다시 인식해 (Type, Rank, Length) 가 템플릿과 같을 때만
    /// 받아들인다(ResolveMove 의 주석 참고).
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
        /// 왜 2차가 재인식을 거쳐야 하는가 — 스트레이트플러시 폭탄 사고: 생성기는 같은 랭크
        /// 구간에 대해 무늬 무관 평범 스트레이트(GenerateStraights, 랭크당 bag.Cards[r][0]
        /// 하나만 대표로 사용)와 실제 한 무늬로만 이루어진 스트레이트플러시 폭탄
        /// (GenerateStraightFlushBombs)을 낼 수 있다. 손의 그 구간에서 매 랭크의 대표 카드가
        /// 우연히 전부 같은 무늬면, GenerateStraights 가 만든 카드 집합 자체가 플러시라
        /// Recognize 가 그것을 StraightFlushBomb 으로 인식하고, 뒤이은
        /// GenerateStraightFlushBombs 가 낸 동일 카드 집합과 ComboKey 가 겹쳐 중복 제거된다 —
        /// moves 에는 그 구간의 StraightFlushBomb 템플릿 하나만 남는다. 클라이언트가 같은
        /// 랭크의 혼합 무늬 변형을 보내면 1차(카드 신원)는 실패하고 2차가 그 템플릿에 랭크
        /// 등가로 매치되는데, 치환한 카드가 플러시가 아니므로 그대로 템플릿의 Type 을 붙이면
        /// 실제로는 평범한 스트레이트인 카드 집합이 폭탄으로 둔갑한다. 그래서 2차는 치환한
        /// cards 를 Recognize 로 다시 인식하고, (Type, Rank, Length) 가 템플릿과 다르면 그
        /// 템플릿을 버리고 다음 후보로 넘어간다 — 끝까지 일치하는 템플릿이 없으면 null
        /// (불법 처리, fail closed).
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

            // 2차: 1차에서 아무것도 못 찾았을 때만 랭크 등가로 완화한다(클래스 주석 참고).
            var ctx = TrickComparer.ContextFor(s.CurrentTrick);
            for (int i = 0; i < moves.Count; i++)
            {
                var m = moves[i];
                // 생성기는 랭크당 한 장만 내보내므로(GenerateSingles) 카드 신원 일치만 보면
                // 같은 랭크의 다른 무늬를 고른 사람의 멀쩡한 수가 거부된다. 랭크 다중집합이
                // 같으면 카드만 클라이언트 것으로 바꾸되, 치환한 카드를 엔진과 똑같이
                // Recognize 로 다시 인식해 (Type, Rank, Length) 가 템플릿과 같을 때만 받아들인다
                // — 무늬가 섞여 실제로는 플러시가 아닌데 템플릿의 폭탄 Type 이 그대로 붙는 사고
                // (ResolveMove 주석 참고)를 막는다.
                var cards = SubstituteCards(m.Cards, a.Cards);
                if (cards == null) continue;
                if (a.Rank != 0 && m.Rank != a.Rank) return null; // 의도 불일치 — 적용하지 않는다

                var recognized = CombinationRecognizer.Recognize(cards.ToArray(), ctx);
                if (recognized.Type != m.Type || recognized.Rank != m.Rank || recognized.Length != m.Length)
                    continue; // 재인식이 템플릿과 다르면 이 템플릿은 쓸 수 없다 — fail closed
                return recognized;
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
