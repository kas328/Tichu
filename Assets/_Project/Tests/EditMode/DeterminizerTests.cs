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
    }
}
