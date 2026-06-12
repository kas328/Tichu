using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Game;

namespace Tichu.Core.Tests
{
    public class DealExchangeTests
    {
        // ── 헬퍼 ────────────────────────────────────────────────────────────────

        /// <summary>모든 4명이 DeclineGrandTichu → Exchange 페이즈로 진입.</summary>
        private static GameState AdvanceToExchange(ulong seed = 1UL)
        {
            var s = GameEngine.NewRound(seed);
            for (int i = 0; i < 4; i++)
                GameEngine.Apply(s, GameAction.DeclineGrandTichu(i));
            return s;
        }

        /// <summary>
        /// 각 자리에서 교환 카드 3장(left, partner, right)을 선택하여 제출.
        /// 손에서 앞 3장을 그대로 사용하므로, 분포에 따라 달라진다.
        /// </summary>
        private static void SubmitAllExchanges(GameState s)
        {
            for (int seat = 0; seat < 4; seat++)
            {
                var hand = s.Seats[seat].Hand;
                // 앞 3장을 각각 left/partner/right 에 할당
                var toLeft    = new List<Card> { hand[0] };
                var toPartner = new List<Card> { hand[1] };
                var toRight   = new List<Card> { hand[2] };
                GameEngine.Apply(s, GameAction.Exchange(seat, toLeft, toPartner, toRight));
            }
        }

        // ── 딜 테스트 ────────────────────────────────────────────────────────────

        [Test]
        public void Deal8_is_deterministic_for_seed()
        {
            var s1 = GameEngine.NewRound(777UL);
            var s2 = GameEngine.NewRound(777UL);

            for (int i = 0; i < 4; i++)
            {
                Assert.That(s1.Seats[i].Hand.Count, Is.EqualTo(s2.Seats[i].Hand.Count));
                for (int j = 0; j < s1.Seats[i].Hand.Count; j++)
                    Assert.That(s1.Seats[i].Hand[j], Is.EqualTo(s2.Seats[i].Hand[j]),
                        $"seat {i} card {j} differs between two NewRound(777) calls");
            }
        }

        [Test]
        public void Deal8_gives_each_seat_8_and_phase_is_grand_tichu()
        {
            var s = GameEngine.NewRound(42UL);

            Assert.That(s.Phase, Is.EqualTo(RoundPhase.GrandTichuDecision));
            for (int i = 0; i < 4; i++)
                Assert.That(s.Seats[i].Hand.Count, Is.EqualTo(8),
                    $"seat {i} should have 8 cards after Deal8");

            // 전체 56장 중 32장 배분 + 24장 미배분
            int totalDealt = s.Seats.Sum(seat => seat.Hand.Count);
            Assert.That(totalDealt, Is.EqualTo(32));
        }

        [Test]
        public void Different_seeds_give_different_hands()
        {
            var s1 = GameEngine.NewRound(1UL);
            var s2 = GameEngine.NewRound(2UL);

            // 두 시드의 seat 0 손패가 완전히 동일할 확률은 극히 낮다
            bool anyDiff = false;
            for (int j = 0; j < 8; j++)
                if (!s1.Seats[0].Hand[j].Equals(s2.Seats[0].Hand[j])) { anyDiff = true; break; }
            Assert.That(anyDiff, Is.True, "different seeds should produce different hands");
        }

        // ── 큰 티츄 결정 테스트 ────────────────────────────────────────────────────

        [Test]
        public void GrandTichu_call_is_recorded()
        {
            var s = GameEngine.NewRound(1UL);
            var result = GameEngine.Apply(s, GameAction.CallGrandTichu(0));

            Assert.That(result.Ok, Is.True);
            Assert.That(s.Seats[0].Call, Is.EqualTo(TichuCall.GrandTichu));
        }

        [Test]
        public void Decline_does_not_set_grand_tichu_call()
        {
            var s = GameEngine.NewRound(1UL);
            var result = GameEngine.Apply(s, GameAction.DeclineGrandTichu(1));

            Assert.That(result.Ok, Is.True);
            Assert.That(s.Seats[1].Call, Is.EqualTo(TichuCall.None));
        }

        [Test]
        public void GrandTichu_second_decision_from_same_seat_is_rejected()
        {
            var s = GameEngine.NewRound(1UL);
            GameEngine.Apply(s, GameAction.CallGrandTichu(0));
            var second = GameEngine.Apply(s, GameAction.DeclineGrandTichu(0));

            Assert.That(second.Ok, Is.False);
            Assert.That(second.RejectReason, Is.Not.Empty);
        }

        [Test]
        public void All_four_decisions_advance_to_exchange_and_deal6()
        {
            var s = GameEngine.NewRound(1UL);

            // 3명 결정 후에는 아직 GrandTichuDecision
            for (int i = 0; i < 3; i++)
            {
                GameEngine.Apply(s, GameAction.DeclineGrandTichu(i));
                Assert.That(s.Phase, Is.EqualTo(RoundPhase.GrandTichuDecision));
            }

            // 4번째 결정 → Exchange + 14장
            GameEngine.Apply(s, GameAction.DeclineGrandTichu(3));
            Assert.That(s.Phase, Is.EqualTo(RoundPhase.Exchange));
            for (int i = 0; i < 4; i++)
                Assert.That(s.Seats[i].Hand.Count, Is.EqualTo(14),
                    $"seat {i} should have 14 cards after Deal6");
        }

        [Test]
        public void GrandTichu_call_after_phase_advances_is_rejected()
        {
            var s = AdvanceToExchange();
            var result = GameEngine.Apply(s, GameAction.CallGrandTichu(0));

            Assert.That(result.Ok, Is.False);
            Assert.That(result.RejectReason, Is.Not.Empty);
        }

        // ── 교환 테스트 ──────────────────────────────────────────────────────────

        [Test]
        public void Exchange_swaps_one_to_each_and_preserves_14()
        {
            var s = AdvanceToExchange(seed: 42UL);

            // 각 자리의 교환 카드를 기록해둔다
            var given = new Card[4, 3]; // given[seat, 0]=toLeft, [seat,1]=toPartner, [seat,2]=toRight
            for (int seat = 0; seat < 4; seat++)
            {
                given[seat, 0] = s.Seats[seat].Hand[0];
                given[seat, 1] = s.Seats[seat].Hand[1];
                given[seat, 2] = s.Seats[seat].Hand[2];
                var toLeft    = new List<Card> { given[seat, 0] };
                var toPartner = new List<Card> { given[seat, 1] };
                var toRight   = new List<Card> { given[seat, 2] };
                var r = GameEngine.Apply(s, GameAction.Exchange(seat, toLeft, toPartner, toRight));
                Assert.That(r.Ok, Is.True, $"seat {seat} exchange should be accepted");
            }

            // 완료 후 Phase == Play, 각 14장
            Assert.That(s.Phase, Is.EqualTo(RoundPhase.Play));
            for (int i = 0; i < 4; i++)
                Assert.That(s.Seats[i].Hand.Count, Is.EqualTo(14),
                    $"seat {i} should still have 14 cards after exchange");

            // seat 0 이 left(=seat1) 에게 보낸 카드가 seat 1 손에 있어야 한다
            Card cardSent0ToLeft = given[0, 0];
            Assert.That(s.Seats[1].Hand.Contains(cardSent0ToLeft), Is.True,
                "card sent from seat0 to seat1 (left) must be in seat1's hand");

            // seat 0 이 right(=seat3) 에게 보낸 카드가 seat 3 손에 있어야 한다
            Card cardSent0ToRight = given[0, 2];
            Assert.That(s.Seats[3].Hand.Contains(cardSent0ToRight), Is.True,
                "card sent from seat0 to seat3 (right) must be in seat3's hand");

            // 교환한 카드가 원래 자리 손에서는 없어야 한다 (단, 상대방이 같은 카드를 돌려주지 않은 경우)
            // → 간단히 giver 손에 해당 카드 count 가 늘지 않았는지로 검증하기 어려우므로
            //   대신 교환된 카드 수가 총 56장임을 확인
            int totalCards = s.Seats.Sum(seat => seat.Hand.Count);
            Assert.That(totalCards, Is.EqualTo(56));
        }

        [Test]
        public void Exchange_card_not_in_hand_is_rejected()
        {
            var s = AdvanceToExchange(seed: 1UL);

            // 손에 없는 카드로 교환 시도 (Dragon은 손에 있거나 없거나 다름 → 가짜 생성)
            // 확실히 손에 없는 카드를 만들기 위해, 전체 덱 중 손에 없는 카드를 찾는다
            var hand = new HashSet<Card>(s.Seats[0].Hand);
            Card notInHand = Card.Normal(2, Suit.Jade); // 기본값; 혹시 손에 있으면 다른 거 찾기
            foreach (var suit in new[] { Suit.Jade, Suit.Sword, Suit.Pagoda, Suit.Star })
                for (int r = 2; r <= 14; r++)
                {
                    var c = Card.Normal(r, suit);
                    if (!hand.Contains(c)) { notInHand = c; goto foundNotInHand; }
                }
            notInHand = Card.Phoenix; // 혹시 위에서 못 찾았으면
            foundNotInHand:

            var toLeft    = new List<Card> { notInHand };
            var toPartner = new List<Card> { s.Seats[0].Hand[1] };
            var toRight   = new List<Card> { s.Seats[0].Hand[2] };
            var result = GameEngine.Apply(s, GameAction.Exchange(0, toLeft, toPartner, toRight));

            Assert.That(result.Ok, Is.False);
            Assert.That(result.RejectReason, Is.Not.Empty);
        }

        [Test]
        public void Exchange_duplicate_cards_is_rejected()
        {
            var s = AdvanceToExchange(seed: 1UL);

            // 같은 카드를 두 곳에 보내려는 시도
            var sameCard  = s.Seats[0].Hand[0];
            var otherCard = s.Seats[0].Hand[1];
            var toLeft    = new List<Card> { sameCard };
            var toPartner = new List<Card> { sameCard }; // 중복
            var toRight   = new List<Card> { otherCard };
            var result = GameEngine.Apply(s, GameAction.Exchange(0, toLeft, toPartner, toRight));

            Assert.That(result.Ok, Is.False);
            Assert.That(result.RejectReason, Is.Not.Empty);
        }

        [Test]
        public void Exchange_submitted_twice_is_rejected()
        {
            var s = AdvanceToExchange(seed: 1UL);

            var hand = s.Seats[0].Hand;
            var toLeft    = new List<Card> { hand[0] };
            var toPartner = new List<Card> { hand[1] };
            var toRight   = new List<Card> { hand[2] };
            GameEngine.Apply(s, GameAction.Exchange(0, toLeft, toPartner, toRight));
            var second = GameEngine.Apply(s, GameAction.Exchange(0, toLeft, toPartner, toRight));

            Assert.That(second.Ok, Is.False);
            Assert.That(second.RejectReason, Is.Not.Empty);
        }

        [Test]
        public void Exchange_completes_to_play_with_mahjong_holder_leading()
        {
            var s = AdvanceToExchange(seed: 1UL);
            SubmitAllExchanges(s);

            Assert.That(s.Phase, Is.EqualTo(RoundPhase.Play));

            // Turn 이 마작 패 보유 자리와 일치해야 한다
            int mahjongSeat = -1;
            for (int i = 0; i < 4; i++)
                if (s.Seats[i].Hand.Contains(Card.Mahjong))
                    mahjongSeat = i;

            Assert.That(mahjongSeat, Is.Not.EqualTo(-1), "someone must hold Mahjong after exchange");
            Assert.That(s.Turn, Is.EqualTo(mahjongSeat),
                "Turn must equal the seat holding Mahjong");
        }

        // ── 잘못된 페이즈 거부 테스트 ─────────────────────────────────────────────

        [Test]
        public void Exchange_during_grand_tichu_decision_is_rejected()
        {
            var s = GameEngine.NewRound(1UL);
            // GrandTichuDecision 페이즈에서 Exchange 시도
            var hand = s.Seats[0].Hand;
            var toLeft    = new List<Card> { hand[0] };
            var toPartner = new List<Card> { hand[1] };
            var toRight   = new List<Card> { hand[2] };
            var result = GameEngine.Apply(s, GameAction.Exchange(0, toLeft, toPartner, toRight));

            Assert.That(result.Ok, Is.False);
            Assert.That(result.RejectReason, Is.Not.Empty);
        }

        [Test]
        public void CallGrandTichu_during_exchange_phase_is_rejected()
        {
            var s = AdvanceToExchange();
            var result = GameEngine.Apply(s, GameAction.CallGrandTichu(0));

            Assert.That(result.Ok, Is.False);
            Assert.That(result.RejectReason, Is.Not.Empty);
        }

        [Test]
        public void Play_action_during_setup_is_rejected()
        {
            var s = GameEngine.NewRound(1UL);
            var result = GameEngine.Apply(s,
                GameAction.Play(0, new List<Card> { s.Seats[0].Hand[0] }));

            Assert.That(result.Ok, Is.False);
            Assert.That(result.RejectReason, Is.Not.Empty);
        }

        [Test]
        public void Pass_action_during_setup_is_rejected()
        {
            var s = GameEngine.NewRound(1UL);
            var result = GameEngine.Apply(s, GameAction.Pass(0));

            Assert.That(result.Ok, Is.False);
            Assert.That(result.RejectReason, Is.Not.Empty);
        }

        // ── 범위 초과 좌석 인덱스 거부 테스트 ──────────────────────────────────────

        [Test]
        public void Apply_rejects_out_of_range_seat()
        {
            var s = GameEngine.NewRound(1UL);

            // 상한 초과 (seat 4)
            var resultHigh = GameEngine.Apply(s, GameAction.CallGrandTichu(4));
            Assert.That(resultHigh.Ok, Is.False, "seat=4 should be rejected");
            Assert.That(resultHigh.RejectReason, Is.Not.Empty);

            // 음수 좌석 (seat -1)
            var resultLow = GameEngine.Apply(s, GameAction.CallGrandTichu(-1));
            Assert.That(resultLow.Ok, Is.False, "seat=-1 should be rejected");
            Assert.That(resultLow.RejectReason, Is.Not.Empty);
        }
    }
}
