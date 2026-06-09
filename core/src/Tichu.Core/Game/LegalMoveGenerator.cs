using System;
using System.Collections.Generic;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;

namespace Tichu.Core.Game
{
    /// <summary>
    /// 주어진 상태에서 한 좌석이 지금 낼 수 있는 모든 합법 조합을 생성/검증한다.
    /// 후보를 타입별로 만들어 CombinationRecognizer.Recognize로 정규화/검증하므로
    /// 인식기와 항상 일치한다. 패스는 별도 CanPass로 표현한다(조합 리스트엔 포함하지 않음).
    /// </summary>
    public static class LegalMoveGenerator
    {
        /// <summary>지금 seat이 낼 수 있는 모든 합법 조합. (패스는 CanPass로 별도 표현)</summary>
        public static IReadOnlyList<Combination> LegalMoves(GameState s, int seat)
        {
            var result = new List<Combination>();
            if (s.Phase != RoundPhase.Play) return result;

            var hand = s.Seats[seat].Hand;
            if (s.Seats[seat].IsOut || hand.Count == 0) return result;

            bool isLead = s.CurrentTrick == null;
            var ctx = TrickComparer.ContextFor(s.CurrentTrick);

            // 비폭탄은 자기 턴일 때만. 폭탄은 리드는 자기 턴, 폴로우는 턴 무관(인터럽트 허용).
            bool canPlayNonBomb = seat == s.Turn;
            bool canPlayBomb = isLead ? (seat == s.Turn) : true;
            if (!canPlayNonBomb && !canPlayBomb) return result;

            var bag = HandBag.Build(hand);
            var candidates = new List<Combination>();
            EnumerateCandidates(bag, ctx, candidates);

            // 폴로우/리드, 폭탄 제약, beats 필터.
            foreach (var c in candidates)
            {
                if (c.Type == CombinationType.Invalid) continue;
                bool bomb = c.IsBomb;
                if (bomb ? !canPlayBomb : !canPlayNonBomb) continue;

                if (isLead)
                {
                    // 리드: 모든 유효 조합 가능(개도 리드 가능).
                    result.Add(c);
                }
                else
                {
                    // 폴로우: Top을 이겨야 한다(개/용 등은 Beats가 걸러줌).
                    if (TrickComparer.Beats(c, s.CurrentTrick!))
                        result.Add(c);
                }
            }

            // 소원 강제 필터.
            ApplyWishFilter(s, result);

            return result;
        }

        /// <summary>지금 seat이 패스할 수 있는가? (폴로우 + 자기 턴 + 소원 미강제)</summary>
        public static bool CanPass(GameState s, int seat)
        {
            if (s.Phase != RoundPhase.Play) return false;
            if (s.CurrentTrick == null) return false;          // 리드는 패스 불가
            if (seat != s.Turn) return false;                  // 자기 턴에만

            // 소원이 강제되면 패스 불가.
            if (WishIsEnforced(s, seat)) return false;
            return true;
        }

        /// <summary>
        /// move가 지금 합법인가? GameEngine.Apply의 수락 조건과 일치해야 한다.
        /// 실패 시 reason 설정.
        /// </summary>
        public static bool IsLegal(GameState s, int seat, Combination move, out string reason)
        {
            reason = string.Empty;
            if (s.Phase != RoundPhase.Play) { reason = "wrong phase"; return false; }
            if (move == null || move.Type == CombinationType.Invalid) { reason = "not a valid combination"; return false; }

            var ps = s.Seats[seat];
            if (ps.IsOut) { reason = $"seat {seat} is out"; return false; }
            if (!HandContainsAll(ps.Hand, move.Cards)) { reason = "played cards are not all in hand"; return false; }

            bool isLead = s.CurrentTrick == null;
            bool bomb = move.IsBomb;

            if (isLead)
            {
                if (seat != s.Turn) { reason = "not your turn to lead"; return false; }
            }
            else
            {
                if (!bomb && seat != s.Turn) { reason = "not your turn"; return false; }
                if (!TrickComparer.Beats(move, s.CurrentTrick!)) { reason = "does not beat current top"; return false; }
            }

            // 소원 강제: 강제 상황이면 move가 소원 랭크를 포함해야 한다.
            if (WishIsEnforced(s, seat) && !IncludesRank(move.Cards, s.Wish!.Value))
            {
                reason = $"wish {s.Wish.Value} must be satisfied";
                return false;
            }

            return true;
        }

        // ── 소원(Wish) ────────────────────────────────────────────────────────────

        /// <summary>지금 seat에게 소원이 강제되는가(소원 랭크를 포함하는 합법수가 존재).</summary>
        internal static bool WishIsEnforced(GameState s, int seat)
        {
            if (!s.Wish.HasValue) return false;
            if (s.Seats[seat].IsOut) return false;

            int wish = s.Wish.Value;
            bool isLead = s.CurrentTrick == null;
            bool canPlayNonBomb = seat == s.Turn;
            bool canPlayBomb = isLead ? (seat == s.Turn) : true;
            if (!canPlayNonBomb && !canPlayBomb) return false;

            var ctx = TrickComparer.ContextFor(s.CurrentTrick);
            var bag = HandBag.Build(s.Seats[seat].Hand);
            var candidates = new List<Combination>();
            EnumerateCandidates(bag, ctx, candidates);

            foreach (var c in candidates)
            {
                if (c.Type == CombinationType.Invalid) continue;
                bool bomb = c.IsBomb;
                if (bomb ? !canPlayBomb : !canPlayNonBomb) continue;
                if (!isLead && !TrickComparer.Beats(c, s.CurrentTrick!)) continue;
                if (IncludesRank(c.Cards, wish)) return true;
            }
            return false;
        }

        /// <summary>소원이 강제되면 소원 랭크를 포함하지 않는 수를 제거한다.</summary>
        private static void ApplyWishFilter(GameState s, List<Combination> moves)
        {
            if (!s.Wish.HasValue) return;
            int wish = s.Wish.Value;

            bool anyIncludes = false;
            for (int i = 0; i < moves.Count; i++)
                if (IncludesRank(moves[i].Cards, wish)) { anyIncludes = true; break; }
            if (!anyIncludes) return; // 강제 불가 → 제한 없음

            moves.RemoveAll(m => !IncludesRank(m.Cards, wish));
        }

        internal static bool IncludesRank(IReadOnlyList<Card> cards, int rank)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                if (c.IsSpecial)
                {
                    if (c.Special == SpecialKind.Mahjong && rank == 1) return true;
                }
                else if (c.Rank == rank) return true;
            }
            return false;
        }

        // ── 후보 열거 ───────────────────────────────────────────────────────────

        // ThreadStatic 재사용 버퍼들 — 핫패스 할당 제거.
        [ThreadStatic] private static Card[]? _tryAddBuffer;
        [ThreadStatic] private static HashSet<ulong>? _seenSet;

        private static void EnumerateCandidates(in HandBag bag, TrickContext ctx, List<Combination> outList)
        {
            // HashSet 재사용: Clear()는 내부 버킷을 유지하므로 재할당 없음.
            var seen = _seenSet ??= new HashSet<ulong>(256);
            seen.Clear();

            void TryAdd(List<Card> cards)
            {
                int n = cards.Count;
                if (_tryAddBuffer == null || _tryAddBuffer.Length < n)
                    _tryAddBuffer = new Card[n < 16 ? 16 : n];
                for (int i = 0; i < n; i++) _tryAddBuffer[i] = cards[i];
                // 안전: Recognize()는 span을 HandShape.Source(새 Card[])로 복사하므로,
                // 반환된 Combination은 _tryAddBuffer를 참조하지 않는다(다음 후보가 덮어써도 무해).
                var combo = CombinationRecognizer.Recognize(_tryAddBuffer.AsSpan(0, n), ctx);
                if (combo.Type == CombinationType.Invalid) return;
                ulong key = ComboKey(combo);
                if (seen.Add(key)) outList.Add(combo);
            }

            GenerateSingles(bag, TryAdd);
            GeneratePairsTriplesBombs(bag, TryAdd);
            GenerateFullHouses(bag, TryAdd);
            GenerateStraights(bag, TryAdd);
            GenerateConsecutivePairs(bag, TryAdd);
            GenerateStraightFlushBombs(bag, TryAdd);
        }

        /// <summary>
        /// 조합을 중복 제거하기 위한 64비트 키.
        /// (Type, Length, Rank) 를 상위 비트에, 카드 집합의 XOR-fold 해시를 하위 비트에 배치한다.
        /// 카드 1장: Rank(5b) | Suit(3b) | Special(3b) = 11비트 → 64비트 FNV-1a 폴드.
        /// 충돌 확률은 2^-40 이하(최대 14장 조합, 실용상 무시).
        /// </summary>
        private static ulong ComboKey(Combination c)
        {
            // 상위: Type(3b), Length(5b), Rank(12b) — 20비트
            ulong header = ((ulong)(int)c.Type << 17) | ((ulong)c.Length << 12) | (ulong)(uint)c.Rank;

            // 카드 집합 해시: 순서 독립적 XOR fold (정렬 불필요)
            ulong cardHash = 0;
            for (int i = 0; i < c.Cards.Count; i++)
            {
                var card = c.Cards[i];
                // 카드 식별자: Rank(5b) | Suit(3b) | Special(3b) = 11비트 범위내 고유
                uint id = ((uint)card.Rank & 0x1F) | ((uint)(int)card.Suit << 5) | ((uint)(int)card.Special << 8);
                // FNV-1a 로 개별 해시 후 XOR-fold (순서 독립)
                ulong h = 14695981039346656037UL;
                h = (h ^ (ulong)id) * 1099511628211UL;
                cardHash ^= h;
            }

            return (header << 43) | (cardHash & 0x7FF_FFFF_FFFFUL);
        }

        // 단독: 각 distinct 일반 랭크 1장 + 특수카드(마작/개/봉황/용).
        private static void GenerateSingles(in HandBag bag, Action<List<Card>> tryAdd)
        {
            for (int r = 1; r <= 14; r++)
            {
                if (bag.Cards[r].Count == 0) continue;
                tryAdd(new List<Card> { bag.Cards[r][0] });
            }
            if (bag.HasDog) tryAdd(new List<Card> { Card.Dog });
            if (bag.HasPhoenix) tryAdd(new List<Card> { Card.Phoenix });
            if (bag.HasDragon) tryAdd(new List<Card> { Card.Dragon });
            // 마작은 Cards[1]에 들어있어 위 루프에서 처리됨.
        }

        // 페어/트리플/포카드: 랭크 카운트(+봉황은 페어/트리플만, 폭탄 불가).
        private static void GeneratePairsTriplesBombs(in HandBag bag, Action<List<Card>> tryAdd)
        {
            for (int r = 2; r <= 14; r++)
            {
                int cnt = bag.Cards[r].Count;
                // 페어
                if (cnt >= 2) tryAdd(Take(bag, r, 2));
                else if (cnt == 1 && bag.HasPhoenix) tryAdd(WithPhoenix(Take(bag, r, 1)));
                // 트리플
                if (cnt >= 3) tryAdd(Take(bag, r, 3));
                else if (cnt == 2 && bag.HasPhoenix) tryAdd(WithPhoenix(Take(bag, r, 2)));
                // 포카드 폭탄 (봉황 불가)
                if (cnt >= 4) tryAdd(Take(bag, r, 4));
            }
        }

        // 풀하우스: 트리플 랭크 × 페어 랭크. 봉황은 한 슬롯 보완.
        private static void GenerateFullHouses(in HandBag bag, Action<List<Card>> tryAdd)
        {
            // 자연 트리플(>=3) 또는 봉황보강 트리플(==2 + 봉황) + 다른 랭크의 페어.
            for (int t = 2; t <= 14; t++)
            {
                int tc = bag.Cards[t].Count;
                bool naturalTriple = tc >= 3;
                bool phoenixTriple = tc == 2; // 봉황으로 트리플 완성

                for (int p = 2; p <= 14; p++)
                {
                    if (p == t) continue;
                    int pc = bag.Cards[p].Count;
                    bool naturalPair = pc >= 2;
                    bool phoenixPair = pc == 1; // 봉황으로 페어 완성

                    // 봉황은 최대 1개. 트리플/페어 중 한 쪽만 봉황 사용 가능.
                    // case 1: 자연 트리플 + 자연 페어 (봉황 불요)
                    if (naturalTriple && naturalPair)
                    {
                        var cards = Take(bag, t, 3);
                        cards.AddRange(Take(bag, p, 2));
                        tryAdd(cards);
                    }
                    // case 2: 자연 트리플 + 봉황 페어
                    if (naturalTriple && phoenixPair && bag.HasPhoenix)
                    {
                        var cards = Take(bag, t, 3);
                        cards.AddRange(Take(bag, p, 1));
                        tryAdd(WithPhoenix(cards));
                    }
                    // case 3: 봉황 트리플 + 자연 페어
                    if (phoenixTriple && naturalPair && bag.HasPhoenix)
                    {
                        var cards = Take(bag, t, 2);
                        cards.AddRange(Take(bag, p, 2));
                        tryAdd(WithPhoenix(cards));
                    }
                }
            }
        }

        // 스트레이트: 연속 구간 길이 >=5. 마작(1)=하단, A(14)=상단. 봉황 1개 보완.
        private static void GenerateStraights(in HandBag bag, Action<List<Card>> tryAdd)
        {
            // 랭크 1..14에 대해 "있음(>=1)" 비트로 보고, 시작 lo와 길이 len을 스캔.
            // 봉황을 0개/1개 쓰는 경우를 모두 시도해 Recognize에 위임.
            for (int lo = 1; lo <= 14; lo++)
            {
                for (int len = 5; lo + len - 1 <= 14; len++)
                {
                    int hi = lo + len - 1;
                    // 0봉황: 구간 전체 보유.
                    BuildStraight(bag, lo, hi, usePhoenix: false, tryAdd);
                    // 1봉황: 구간에서 정확히 한 랭크가 빠진 경우(봉황이 메움).
                    if (bag.HasPhoenix)
                        BuildStraight(bag, lo, hi, usePhoenix: true, tryAdd);
                }
            }
        }

        // [lo,hi] 구간 스트레이트 카드 구성. usePhoenix=false면 전부 보유해야;
        // true면 정확히 한 랭크가 비고 나머지 보유.
        private static void BuildStraight(in HandBag bag, int lo, int hi, bool usePhoenix, Action<List<Card>> tryAdd)
        {
            int missing = 0;
            int missingRank = 0;
            for (int r = lo; r <= hi; r++)
            {
                if (bag.Cards[r].Count == 0) { missing++; missingRank = r; }
            }

            if (!usePhoenix)
            {
                if (missing != 0) return;
                var cards = new List<Card>();
                for (int r = lo; r <= hi; r++) cards.Add(bag.Cards[r][0]);
                tryAdd(cards);
            }
            else
            {
                if (missing != 1) return; // 봉황은 내부 한 칸만 메움(끝 확장은 별도 lo/hi 조합으로 커버됨)
                var cards = new List<Card>();
                for (int r = lo; r <= hi; r++)
                    if (r != missingRank) cards.Add(bag.Cards[r][0]);
                cards.Add(Card.Phoenix);
                tryAdd(cards);
            }
        }

        // 연속 페어: 연속 랭크 각 2장(봉황이 한 페어 보완), 길이 >=4(>=2페어).
        private static void GenerateConsecutivePairs(in HandBag bag, Action<List<Card>> tryAdd)
        {
            for (int lo = 2; lo <= 14; lo++)
            {
                for (int pairs = 2; lo + pairs - 1 <= 14; pairs++)
                {
                    int hi = lo + pairs - 1;
                    // 0봉황: 각 랭크 2장 이상.
                    BuildConsecutivePairs(bag, lo, hi, usePhoenix: false, tryAdd);
                    // 1봉황: 정확히 한 랭크가 1장(나머지 >=2).
                    if (bag.HasPhoenix)
                        BuildConsecutivePairs(bag, lo, hi, usePhoenix: true, tryAdd);
                }
            }
        }

        private static void BuildConsecutivePairs(in HandBag bag, int lo, int hi, bool usePhoenix, Action<List<Card>> tryAdd)
        {
            int singleRank = 0, singleCount = 0;
            for (int r = lo; r <= hi; r++)
            {
                int cnt = bag.Cards[r].Count;
                if (cnt == 0) return;                 // 빈 랭크 → 연속 불가
                if (cnt == 1) { singleCount++; singleRank = r; }
            }

            if (!usePhoenix)
            {
                if (singleCount != 0) return;         // 봉황 없이 모두 페어여야
                var cards = new List<Card>();
                for (int r = lo; r <= hi; r++) cards.AddRange(Take(bag, r, 2));
                tryAdd(cards);
            }
            else
            {
                if (singleCount != 1) return;         // 봉황이 정확히 한 단수 랭크를 페어로
                var cards = new List<Card>();
                for (int r = lo; r <= hi; r++)
                    cards.AddRange(Take(bag, r, r == singleRank ? 1 : 2));
                cards.Add(Card.Phoenix);
                tryAdd(cards);
            }
        }

        // 스트레이트 플러시 폭탄: 같은 문양 연속 길이 >=5(봉황 불가).
        private static void GenerateStraightFlushBombs(in HandBag bag, Action<List<Card>> tryAdd)
        {
            // 문양별로 보유 랭크를 모아 연속 구간을 찾는다(마작/특수 제외).
            foreach (Suit suit in new[] { Suit.Jade, Suit.Sword, Suit.Pagoda, Suit.Star })
            {
                for (int lo = 2; lo <= 14; lo++)
                {
                    for (int len = 5; lo + len - 1 <= 14; len++)
                    {
                        int hi = lo + len - 1;
                        var cards = new List<Card>();
                        bool ok = true;
                        for (int r = lo; r <= hi; r++)
                        {
                            if (!bag.TryGetSuited(r, suit, out var card)) { ok = false; break; }
                            cards.Add(card);
                        }
                        if (ok) tryAdd(cards);
                    }
                }
            }
        }

        // ── 보조 ────────────────────────────────────────────────────────────────

        private static List<Card> Take(in HandBag bag, int rank, int n)
        {
            var list = new List<Card>(n);
            for (int i = 0; i < n; i++) list.Add(bag.Cards[rank][i]);
            return list;
        }

        private static List<Card> WithPhoenix(List<Card> cards)
        {
            cards.Add(Card.Phoenix);
            return cards;
        }

        private static bool HandContainsAll(List<Card> hand, IReadOnlyList<Card> cards)
        {
            var remaining = new List<Card>(hand);
            for (int i = 0; i < cards.Count; i++)
                if (!remaining.Remove(cards[i])) return false;
            return true;
        }

        /// <summary>손패를 랭크별 카드 리스트 + 특수카드 플래그로 정리.</summary>
        private readonly struct HandBag
        {
            public readonly List<Card>[] Cards; // index 1..14 (마작=1), 일반/마작 카드
            public readonly bool HasPhoenix;
            public readonly bool HasDog;
            public readonly bool HasDragon;

            private HandBag(List<Card>[] cards, bool phoenix, bool dog, bool dragon)
            {
                Cards = cards; HasPhoenix = phoenix; HasDog = dog; HasDragon = dragon;
            }

            public static HandBag Build(List<Card> hand)
            {
                var cards = new List<Card>[15];
                for (int r = 0; r < 15; r++) cards[r] = new List<Card>();
                bool phoenix = false, dog = false, dragon = false;
                foreach (var c in hand)
                {
                    switch (c.Special)
                    {
                        case SpecialKind.Phoenix: phoenix = true; break;
                        case SpecialKind.Dog: dog = true; break;
                        case SpecialKind.Dragon: dragon = true; break;
                        case SpecialKind.Mahjong: cards[1].Add(c); break;
                        default: cards[c.Rank].Add(c); break;
                    }
                }
                return new HandBag(cards, phoenix, dog, dragon);
            }

            public bool TryGetSuited(int rank, Suit suit, out Card card)
            {
                var list = Cards[rank];
                for (int i = 0; i < list.Count; i++)
                    if (list[i].Suit == suit) { card = list[i]; return true; }
                card = default;
                return false;
            }
        }
    }
}
