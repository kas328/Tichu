using System.Collections.Generic;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;

namespace Tichu.Core.Game
{
    /// <summary>라운드 셋업(Deal8 → 큰 티츄 → Deal6 → Exchange) 상태 머신.</summary>
    public static class GameEngine
    {
        internal const int SeatCount     = 4;
        internal const int Deal8Count    = 8;
        internal const int Deal6Count    = 6;
        internal const int FullHandCount = Deal8Count + Deal6Count; // 14

        // ── 공개 API ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 새 라운드를 생성한다. seed 가 동일하면 항상 동일한 배분이 보장된다.
        /// Phase = GrandTichuDecision, 각 자리 8장 배분.
        /// </summary>
        public static GameState NewRound(ulong seed)
        {
            var state = new GameState
            {
                RngSeed = seed,
                Rng     = new Rng(seed),
                Phase   = RoundPhase.GrandTichuDecision,
                Turn    = 0
            };

            for (int i = 0; i < SeatCount; i++)
                state.Seats[i] = new PlayerSeat { SeatIndex = i };

            // 셔플 (Rng는 구조체 → 로컬로 꺼내 셔플 후 다시 기록)
            var deck = Deck.CreateStandard();
            var rng = state.Rng;
            Deck.Shuffle(deck, ref rng);
            state.Rng = rng;

            // Deal8: 자리 0→1→2→3 순서로 8장씩
            // 스트라이드 딜: 좌석 s 는 deck[s*8 .. s*8+7] 를 받음 (균등 셔플이므로 라운드로빈과 분포 동일).
            var setup = new RoundSetup();
            for (int seat = 0; seat < SeatCount; seat++)
                for (int card = 0; card < Deal8Count; card++)
                    state.Seats[seat].Hand.Add(deck[seat * Deal8Count + card]);

            // 남은 24장 보관
            for (int i = SeatCount * Deal8Count; i < deck.Count; i++)
                setup.Undealt.Add(deck[i]);

            state.Setup = setup;
            return state;
        }

        /// <summary>
        /// 액션을 적용한다. 성공 시 state를 in-place 변경 후 ApplyResult.Accepted 반환;
        /// 실패 시 state 변경 없이 ApplyResult.Reject 반환.
        /// </summary>
        public static ApplyResult Apply(GameState s, GameAction a)
        {
            if (a.Seat < 0 || a.Seat >= SeatCount)
                return ApplyResult.Reject("invalid seat index");

            switch (a.Kind)
            {
                case GameActionKind.CallGrandTichu:
                    return ApplyGrandTichuDecision(s, a.Seat, callGrandTichu: true);

                case GameActionKind.DeclineGrandTichu:
                    return ApplyGrandTichuDecision(s, a.Seat, callGrandTichu: false);

                case GameActionKind.Exchange:
                    return ApplyExchange(s, a);

                case GameActionKind.CallTichu:
                    if (s.Phase == RoundPhase.Play)
                        return ApplyCallTichu(s, a.Seat);
                    return ApplyResult.Reject("wrong phase");

                case GameActionKind.Play:
                    if (s.Phase == RoundPhase.Play)
                        return ApplyPlay(s, a);
                    return ApplyResult.Reject("wrong phase");

                case GameActionKind.Pass:
                    if (s.Phase == RoundPhase.Play)
                        return ApplyPass(s, a.Seat);
                    return ApplyResult.Reject("wrong phase");

                default:
                    return ApplyResult.Reject("unknown action");
            }
        }

        // ── Play 페이즈: 작은 티츄 ────────────────────────────────────────────────

        private static ApplyResult ApplyCallTichu(GameState s, int seat)
        {
            var ps = s.Seats[seat];
            // 교환 후 14장, 아직 카드를 한 장도 내지 않았고, 다른 콜이 없으며, 아웃이 아닐 때만.
            if (ps.IsOut)
                return ApplyResult.Reject($"seat {seat} is out");
            if (ps.Call != TichuCall.None)
                return ApplyResult.Reject($"seat {seat} has already called");
            if (ps.Hand.Count != FullHandCount)
                return ApplyResult.Reject($"seat {seat} has already played a card");

            ps.Call = TichuCall.Tichu;
            return ApplyResult.Accepted;
        }

        // ── Play 페이즈: 카드 제출 ────────────────────────────────────────────────

        private static ApplyResult ApplyPlay(GameState s, GameAction a)
        {
            int seat   = a.Seat;
            var ps     = s.Seats[seat];
            var cards  = a.Cards!;

            if (ps.IsOut)
                return ApplyResult.Reject($"seat {seat} is out");

            // 손에 모두 있는지(중복 수량 포함) 확인.
            if (!HandContainsAll(ps.Hand, cards))
                return ApplyResult.Reject("played cards are not all in hand");

            var ctx   = TrickComparer.ContextFor(s.CurrentTrick!);
            var combo = CombinationRecognizer.Recognize(ToSpan(cards), ctx);
            if (combo.Type == CombinationType.Invalid)
                return ApplyResult.Reject("not a valid combination");

            // 소원 강제: 강제 상황이면 소원 랭크를 포함하지 않는 수는 거부.
            if (LegalMoveGenerator.WishIsEnforced(s, seat) && !LegalMoveGenerator.IncludesRank(cards, s.Wish!.Value))
                return ApplyResult.Reject($"wish {s.Wish.Value} must be satisfied");

            // 이번 플레이가 (기존) 소원을 만족시키면 적용 후 해제한다.
            // 마작 리드가 새로 거는 소원(2~14)은 마작 카드(랭크1)에 포함되지 않으므로 영향 없음.
            int? wishBefore = s.Wish;

            // 개: 단독으로만, 리드일 때만, 자기 턴일 때만.
            if (ContainsSpecial(cards, SpecialKind.Dog))
                return ApplyDog(s, seat);

            bool isLead = s.CurrentTrick == null;

            if (isLead)
            {
                if (seat != s.Turn)
                    return ApplyResult.Reject("not your turn to lead");

                RemoveCards(ps.Hand, cards);

                var trick = new Trick
                {
                    LeadType          = combo.Type,
                    LeadLength        = combo.Length,
                    Top               = combo,
                    TopOwnerSeat      = seat,
                    AccumulatedPoints = combo.PointsInPlay
                };
                trick.History.Add(new Play { Seat = seat, Combination = combo });
                s.CurrentTrick = trick;

                // 마작 소원 저장 (강제·해제는 LegalMoveGenerator.WishIsEnforced / ClearWishIfSatisfied).
                if (ContainsSpecial(cards, SpecialKind.Mahjong) && a.Wish.HasValue && a.Wish.Value >= 2 && a.Wish.Value <= 14)
                    s.Wish = a.Wish;

                ClearWishIfSatisfied(s, wishBefore, cards);
                MarkOutIfEmpty(s, ps);
                AdvanceAfterTopChange(s, seat);
                CheckTrickCompletion(s);
                CheckRoundEnd(s);
                return ApplyResult.Accepted;
            }

            // 팔로우.
            int turnBefore = s.Turn; // IsBombInterrupt 판단에 사용: 폭탄이 현재 턴 소유자와 다르면 인터럽트.
            var top = s.CurrentTrick!;
            if (combo.IsBomb)
            {
                // 폭탄은 자기 턴이 아니어도 가능하지만, 현재 Top을 이겨야 한다.
                if (!TrickComparer.Beats(combo, top))
                    return ApplyResult.Reject("bomb does not beat current top");
            }
            else
            {
                if (seat != s.Turn)
                    return ApplyResult.Reject("not your turn");
                if (!TrickComparer.Beats(combo, top))
                    return ApplyResult.Reject("does not beat current top");
            }

            RemoveCards(ps.Hand, cards);
            top.Top          = combo;
            top.TopOwnerSeat = seat;
            top.AccumulatedPoints += combo.PointsInPlay;
            top.History.Add(new Play
            {
                Seat            = seat,
                Combination     = combo,
                IsBombInterrupt = combo.IsBomb && seat != turnBefore
            });

            ClearWishIfSatisfied(s, wishBefore, cards);
            MarkOutIfEmpty(s, ps);
            AdvanceAfterTopChange(s, seat);
            CheckTrickCompletion(s);
            CheckRoundEnd(s);
            return ApplyResult.Accepted;
        }

        /// <summary>이번 플레이가 기존 소원을 만족시키면 소원을 해제한다.</summary>
        private static void ClearWishIfSatisfied(GameState s, int? wishBefore, IReadOnlyList<Card> cards)
        {
            if (wishBefore.HasValue && LegalMoveGenerator.IncludesRank(cards, wishBefore.Value))
                s.Wish = null;
        }

        /// <summary>개(단독): 트릭을 형성하지 않고 리드를 파트너에게 넘긴다.</summary>
        private static ApplyResult ApplyDog(GameState s, int seat)
        {
            // 팔로우 개도 Single로 인식되어 여기까지 도달한다 — 리드가 아니면 거부.
            if (s.CurrentTrick != null)
                return ApplyResult.Reject("dog can only be played as a lead");
            if (seat != s.Turn)
                return ApplyResult.Reject("not your turn to lead");

            var ps = s.Seats[seat];
            ps.Hand.Remove(Card.Dog); // 0점, 버려짐(어떤 더미에도 들어가지 않음).
            MarkOutIfEmpty(s, ps);

            int partner = Seating.Partner(seat);
            s.Turn = s.Seats[partner].IsOut
                ? Seating.NextActive(s.Seats, partner)
                : partner;

            CheckRoundEnd(s);
            return ApplyResult.Accepted;
        }

        // ── Play 페이즈: 패스 ─────────────────────────────────────────────────────

        private static ApplyResult ApplyPass(GameState s, int seat)
        {
            if (s.CurrentTrick == null)
                return ApplyResult.Reject("cannot pass on a lead");
            if (seat != s.Turn)
                return ApplyResult.Reject("not your turn");

            // 소원 강제 시 패스 불가.
            if (LegalMoveGenerator.WishIsEnforced(s, seat))
                return ApplyResult.Reject($"wish {s.Wish!.Value} must be satisfied; cannot pass");

            s.CurrentTrick.History.Add(new Play { Seat = seat, Combination = null });
            s.Turn = Seating.NextActive(s.Seats, seat);
            CheckTrickCompletion(s);
            return ApplyResult.Accepted;
        }

        // ── Play 페이즈: 트릭 완료 판정 ──────────────────────────────────────────

        /// <summary>
        /// 새 Top이 정해진 직후 턴을 진행한다.
        /// Top 소유자가 마지막 카드로 아웃되었다면 NextActive가 그를 건너뛴다.
        /// </summary>
        private static void AdvanceAfterTopChange(GameState s, int topSeat)
        {
            s.Turn = Seating.NextActive(s.Seats, topSeat);
        }

        /// <summary>
        /// 트릭 완료 여부를 판정하고, 완료 시 회수한다.
        /// 규칙: 턴이 Top 소유자에게 되돌아오면(모두 패스) 완료.
        /// 예외: Top 소유자가 그 플레이로 아웃되어 턴이 그에게 닿을 수 없으면,
        ///       그 이후 살아있는(아웃 아님) 좌석 수만큼 연속 패스가 쌓이면 완료.
        /// </summary>
        private static void CheckTrickCompletion(GameState s)
        {
            var trick = s.CurrentTrick;
            if (trick == null) return; // 개 등으로 트릭이 없을 수 있음.

            int owner = trick.TopOwnerSeat;
            bool complete;

            if (!s.Seats[owner].IsOut)
            {
                complete = s.Turn == owner;
            }
            else
            {
                // 소유자가 아웃됨: 살아있는 좌석 전원이 마지막 Top 이후 패스해야 완료.
                int activeCount = 0;
                for (int i = 0; i < SeatCount; i++)
                    if (!s.Seats[i].IsOut) activeCount++;
                complete = TrailingPassCount(trick) >= activeCount;
            }

            if (complete)
                CollectTrick(s, trick);
        }

        /// <summary>현재 Top 설정 이후 연속된 패스 수(History 후미의 연속 pass).</summary>
        private static int TrailingPassCount(Trick trick)
        {
            int n = 0;
            for (int i = trick.History.Count - 1; i >= 0; i--)
            {
                if (trick.History[i].Combination == null) n++;
                else break;
            }
            return n;
        }

        private static void CollectTrick(GameState s, Trick trick)
        {
            // 용 단독으로 이긴 트릭 표시 (ScoreCalculator의 용 양도 처리에 사용).
            MarkDragonIfApplicable(trick);

            s.CompletedTricks.Add(trick);
            s.CurrentTrick = null;

            int winner = trick.TopOwnerSeat;
            s.Turn = s.Seats[winner].IsOut
                ? Seating.NextActive(s.Seats, winner)
                : winner;
            // s.Wish 는 그대로 유지(트릭을 넘어 지속; 충족 시 해제는 ClearWishIfSatisfied에서 처리).
        }

        // ── 라운드 종료 판정 ──────────────────────────────────────────────────────

        /// <summary>
        /// 아웃이 발생한 직후 라운드 종료 여부를 판정한다.
        ///  - 원-투: 정확히 2명이 아웃이고 둘이 파트너 → 2번째 아웃에서 즉시 종료.
        ///  - 일반: 3명이 아웃(1명만 남음) → 종료.
        /// 종료 시 진행중 트릭을 현재 소유자에게 마무리하고 Phase=Scoring 으로 전환한다.
        /// 점수 계산은 ScoreCalculator(Part B)에서 수행한다.
        /// </summary>
        private static void CheckRoundEnd(GameState s)
        {
            if (s.Phase != RoundPhase.Play) return;

            int outCount = 0;
            int firstOutSeat = -1, secondOutSeat = -1;
            for (int i = 0; i < SeatCount; i++)
            {
                if (!s.Seats[i].IsOut) continue;
                outCount++;
                if (s.Seats[i].FinishOrder == 1) firstOutSeat = i;
                else if (s.Seats[i].FinishOrder == 2) secondOutSeat = i;
            }

            bool roundOver;
            if (outCount >= 3)
                roundOver = true;
            else if (outCount == 2)
                roundOver = secondOutSeat == Seating.Partner(firstOutSeat); // 원-투
            else
                roundOver = false;

            if (!roundOver) return;

            if (s.CurrentTrick != null)
                FinalizeOpenTrick(s);

            s.Phase = RoundPhase.Scoring;
        }

        /// <summary>진행중 트릭을 현재 Top 소유자에게 귀속시켜 CompletedTricks로 마무리한다.</summary>
        private static void FinalizeOpenTrick(GameState s)
        {
            var trick = s.CurrentTrick!;
            MarkDragonIfApplicable(trick);

            s.CompletedTricks.Add(trick);
            s.CurrentTrick = null;
        }

        private static void MarkDragonIfApplicable(Trick trick)
        {
            if (trick.Top != null && trick.Top.Cards.Count == 1 &&
                trick.Top.Cards[0].Special == SpecialKind.Dragon)
                trick.WonByDragon = true;
        }

        // ── Play 페이즈: 보조 ─────────────────────────────────────────────────────

        private static void MarkOutIfEmpty(GameState s, PlayerSeat ps)
        {
            if (ps.Hand.Count != 0 || ps.IsOut) return;
            // already 는 ps.IsOut = true 를 설정하기 전에 집계한다.
            // 따라서 FinishOrder = already + 1 이 정확하다.
            int already = 0;
            for (int i = 0; i < SeatCount; i++)
                if (s.Seats[i].IsOut) already++;
            ps.IsOut = true;
            ps.FinishOrder = already + 1;
        }

        private static bool ContainsSpecial(IReadOnlyList<Card> cards, SpecialKind kind)
        {
            for (int i = 0; i < cards.Count; i++)
                if (cards[i].Special == kind) return true;
            return false;
        }

        /// <summary>cards의 각 카드가 hand에 (중복 수량까지) 모두 있는지 확인.</summary>
        private static bool HandContainsAll(List<Card> hand, IReadOnlyList<Card> cards)
        {
            var remaining = new List<Card>(hand);
            for (int i = 0; i < cards.Count; i++)
            {
                if (!remaining.Remove(cards[i])) return false;
            }
            return true;
        }

        private static void RemoveCards(List<Card> hand, IReadOnlyList<Card> cards)
        {
            for (int i = 0; i < cards.Count; i++)
                hand.Remove(cards[i]);
        }

        private static System.ReadOnlySpan<Card> ToSpan(IReadOnlyList<Card> cards)
        {
            var arr = new Card[cards.Count];
            for (int i = 0; i < cards.Count; i++) arr[i] = cards[i];
            return arr;
        }

        // ── 큰 티츄 결정 ─────────────────────────────────────────────────────────

        private static ApplyResult ApplyGrandTichuDecision(GameState s, int seat, bool callGrandTichu)
        {
            if (s.Phase != RoundPhase.GrandTichuDecision)
                return ApplyResult.Reject("wrong phase: grand tichu decision only allowed in GrandTichuDecision phase");

            var setup = s.Setup!;
            if (setup.GrandTichuDecided[seat])
                return ApplyResult.Reject($"seat {seat} has already decided grand tichu");

            setup.GrandTichuDecided[seat] = true;
            if (callGrandTichu)
                s.Seats[seat].Call = TichuCall.GrandTichu;
            // decline → Call 그대로 None

            // 4명 모두 결정 완료?
            if (AllDecided(setup.GrandTichuDecided))
                DealSixAndAdvance(s);

            return ApplyResult.Accepted;
        }

        private static bool AllDecided(bool[] decided)
        {
            for (int i = 0; i < SeatCount; i++)
                if (!decided[i]) return false;
            return true;
        }

        /// <summary>6장 추가 배분 후 Exchange 페이즈로 전환.</summary>
        private static void DealSixAndAdvance(GameState s)
        {
            var setup = s.Setup!;
            // 남은 24장을 6장씩 배분 (seat 0→1→2→3 순서)
            // 스트라이드 딜: 좌석 s 는 Undealt[s*6 .. s*6+5] 를 받음 (균등 셔플이므로 라운드로빈과 분포 동일).
            for (int seat = 0; seat < SeatCount; seat++)
                for (int card = 0; card < Deal6Count; card++)
                    s.Seats[seat].Hand.Add(setup.Undealt[seat * Deal6Count + card]);

            setup.Undealt.Clear();
            setup.InitExchangeBuffers();
            s.Phase = RoundPhase.Exchange;
        }

        // ── 교환 ─────────────────────────────────────────────────────────────────

        private static ApplyResult ApplyExchange(GameState s, GameAction a)
        {
            if (s.Phase != RoundPhase.Exchange)
                return ApplyResult.Reject("wrong phase: exchange only allowed in Exchange phase");

            var setup = s.Setup!;
            int seat  = a.Seat;

            if (setup.ExchangeSubmitted![seat])
                return ApplyResult.Reject($"seat {seat} has already submitted exchange");

            var toLeft    = a.ExchangeToLeft!;
            var toPartner = a.ExchangePartner!;
            var toRight   = a.ExchangeToRight!;

            // 각 방향당 정확히 1장씩만 허용 (spec: one card to each other seat)
            if (toLeft.Count != 1 || toPartner.Count != 1 || toRight.Count != 1)
                return ApplyResult.Reject("exchange must provide exactly 1 card to each direction");

            var c0 = toLeft[0];
            var c1 = toPartner[0];
            var c2 = toRight[0];

            // 3장이 모두 다른 카드여야 한다
            if (c0.Equals(c1) || c1.Equals(c2) || c0.Equals(c2))
                return ApplyResult.Reject("exchange cards must be distinct");

            // 모두 현재 손에 있어야 한다
            var hand = s.Seats[seat].Hand;
            if (!hand.Contains(c0))
                return ApplyResult.Reject($"card {c0} is not in seat {seat}'s hand");
            if (!hand.Contains(c1))
                return ApplyResult.Reject($"card {c1} is not in seat {seat}'s hand");
            if (!hand.Contains(c2))
                return ApplyResult.Reject($"card {c2} is not in seat {seat}'s hand");

            // 버퍼에 저장 (아직 손에서 제거 X)
            setup.ExchangeToLeft![seat]    = new List<Card> { c0 };
            setup.ExchangeToPartner![seat] = new List<Card> { c1 };
            setup.ExchangeToRight![seat]   = new List<Card> { c2 };
            setup.ExchangeSubmitted[seat]   = true;

            // 4명 모두 제출 완료?
            if (AllSubmitted(setup.ExchangeSubmitted))
                FinalizeExchange(s);

            return ApplyResult.Accepted;
        }

        private static bool AllSubmitted(bool[] submitted)
        {
            for (int i = 0; i < SeatCount; i++)
                if (!submitted[i]) return false;
            return true;
        }

        /// <summary>
        /// 동시 교환을 확정한다.
        /// seat r 은 모든 다른 자리 g에서 g가 r에게 지정한 카드를 받는다.
        /// left   = (seat+1)%4
        /// partner = (seat+2)%4
        /// right  = (seat+3)%4
        /// </summary>
        private static void FinalizeExchange(GameState s)
        {
            var setup = s.Setup!;

            // 먼저 각 자리에서 건네줄 카드를 손에서 제거
            for (int g = 0; g < SeatCount; g++)
            {
                s.Seats[g].Hand.Remove(setup.ExchangeToLeft![g][0]);
                s.Seats[g].Hand.Remove(setup.ExchangeToPartner![g][0]);
                s.Seats[g].Hand.Remove(setup.ExchangeToRight![g][0]);
            }

            // 각 자리가 받을 카드를 추가
            // giver g 가 left=(g+1)%4 에게 ExchangeToLeft[g][0] 을 보냄
            // giver g 가 partner=(g+2)%4 에게 ExchangeToPartner[g][0] 을 보냄
            // giver g 가 right=(g+3)%4 에게 ExchangeToRight[g][0] 을 보냄
            for (int g = 0; g < SeatCount; g++)
            {
                int left    = (g + 1) % SeatCount;
                int partner = (g + 2) % SeatCount;
                int right   = (g + 3) % SeatCount;
                s.Seats[left].Hand.Add(setup.ExchangeToLeft![g][0]);
                s.Seats[partner].Hand.Add(setup.ExchangeToPartner![g][0]);
                s.Seats[right].Hand.Add(setup.ExchangeToRight![g][0]);
            }

            // 마작 패 보유자가 선공
            int mahjongSeat = FindMahjongSeat(s);
            s.Turn = mahjongSeat;
            s.CurrentTrick = null;
            s.Phase = RoundPhase.Play;
            s.Setup = null; // 임시 상태 해제
        }

        private static int FindMahjongSeat(GameState s)
        {
            for (int i = 0; i < SeatCount; i++)
                if (s.Seats[i].Hand.Contains(Card.Mahjong))
                    return i;
            // 마작 패는 반드시 어딘가 있어야 한다
            throw new System.InvalidOperationException("Mahjong card not found in any hand");
        }
    }
}
