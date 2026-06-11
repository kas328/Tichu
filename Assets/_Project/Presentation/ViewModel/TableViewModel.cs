using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;
using Tichu.GameFlow.Agents;

namespace Tichu.Presentation.ViewModel
{
    /// <summary>
    /// IHumanInputPort 구현체 + R3 ReactiveProperty 기반 상태 투영.
    /// AsyncGameDriver 가 Apply 후 ApplySnapshot 을 호출해 상태를 갱신하고,
    /// HumanAgent 는 RequestXxxAsync 를 통해 입력을 기다린다.
    /// UI 뷰(T8)는 ReactiveProperty 를 구독해 화면을 갱신한다.
    /// </summary>
    public sealed class TableViewModel : IHumanInputPort
    {
        // ── 내 좌석 ──────────────────────────────────────────────────────────
        private readonly int _mySeat;

        // ── ReactiveProperty ─────────────────────────────────────────────────

        /// <summary>현재 라운드 페이즈.</summary>
        public ReactiveProperty<RoundPhase> Phase { get; } = new ReactiveProperty<RoundPhase>();

        /// <summary>내 손패.</summary>
        public ReactiveProperty<IReadOnlyList<Card>> MyHand { get; }
            = new ReactiveProperty<IReadOnlyList<Card>>(new List<Card>());

        /// <summary>현재 트릭.</summary>
        public ReactiveProperty<Trick?> CurrentTrick { get; } = new ReactiveProperty<Trick?>();

        /// <summary>마지막으로 완료된 라운드 결산. 라운드 시작 전 null.</summary>
        public ReactiveProperty<RoundResult?> RoundResult { get; } = new ReactiveProperty<RoundResult?>();

        /// <summary>
        /// 현재 인간 플레이어에게 대기 중인 결정 요청.
        /// 자기 턴이 아닌 경우 null.
        /// </summary>
        public ReactiveProperty<DecisionRequest?> PendingDecision { get; }
            = new ReactiveProperty<DecisionRequest?>();

        // 좌석별 손패 수 (0..3)
        private readonly ReactiveProperty<int>[] _handCounts = new ReactiveProperty<int>[4];

        /// <summary>좌석 i 의 손패 수 ReactiveProperty.</summary>
        public ReactiveProperty<int> HandCount(int seat) => _handCounts[seat];

        // ── 진행 중인 결정 TCS ───────────────────────────────────────────────
        // 각 결정 종류별로 최대 1개의 대기 TCS 를 보관한다.
        private UniTaskCompletionSource<bool>? _grandTichuTcs;
        private UniTaskCompletionSource<ExchangeChoice>? _exchangeTcs;
        private UniTaskCompletionSource<bool>? _tichuTcs;
        private UniTaskCompletionSource<TurnDecision>? _turnTcs;
        private UniTaskCompletionSource<Combination?>? _bombTcs;
        private UniTaskCompletionSource<int>? _dragonTcs;

        // 각 결정 요청 시 컨텍스트를 보관한다(Submit 시 합법성 검사에 사용).
        private DecisionContext _pendingCtx;

        // ── 생성자 ──────────────────────────────────────────────────────────

        public TableViewModel(int mySeat)
        {
            _mySeat = mySeat;
            for (int i = 0; i < 4; i++)
                _handCounts[i] = new ReactiveProperty<int>(0);
        }

        // ── 상태 투영 ────────────────────────────────────────────────────────

        /// <summary>
        /// GameEngine.Apply 후 드라이버가 호출. GameState 를 ReactiveProperty 에 투영한다.
        /// </summary>
        public void ApplySnapshot(GameState s)
        {
            Phase.Value = s.Phase;
            // ReactiveProperty 는 같은 참조를 다시 넣으면 통지하지 않는다. 엔진은 손패 List·트릭을
            // 제자리 변경하므로(같은 참조), 변경 감지를 위해 사본/강제 재통지가 필요하다.
            MyHand.Value = new List<Card>(s.Seats[_mySeat].Hand);   // 사본 → 항상 통지
            CurrentTrick.Value = null;                              // 트릭 Top 갱신 반영: null→값으로 강제 통지
            CurrentTrick.Value = s.CurrentTrick;
            for (int i = 0; i < 4; i++)
                _handCounts[i].Value = s.Seats[i].Hand.Count;
        }

        // ── IHumanInputPort 구현 ─────────────────────────────────────────────

        /// <inheritdoc/>
        public UniTask<bool> RequestGrandTichuAsync(DecisionContext ctx, CancellationToken ct)
        {
            // 인간 프롬프트 시점에 현재 상태를 테이블에 반영한다.
            ApplySnapshot(ctx.State);
            _pendingCtx = ctx;
            _grandTichuTcs = new UniTaskCompletionSource<bool>();
            PendingDecision.Value = new DecisionRequest(DecisionKind.GrandTichu, ctx);
            ct.Register(CancelGrandTichu);
            return _grandTichuTcs.Task;
        }

        /// <inheritdoc/>
        public UniTask<ExchangeChoice> RequestExchangeAsync(DecisionContext ctx, CancellationToken ct)
        {
            // 인간 프롬프트 시점에 현재 상태를 테이블에 반영한다.
            ApplySnapshot(ctx.State);
            _pendingCtx = ctx;
            _exchangeTcs = new UniTaskCompletionSource<ExchangeChoice>();
            PendingDecision.Value = new DecisionRequest(DecisionKind.Exchange, ctx);
            ct.Register(CancelExchange);
            return _exchangeTcs.Task;
        }

        /// <inheritdoc/>
        public UniTask<bool> RequestTichuAsync(DecisionContext ctx, CancellationToken ct)
        {
            // 인간 프롬프트 시점에 현재 상태를 테이블에 반영한다.
            ApplySnapshot(ctx.State);
            _pendingCtx = ctx;
            _tichuTcs = new UniTaskCompletionSource<bool>();
            PendingDecision.Value = new DecisionRequest(DecisionKind.Tichu, ctx);
            ct.Register(CancelTichu);
            return _tichuTcs.Task;
        }

        /// <inheritdoc/>
        public UniTask<TurnDecision> RequestTurnDecisionAsync(DecisionContext ctx, CancellationToken ct)
        {
            // 인간 프롬프트 시점에 현재 상태를 테이블에 반영한다.
            ApplySnapshot(ctx.State);
            _pendingCtx = ctx;
            _turnTcs = new UniTaskCompletionSource<TurnDecision>();
            PendingDecision.Value = new DecisionRequest(DecisionKind.Turn, ctx);
            ct.Register(CancelTurn);
            return _turnTcs.Task;
        }

        /// <inheritdoc/>
        public UniTask<Combination?> RequestBombAsync(DecisionContext ctx, CancellationToken ct)
        {
            // 인간 프롬프트 시점에 현재 상태를 테이블에 반영한다.
            ApplySnapshot(ctx.State);
            _pendingCtx = ctx;
            _bombTcs = new UniTaskCompletionSource<Combination?>();
            PendingDecision.Value = new DecisionRequest(DecisionKind.Bomb, ctx);
            ct.Register(CancelBomb);
            return _bombTcs.Task;
        }

        /// <inheritdoc/>
        public UniTask<int> RequestDragonRecipientAsync(DecisionContext ctx, CancellationToken ct)
        {
            // 인간 프롬프트 시점에 현재 상태를 테이블에 반영한다.
            ApplySnapshot(ctx.State);
            _pendingCtx = ctx;
            _dragonTcs = new UniTaskCompletionSource<int>();
            PendingDecision.Value = new DecisionRequest(DecisionKind.DragonRecipient, ctx);
            ct.Register(CancelDragon);
            return _dragonTcs.Task;
        }

        // ── Submit (로컬 합법성 게이팅) ──────────────────────────────────────

        /// <summary>
        /// 큰 티츄 선언 여부를 제출한다.
        /// GrandTichu 는 어떤 bool 값이든 합법이다.
        /// </summary>
        /// <returns>수락 여부.</returns>
        public bool SubmitGrandTichu(bool call)
        {
            if (_grandTichuTcs == null) return false;
            var tcs = _grandTichuTcs;
            _grandTichuTcs = null;
            PendingDecision.Value = null;
            tcs.TrySetResult(call);
            return true;
        }

        /// <summary>
        /// 교환 카드를 제출한다.
        /// 3장이 손패 안에 있고 서로 달라야 한다.
        /// </summary>
        public bool SubmitExchange(ExchangeChoice choice)
        {
            if (_exchangeTcs == null) return false;
            var hand = _pendingCtx.MyHand;
            // 3장이 손패에 있고 서로 다른지 검사
            if (!IsDistinctAndInHand(choice.ToLeft, choice.ToPartner, choice.ToRight, hand))
                return false;
            var tcs = _exchangeTcs;
            _exchangeTcs = null;
            PendingDecision.Value = null;
            tcs.TrySetResult(choice);
            return true;
        }

        /// <summary>
        /// 작은 티츄 선언 여부를 제출한다. 어떤 bool 값이든 합법이다.
        /// </summary>
        public bool SubmitTichu(bool call)
        {
            if (_tichuTcs == null) return false;
            var tcs = _tichuTcs;
            _tichuTcs = null;
            PendingDecision.Value = null;
            tcs.TrySetResult(call);
            return true;
        }

        /// <summary>
        /// 자기 턴 결정을 제출한다.
        /// - 패스: ctx.CanPass 일 때만 허용.
        /// - 패 내기: 카드 집합이 ctx.LegalMoves 중 하나와 일치해야 한다.
        /// </summary>
        /// <returns>수락 여부(false 면 거부, await 는 계속 대기).</returns>
        public bool SubmitTurnDecision(TurnDecision d)
        {
            if (_turnTcs == null) return false;

            if (d.IsPass)
            {
                if (!_pendingCtx.CanPass) return false;
            }
            else
            {
                // d.Move 의 카드 집합이 합법 수 목록 중 하나와 멀티셋 일치해야 한다.
                if (d.Move == null || !IsMoveLegal(d.Move, _pendingCtx.LegalMoves))
                    return false;
            }

            var tcs = _turnTcs;
            _turnTcs = null;
            PendingDecision.Value = null;
            tcs.TrySetResult(d);
            return true;
        }

        /// <summary>
        /// 폭탄 인터럽트를 제출한다.
        /// null(거절) 은 항상 허용.
        /// 폭탄은 ctx.LegalMoves 에 있는 폭탄과 카드 집합이 일치해야 한다.
        /// </summary>
        public bool SubmitBomb(Combination? bomb)
        {
            if (_bombTcs == null) return false;

            if (bomb != null && !IsMoveLegal(bomb, _pendingCtx.LegalMoves))
                return false;

            var tcs = _bombTcs;
            _bombTcs = null;
            PendingDecision.Value = null;
            tcs.TrySetResult(bomb);
            return true;
        }

        /// <summary>
        /// 용 양도 대상을 제출한다.
        /// ctx.LeftSeat 또는 ctx.RightSeat 여야 한다.
        /// </summary>
        public bool SubmitDragonRecipient(int seat)
        {
            if (_dragonTcs == null) return false;
            if (seat != _pendingCtx.LeftSeat && seat != _pendingCtx.RightSeat)
                return false;
            var tcs = _dragonTcs;
            _dragonTcs = null;
            PendingDecision.Value = null;
            tcs.TrySetResult(seat);
            return true;
        }

        // ── 취소 콜백 ────────────────────────────────────────────────────────

        private void CancelGrandTichu()
        {
            _grandTichuTcs?.TrySetCanceled();
            _grandTichuTcs = null;
            PendingDecision.Value = null;
        }

        private void CancelExchange()
        {
            _exchangeTcs?.TrySetCanceled();
            _exchangeTcs = null;
            PendingDecision.Value = null;
        }

        private void CancelTichu()
        {
            _tichuTcs?.TrySetCanceled();
            _tichuTcs = null;
            PendingDecision.Value = null;
        }

        private void CancelTurn()
        {
            _turnTcs?.TrySetCanceled();
            _turnTcs = null;
            PendingDecision.Value = null;
        }

        private void CancelBomb()
        {
            _bombTcs?.TrySetCanceled();
            _bombTcs = null;
            PendingDecision.Value = null;
        }

        private void CancelDragon()
        {
            _dragonTcs?.TrySetCanceled();
            _dragonTcs = null;
            PendingDecision.Value = null;
        }

        // ── 합법성 검사 헬퍼 ─────────────────────────────────────────────────

        /// <summary>
        /// move 의 카드 집합이 legalMoves 중 하나와 멀티셋 동일한지 확인한다.
        /// </summary>
        private static bool IsMoveLegal(Combination move, IReadOnlyList<Combination> legalMoves)
        {
            for (int i = 0; i < legalMoves.Count; i++)
            {
                if (CardsMatch(move.Cards, legalMoves[i].Cards))
                    return true;
            }
            return false;
        }

        /// <summary>두 카드 리스트가 멀티셋으로 동일한지 확인한다. 순서 무관.</summary>
        private static bool CardsMatch(IReadOnlyList<Card> a, IReadOnlyList<Card> b)
        {
            if (a.Count != b.Count) return false;
            // 작은 리스트이므로 O(n²) 선형 탐색으로 충분하다.
            var remaining = new List<Card>(b.Count);
            for (int i = 0; i < b.Count; i++) remaining.Add(b[i]);
            for (int i = 0; i < a.Count; i++)
            {
                if (!remaining.Remove(a[i])) return false;
            }
            return true;
        }

        /// <summary>교환 카드 3장이 손패 안에 있고 서로 다른지 확인한다.</summary>
        private static bool IsDistinctAndInHand(Card l, Card p, Card r, IReadOnlyList<Card> hand)
        {
            // 서로 다른지 확인
            if (l.Equals(p) || l.Equals(r) || p.Equals(r)) return false;
            // 모두 손패 안에 있는지 확인 (멀티셋: 동일 카드 중복 처리)
            var remaining = new List<Card>(hand.Count);
            for (int i = 0; i < hand.Count; i++) remaining.Add(hand[i]);
            return remaining.Remove(l) && remaining.Remove(p) && remaining.Remove(r);
        }
    }
}
