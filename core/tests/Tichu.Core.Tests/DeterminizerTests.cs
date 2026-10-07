using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Tichu.Core;
using Tichu.Core.Cards;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    /// <summary>Determinizer: 56장 폐쇄·hand-count 일치·관측 불변·치트 가드·결정성.</summary>
    public class DeterminizerTests
    {
        // 56장을 4×14로 그대로 나눈 닫힌 Play 상태(셋업 우회). 모든 카드가 손패에 분포 → 폐쇄 성립.
        private static GameState FreshPlayState(int turn)
        {
            var deck = Deck.CreateStandard();            // 56장, 결정적 순서
            Assert.That(deck.Count, Is.EqualTo(56));
            var hands = new IReadOnlyList<Card>[4];
            for (int i = 0; i < 4; i++)
                hands[i] = deck.GetRange(i * 14, 14);
            return GameFlowHelpers.PlayState(turn, hands);
        }

        [Test]
        public void Sample_keeps_observer_hand_and_does_not_mutate_source()
        {
            var src = FreshPlayState(0);
            var observerBefore = new List<Card>(src.Seats[0].Hand);
            var opp1Before = new List<Card>(src.Seats[1].Hand);

            var rng = new Rng(42UL);
            var det = Determinizer.Sample(src, 0, ref rng);

            // 관측 좌석(0) 손패 불변.
            Assert.That(det.Seats[0].Hand, Is.EqualTo(observerBefore));
            // 원본 src 는 변형되지 않는다(상대 손패도 그대로).
            Assert.That(src.Seats[1].Hand, Is.EqualTo(opp1Before));
        }

        [Test]
        public void Sample_redistributes_full_56_card_closure_by_hand_counts()
        {
            var src = FreshPlayState(0);
            var rng = new Rng(7UL);
            var det = Determinizer.Sample(src, 0, ref rng);

            // 4 손패 합집합 == 56장 전체(중복 없음).
            var all = new HashSet<Card>();
            for (int i = 0; i < 4; i++) all.UnionWith(det.Seats[i].Hand);
            Assert.That(all.Count, Is.EqualTo(56), "결정화 후에도 56장 폐쇄가 유지돼야 한다");

            // 각 상대 좌석의 장수가 원본과 동일.
            for (int i = 1; i < 4; i++)
                Assert.That(det.Seats[i].Hand.Count, Is.EqualTo(src.Seats[i].Hand.Count));
        }

        [Test]
        public void Sample_draws_opponents_only_from_unseen_pool_no_cheating()
        {
            var src = FreshPlayState(0);
            // 미관측 풀 = 56장 − 관측 좌석(0) 손패.
            var observerSet = new HashSet<Card>(src.Seats[0].Hand);

            var rng = new Rng(123UL);
            var det = Determinizer.Sample(src, 0, ref rng);

            // 상대 좌석에 배정된 모든 카드는 관측자가 든 카드일 수 없다(미관측 풀 출신).
            for (int seat = 1; seat < 4; seat++)
                foreach (var c in det.Seats[seat].Hand)
                    Assert.That(observerSet.Contains(c), Is.False,
                        $"치트: 상대 {seat} 가 관측자 손패 카드 {c} 를 받았다");

            // 상대에 배정된 카드 집합 == 미관측 풀 전체(빠짐/추가 없음).
            var dealt = new HashSet<Card>();
            for (int seat = 1; seat < 4; seat++) dealt.UnionWith(det.Seats[seat].Hand);
            Assert.That(dealt.Count, Is.EqualTo(56 - observerSet.Count));
        }

        [Test]
        public void Sample_is_deterministic_for_same_seed_and_varies_by_seed()
        {
            var src = FreshPlayState(0);

            var r1 = new Rng(99UL); var a = Determinizer.Sample(src, 0, ref r1);
            var r2 = new Rng(99UL); var b = Determinizer.Sample(src, 0, ref r2);
            for (int seat = 1; seat < 4; seat++)
                Assert.That(a.Seats[seat].Hand, Is.EqualTo(b.Seats[seat].Hand),
                    "같은 시드 → 같은 분배");

            var r3 = new Rng(100UL); var c = Determinizer.Sample(src, 0, ref r3);
            bool anyDiff = false;
            for (int seat = 1; seat < 4; seat++)
                if (!a.Seats[seat].Hand.SequenceEqual(c.Seats[seat].Hand)) anyDiff = true;
            Assert.That(anyDiff, Is.True, "다른 시드 → 분배가 달라져야 한다(42장 셔플)");
        }

        [Test]
        public void Sample_result_passes_engine_apply_for_a_legal_move()
        {
            var src = FreshPlayState(0);
            var rng = new Rng(5UL);
            var det = Determinizer.Sample(src, 0, ref rng);

            // 관측 좌석의 합법수 하나를 적용해도 엔진이 거부하지 않아야 한다.
            var legal = LegalMoveGenerator.LegalMoves(det, 0);
            Assert.That(legal.Count, Is.GreaterThan(0));
            var res = GameEngine.Apply(det, GameAction.Play(0, legal[0].Cards));
            Assert.That(res.Ok, Is.True, res.RejectReason);
        }

        // ── C1 교환 핀 ─────────────────────────────────────────────────────────

        [Test]
        public void Sample_pins_passed_card_to_recipient_seat_for_every_seed()
        {
            var src = FreshPlayState(0);
            // 미관측 풀에 있는 카드 하나를 "관측자가 좌석 1에게 넘긴 카드"로 삼는다.
            var pinnedCard = src.Seats[3].Hand[0];
            var pinned = new List<(Card, int)> { (pinnedCard, 1) };

            // 핀이 없으면 이 카드는 균등 셔플로 좌석 1에 ~1/3 만 안착 → 전 시드 안착은 핀이 있어야만 성립.
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var rng = new Rng(seed);
                var det = Determinizer.Sample(src, 0, ref rng, pinned);
                Assert.That(det.Seats[1].Hand.Contains(pinnedCard), Is.True,
                    $"핀한 카드가 시드 {seed} 에서 수령 좌석 1 에 없다");
                Assert.That(det.Seats[1].Hand.Count, Is.EqualTo(src.Seats[1].Hand.Count));
            }
        }

        [Test]
        public void Sample_with_pin_preserves_closure_and_observer_hand()
        {
            var src = FreshPlayState(0);
            var observerBefore = new List<Card>(src.Seats[0].Hand);
            var pinned = new List<(Card, int)> { (src.Seats[2].Hand[3], 3) };

            var rng = new Rng(17UL);
            var det = Determinizer.Sample(src, 0, ref rng, pinned);

            // 관측 손패 불변 + 56장 폐쇄 + 상대 장수 일치.
            Assert.That(det.Seats[0].Hand, Is.EqualTo(observerBefore));
            var all = new HashSet<Card>();
            for (int i = 0; i < 4; i++) all.UnionWith(det.Seats[i].Hand);
            Assert.That(all.Count, Is.EqualTo(56));
            for (int i = 1; i < 4; i++)
                Assert.That(det.Seats[i].Hand.Count, Is.EqualTo(src.Seats[i].Hand.Count));
        }

        [Test]
        public void Sample_ignores_pin_for_card_not_in_unseen_pool()
        {
            var src = FreshPlayState(0);
            // 관측자 자기 손패 카드는 미관측 풀에 없다(이미 보임) → 핀은 무시돼야 하고 관측 손패에 그대로 남는다.
            var ownCard = src.Seats[0].Hand[0];
            var pinned = new List<(Card, int)> { (ownCard, 2) };

            var rng = new Rng(23UL);
            var det = Determinizer.Sample(src, 0, ref rng, pinned);

            Assert.That(det.Seats[0].Hand.Contains(ownCard), Is.True, "관측자 카드는 그대로 관측자에게");
            Assert.That(det.Seats[2].Hand.Contains(ownCard), Is.False, "풀에 없는 핀 카드를 상대에 강제 배치하면 안 됨");
            var all = new HashSet<Card>();
            for (int i = 0; i < 4; i++) all.UnionWith(det.Seats[i].Hand);
            Assert.That(all.Count, Is.EqualTo(56));
        }

        // ── C3 티츄콜 강도 제약 ────────────────────────────────────────────────────────

        // 관측자(0)가 고카드(용·A·K)를 독점 → 미관측 풀의 유일한 ≥3 강도 카드는 봉황(3)뿐.
        // 좌석 1이 Tichu 콜 → 제약 ON이면 봉황을 좌석1에 배치해야 하한(3)을 넘긴다.
        private static GameState CallerHoardState()
        {
            var obs = new List<Card>
            {
                Card.Dragon,
                Card.Normal(14, Suit.Jade), Card.Normal(14, Suit.Sword), Card.Normal(14, Suit.Pagoda), Card.Normal(14, Suit.Star),
                Card.Normal(13, Suit.Jade), Card.Normal(13, Suit.Sword), Card.Normal(13, Suit.Pagoda), Card.Normal(13, Suit.Star),
                Card.Normal(2, Suit.Jade), Card.Normal(3, Suit.Jade), Card.Normal(4, Suit.Jade), Card.Normal(5, Suit.Jade), Card.Normal(6, Suit.Jade),
            };
            var obsSet = new HashSet<Card>(obs);
            var rest = new List<Card>();
            foreach (var c in Deck.CreateStandard())
                if (!obsSet.Contains(c)) rest.Add(c);            // 42장, 봉황 포함

            var hands = new IReadOnlyList<Card>[4];
            hands[0] = obs;
            hands[1] = rest.GetRange(0, 14);
            hands[2] = rest.GetRange(14, 14);
            hands[3] = rest.GetRange(28, 14);
            var state = GameFlowHelpers.PlayState(0, hands);
            state.Seats[1].Call = TichuCall.Tichu;
            return state;
        }

        [Test]
        public void Sample_call_constraint_raises_caller_to_floor_far_more_than_uniform()
        {
            var src = CallerHoardState();
            int onOk = 0, offOk = 0;
            for (ulong seed = 1; seed <= 60; seed++)
            {
                var r1 = new Rng(seed);
                var on = Determinizer.Sample(src, 0, ref r1, null, constrainCalls: true);
                if (ReachWeight.AtCallStrength(on, 1) >= 3) onOk++;

                var r2 = new Rng(seed);
                var off = Determinizer.Sample(src, 0, ref r2, null, constrainCalls: false);
                if (ReachWeight.AtCallStrength(off, 1) >= 3) offOk++;
            }
            Assert.That(onOk, Is.GreaterThanOrEqualTo(58), $"제약 ON은 거의 항상 콜 하한 충족 (실제 {onOk}/60)");
            Assert.That(onOk - offOk, Is.GreaterThanOrEqualTo(20), $"ON이 uniform보다 훨씬 자주 충족 (ON {onOk} vs OFF {offOk})");
        }

        [Test]
        public void Sample_call_constraint_off_is_bit_invariant()
        {
            var src = CallerHoardState();
            var r1 = new Rng(9UL); var a = Determinizer.Sample(src, 0, ref r1, null, constrainCalls: false);
            var r2 = new Rng(9UL); var b = Determinizer.Sample(src, 0, ref r2, null);   // 4-arg = 기본 false
            for (int seat = 1; seat < 4; seat++)
                Assert.That(a.Seats[seat].Hand, Is.EqualTo(b.Seats[seat].Hand), "제약 OFF는 기존 분배와 비트불변");
        }

        [Test]
        public void Sample_call_constraint_preserves_closure()
        {
            var src = CallerHoardState();
            var rng = new Rng(3UL);
            var det = Determinizer.Sample(src, 0, ref rng, null, constrainCalls: true);
            var all = new HashSet<Card>();
            for (int i = 0; i < 4; i++) all.UnionWith(det.Seats[i].Hand);
            Assert.That(all.Count, Is.EqualTo(56));
            for (int i = 1; i < 4; i++)
                Assert.That(det.Seats[i].Hand.Count, Is.EqualTo(src.Seats[i].Hand.Count));
        }
    }
}
