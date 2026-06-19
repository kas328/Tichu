using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using R3;
using Tichu.Core.Cards;
using Tichu.Core.Game;
using Tichu.GameFlow.Agents;
using Tichu.Presentation.ViewModel;

namespace Tichu.Presentation.Tests
{
    /// <summary>
    /// TableViewModel R3 상태투영 + 로컬 합법성 게이팅 검증.
    /// </summary>
    [TestFixture]
    public class TableViewModelTests
    {
        // ── 헬퍼 ──────────────────────────────────────────────────────────────

        /// <summary>Play 페이즈, 좌석 0에 2장(3옥,4옥)이 있는 상태를 구성한다.</summary>
        private static (GameState state, DecisionContext ctx) MakePlayState()
        {
            var s = new GameState { Phase = RoundPhase.Play, Turn = 0, CurrentTrick = null };
            for (int i = 0; i < 4; i++)
                s.Seats[i] = new PlayerSeat { SeatIndex = i };

            s.Seats[0].Hand.AddRange(new[]
            {
                Card.Normal(3, Suit.Jade),
                Card.Normal(4, Suit.Jade)
            });
            // 나머지 좌석에 카드 1장씩 배치(라운드 무결성)
            s.Seats[1].Hand.Add(Card.Normal(5, Suit.Jade));
            s.Seats[2].Hand.Add(Card.Normal(6, Suit.Jade));
            s.Seats[3].Hand.Add(Card.Normal(7, Suit.Jade));

            var ctx = new DecisionContext(s, 0);
            return (s, ctx);
        }

        // ── 테스트 ────────────────────────────────────────────────────────────

        /// <summary>
        /// ApplySnapshot 이 GameState 를 ReactiveProperty 에 올바르게 투영한다.
        /// - MyHand == seats[0].Hand
        /// - 각 좌석 HandCount == 실제 손패 수
        /// - Phase == GrandTichuDecision
        /// - CurrentTrick == null
        /// </summary>
        [Test]
        public void Snapshot_projects()
        {
            var state = GameEngine.NewRound(42UL);
            var vm = new TableViewModel(0);

            vm.ApplySnapshot(state);

            // Phase 투영
            Assert.That(vm.Phase.CurrentValue, Is.EqualTo(RoundPhase.GrandTichuDecision));

            // MyHand 투영
            Assert.That(vm.MyHand.CurrentValue, Is.EqualTo(state.Seats[0].Hand));

            // 각 좌석 손패 수 (NewRound → 8장씩)
            for (int i = 0; i < 4; i++)
                Assert.That(vm.HandCount(i).CurrentValue, Is.EqualTo(8),
                    $"좌석 {i} 손패 수가 8이어야 한다");

            // CurrentTrick 투영
            Assert.That(vm.CurrentTrick.CurrentValue, Is.Null);
        }

        /// <summary>
        /// RequestTurnDecisionAsync → PendingDecision 설정,
        /// SubmitTurnDecision(합법 수) → task 완료 + PendingDecision null.
        /// </summary>
        [Test]
        public void Request_then_legal_submit_completes()
        {
            var (_, ctx) = MakePlayState();
            var vm = new TableViewModel(0);

            // 리드 상태: CanPass == false, LegalMoves 에 항목 존재
            Assert.That(ctx.CanPass, Is.False, "리드에서는 패스 불가여야 한다");
            Assert.That(ctx.LegalMoves.Count, Is.GreaterThan(0), "합법 수가 존재해야 한다");

            // 요청 시작
            UniTask<TurnDecision> task = vm.RequestTurnDecisionAsync(ctx, default);

            // PendingDecision 확인
            // LegalMoves 는 호출마다 새 리스트를 생성하므로 Count 와 첫 번째 수의 카드 집합으로 비교한다.
            var legalMoves = ctx.LegalMoves; // 한 번만 계산해 재사용
            Assert.That(vm.PendingDecision.CurrentValue, Is.Not.Null);
            Assert.That(vm.PendingDecision.CurrentValue!.Kind, Is.EqualTo(DecisionKind.Turn));
            Assert.That(vm.PendingDecision.CurrentValue.Context.LegalMoves.Count,
                Is.EqualTo(legalMoves.Count), "PendingDecision 컨텍스트의 합법 수 목록 크기가 일치해야 한다");

            // 합법 수 제출
            var legalMove = legalMoves[0];
            bool accepted = vm.SubmitTurnDecision(TurnDecision.Play(legalMove));
            Assert.That(accepted, Is.True, "합법 수 제출이 수락되어야 한다");

            // task 완료 확인
            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            TurnDecision result = task.GetAwaiter().GetResult();
            Assert.That(result.Move, Is.EqualTo(legalMove));

            // PendingDecision 클리어 확인
            Assert.That(vm.PendingDecision.CurrentValue, Is.Null);
        }

        /// <summary>
        /// ApplySnapshot 이 각 좌석의 TichuCall 을 SeatCall ReactiveProperty 에 투영한다.
        /// </summary>
        [Test]
        public void ApplySnapshot_projects_seat_calls()
        {
            var vm = new TableViewModel(0);
            var state = GameEngine.NewRound(123UL);
            state.Seats[2].Call = TichuCall.GrandTichu;
            state.Seats[1].Call = TichuCall.Tichu;
            vm.ApplySnapshot(state);
            Assert.AreEqual(TichuCall.GrandTichu, vm.SeatCall(2).CurrentValue);
            Assert.AreEqual(TichuCall.Tichu, vm.SeatCall(1).CurrentValue);
            Assert.AreEqual(TichuCall.None, vm.SeatCall(0).CurrentValue);
        }

        /// <summary>
        /// 불법 제출(리드에서 패스)은 거부되고 task 가 완료되지 않으며 PendingDecision 도 유지된다.
        /// </summary>
        [Test]
        public void Illegal_submit_rejected()
        {
            var (_, ctx) = MakePlayState();
            var vm = new TableViewModel(0);

            // CanPass == false 확인
            Assert.That(ctx.CanPass, Is.False);

            UniTask<TurnDecision> task = vm.RequestTurnDecisionAsync(ctx, default);

            // 불법 제출: 리드에서 패스
            bool accepted = vm.SubmitTurnDecision(TurnDecision.Pass);
            Assert.That(accepted, Is.False, "패스가 거부되어야 한다");

            // task 아직 완료되지 않아야 한다
            Assert.That(task.Status, Is.Not.EqualTo(UniTaskStatus.Succeeded),
                "불법 제출 후 task 는 완료되지 않아야 한다");

            // PendingDecision 은 그대로여야 한다
            Assert.That(vm.PendingDecision.CurrentValue, Is.Not.Null,
                "불법 제출 후 PendingDecision 은 null 이 아니어야 한다");
        }
    }
}
