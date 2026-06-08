using System.Collections.Generic;
using Tichu.Core.Cards;

namespace Tichu.Core.Game
{
    /// <summary>라운드 셋업(Deal8 → 큰 티츄 → Deal6 → Exchange) 상태 머신.</summary>
    public static class GameEngine
    {
        internal const int SeatCount  = 4;
        internal const int Deal8Count = 8;
        internal const int Deal6Count = 6;

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
                case GameActionKind.Play:
                case GameActionKind.Pass:
                    if (s.Phase == RoundPhase.Play)
                        return ApplyResult.Reject("not implemented in this task");
                    return ApplyResult.Reject("wrong phase");

                default:
                    return ApplyResult.Reject("unknown action");
            }
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
