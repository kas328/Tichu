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

            var h = HandShape.Analyze(cards);
            if (h.HasDog || h.HasDragon) return Combination.Invalid; // 개/용은 단독만

            Combination c;
            if ((c = RecognizeBomb(h)).Type != CombinationType.Invalid) return c;          // Task 8
            if ((c = RecognizePairTripleFull(h)).Type != CombinationType.Invalid) return c; // Task 6
            if ((c = RecognizeStraight(h)).Type != CombinationType.Invalid) return c;        // Task 7
            if ((c = RecognizeConsecutivePairs(h)).Type != CombinationType.Invalid) return c;// Task 7
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

        // 멀티카드 판별용 손 모양 요약.
        private readonly struct HandShape
        {
            public readonly int[] Counts;      // index 1..14 (마작=1), 일반/마작 랭크별 개수
            public readonly int PhoenixCount;  // 0 또는 1
            public readonly bool HasDog;
            public readonly bool HasDragon;
            public readonly bool HasMahjong;
            public readonly int Points;        // 조합 총 점수
            public readonly int CardCount;     // 봉황 포함 전체 장수
            public readonly Card[] Source;     // 원본 카드(점수/문양 참조용)

            private HandShape(int[] counts, int phoenix, bool dog, bool dragon, bool mahjong,
                              int points, int cardCount, Card[] source)
            {
                Counts = counts; PhoenixCount = phoenix; HasDog = dog; HasDragon = dragon;
                HasMahjong = mahjong; Points = points; CardCount = cardCount; Source = source;
            }

            public static HandShape Analyze(ReadOnlySpan<Card> cards)
            {
                var counts = new int[15];
                int phoenix = 0, points = 0; bool dog = false, dragon = false, mahjong = false;
                var src = new Card[cards.Length];
                for (int i = 0; i < cards.Length; i++)
                {
                    Card c = cards[i]; src[i] = c; points += c.Points;
                    switch (c.Special)
                    {
                        case SpecialKind.Phoenix: phoenix++; break;
                        case SpecialKind.Dog: dog = true; break;
                        case SpecialKind.Dragon: dragon = true; break;
                        case SpecialKind.Mahjong: mahjong = true; counts[1]++; break;
                        default: counts[c.Rank]++; break;
                    }
                }
                return new HandShape(counts, phoenix, dog, dragon, mahjong, points, cards.Length, src);
            }
        }

        // 마작(랭크1)이 들어가면 페어/트리플/풀하우스/연속페어 불가.
        private static bool UsesMahjong(in HandShape h) => h.Counts[1] > 0;

        private static Combination RecognizePairTripleFull(in HandShape h)
        {
            if (UsesMahjong(h)) return Combination.Invalid;
            int n = h.CardCount;

            if (n == 2) // 페어
            {
                int rank = SingleRankWithPhoenix(h, needed: 2);
                return rank > 0
                    ? new Combination(CombinationType.Pair, h.Source, 2, rank * 2, h.Points)
                    : Combination.Invalid;
            }
            if (n == 3) // 트리플
            {
                int rank = SingleRankWithPhoenix(h, needed: 3);
                return rank > 0
                    ? new Combination(CombinationType.Triple, h.Source, 3, rank * 2, h.Points)
                    : Combination.Invalid;
            }
            if (n == 5) // 풀하우스 = 트리플 + 페어
            {
                int tripleRank = 0, pairRank = 0;
                if (h.PhoenixCount == 0)
                {
                    for (int r = 2; r <= 14; r++)
                    {
                        int cnt = h.Counts[r];
                        if (cnt == 0) continue;
                        if (cnt == 3) { if (tripleRank != 0) return Combination.Invalid; tripleRank = r; }
                        else if (cnt == 2) { if (pairRank != 0) return Combination.Invalid; pairRank = r; }
                        else return Combination.Invalid;
                    }
                }
                else // 봉황 1장: 자연 4장 패턴은 (3+1) 또는 (2+2)
                {
                    int triple = 0, single = 0, pairA = 0, pairB = 0;
                    for (int r = 2; r <= 14; r++)
                    {
                        int cnt = h.Counts[r];
                        if (cnt == 0) continue;
                        if (cnt == 3) { if (triple != 0) return Combination.Invalid; triple = r; }
                        else if (cnt == 2) { if (pairA == 0) pairA = r; else if (pairB == 0) pairB = r; else return Combination.Invalid; }
                        else if (cnt == 1) { if (single != 0) return Combination.Invalid; single = r; }
                        else return Combination.Invalid;
                    }
                    if (triple != 0 && single != 0 && pairA == 0)        // 3 + 1 + 봉황 → 봉황이 페어 완성
                    { tripleRank = triple; pairRank = single; }
                    else if (pairA != 0 && pairB != 0 && triple == 0 && single == 0) // 2 + 2 + 봉황 → 봉황이 트리플 완성(높은 페어)
                    { tripleRank = pairA > pairB ? pairA : pairB; pairRank = pairA > pairB ? pairB : pairA; }
                    else return Combination.Invalid;
                }

                if (tripleRank != 0 && pairRank != 0)
                    return new Combination(CombinationType.FullHouse, h.Source, 5, tripleRank * 2, h.Points);
                return Combination.Invalid;
            }
            return Combination.Invalid;
        }

        // n장이 한 랭크로 모이는지(봉황 1장 대체 허용) 검사 → 그 랭크 반환(실패 0).
        private static int SingleRankWithPhoenix(in HandShape h, int needed)
        {
            int found = 0;
            for (int r = 2; r <= 14; r++)
            {
                if (h.Counts[r] == 0) continue;
                if (found != 0) return 0;            // 두 종류 이상 → 실패
                found = r;
            }
            if (found == 0) return 0;
            int have = h.Counts[found] + h.PhoenixCount;
            return have == needed ? found : 0;
        }

        private static Combination RecognizeBomb(in HandShape h)
        {
            if (h.PhoenixCount > 0) return Combination.Invalid; // 봉황은 폭탄 불가
            int n = h.CardCount;

            if (n == 4) // 포카드
            {
                if (UsesMahjong(h)) return Combination.Invalid;
                for (int r = 2; r <= 14; r++)
                    if (h.Counts[r] == 4)
                        return new Combination(CombinationType.FourBomb, h.Source, 4, r * 2, h.Points);
                return Combination.Invalid;
            }

            if (n >= 5) // 스트레이트 플러시
            {
                // 같은 문양 + 연속 + 각 랭크 1장
                Suit suit = h.Source[0].Suit;
                int min = 0, max = 0, distinct = 0;
                for (int i = 0; i < h.Source.Length; i++)
                {
                    Card c = h.Source[i];
                    if (c.IsSpecial || c.Suit != suit) return Combination.Invalid;
                }
                for (int r = 2; r <= 14; r++)
                {
                    int cnt = h.Counts[r];
                    if (cnt == 0) continue;
                    if (cnt > 1) return Combination.Invalid;
                    if (min == 0) min = r;
                    max = r; distinct++;
                }
                if (distinct == n && (max - min + 1) == n)
                    return new Combination(CombinationType.StraightFlushBomb, h.Source, n, max * 2, h.Points);
                return Combination.Invalid;
            }

            return Combination.Invalid;
        }

        private static Combination RecognizeStraight(in HandShape h)
        {
            int n = h.CardCount;
            if (n < 5) return Combination.Invalid;

            // 일반/마작 랭크는 각 1장만 허용(중복 시 스트레이트 아님). 봉황 0/1.
            int min = 0, max = 0, distinct = 0;
            for (int r = 1; r <= 14; r++) // r=1은 마작(스트레이트 최하단으로 허용)
            {
                int cnt = h.Counts[r];
                if (cnt == 0) continue;
                if (cnt > 1) return Combination.Invalid;
                if (min == 0) min = r;
                max = r; distinct++;
            }
            if (distinct == 0) return Combination.Invalid;

            int span = max - min + 1;
            int gaps = span - distinct;          // [min,max] 내부 빠진 랭크 수
            int phoenix = h.PhoenixCount;
            int topValue;

            if (phoenix == 0)
            {
                if (gaps != 0 || distinct != n) return Combination.Invalid;
                topValue = max;
            }
            else // phoenix == 1
            {
                if (distinct + 1 != n) return Combination.Invalid;
                if (gaps == 1) topValue = max;                 // 내부 빈칸 메움
                else if (gaps == 0)                            // 끝 확장
                {
                    if (max + 1 <= 14) topValue = max + 1;     // 상단 확장 우선
                    else if (min - 1 >= 1) topValue = max;     // 상단 불가 → 하단 확장
                    else return Combination.Invalid;
                }
                else return Combination.Invalid;               // 봉황 1장으로 메울 수 없음
            }

            // n == 결과 스트레이트 길이(봉황 포함). 진입부 n>=5 보장으로 추가 길이검사 불요.
            return new Combination(CombinationType.Straight, h.Source, n, topValue * 2, h.Points);
        }

        private static Combination RecognizeConsecutivePairs(in HandShape h)
        {
            int n = h.CardCount;
            if (n < 4 || (n % 2) != 0) return Combination.Invalid;
            if (UsesMahjong(h)) return Combination.Invalid;

            int min = 0, max = 0, distinct = 0, singles = 0;
            for (int r = 2; r <= 14; r++) // r=2부터: 마작(랭크1)은 연속페어 불가
            {
                int cnt = h.Counts[r];
                if (cnt == 0) continue;
                if (cnt > 2) return Combination.Invalid;
                if (cnt == 1) singles++;
                if (min == 0) min = r;
                max = r; distinct++;
            }
            if (distinct == 0) return Combination.Invalid;

            int needPairs = n / 2;
            // 봉황이 단수 랭크 1개를 페어로 완성. 봉황 없으면 단수 0이어야.
            if (h.PhoenixCount == 0 && singles != 0) return Combination.Invalid;
            if (h.PhoenixCount == 1 && singles != 1) return Combination.Invalid;
            if (distinct != needPairs) return Combination.Invalid;       // 연속이며 빈칸 없음
            if (max - min + 1 != needPairs) return Combination.Invalid;  // 랭크가 연속

            return new Combination(CombinationType.ConsecutivePairs, h.Source, n, max * 2, h.Points);
        }
    }
}
