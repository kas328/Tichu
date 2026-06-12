using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;

namespace Tichu.Presentation
{
    /// <summary>
    /// <see cref="GameDriver"/>의 비동기 미러. 한 라운드를 끝까지 구동하는 오케스트레이터.
    /// 각 결정 지점에서 좌석별 <see cref="IDecisionAgent"/>에게 await 로 묻고,
    /// 모든 액션은 throw 하는 Apply 헬퍼를 통과한다.
    /// 드라이버 자체는 무작위성을 도입하지 않으며 동기 GameDriver 와 동일한 고정 반복 순서를 사용한다 →
    /// 동일 시드에 대해 동기 드라이버와 비트 동일한 결과를 낸다(오라클 교차검증).
    /// 결과 타입 <see cref="RoundOutcome"/>은 Tichu.GameFlow 의 것을 그대로 재사용한다.
    /// </summary>
    public sealed class AsyncGameDriver
    {
        private const int FullHand = 14;
        private const int MaxSteps = 100_000;

        private readonly IDecisionAgent[] _agents; // 좌석 0..3 인덱싱.
        private System.Action<GameState, GameAction> _onApply; // 매 Apply 후 호출(앱 뷰 갱신용; null=테스트/오라클).
        private System.Func<GameState, int> _takePendingTichu;  // 인간 작은 티츄 인터럽트(-1=없음). 앱 전용; null=테스트/오라클.
        private System.Func<GameState, GameAction> _takePendingBomb; // 인간 차례밖 폭탄 인터럽트(null=없음). 앱 전용.
        private Observable<R3.Unit> _bombInterrupt;                  // 폭탄 예약 즉시 신호 — 진행 중 상대 결정 취소용. null=테스트.

        public AsyncGameDriver(IDecisionAgent[] agents)
        {
            if (agents == null || agents.Length != 4) throw new System.ArgumentException("exactly 4 agents required", nameof(agents));
            _agents = agents;
        }

        /// <summary>주어진 상태에서 한 라운드를 Scoring 까지 구동한 뒤 정산해 결과를 반환한다.</summary>
        public async UniTask<RoundOutcome> RunRoundAsync(GameState s, CancellationToken ct,
            System.Action<GameState, GameAction> onApply = null,
            System.Func<GameState, int> takePendingTichu = null,
            System.Func<GameState, GameAction> takePendingBomb = null,
            Observable<R3.Unit> bombInterrupt = null)
        {
            _onApply = onApply;
            _takePendingTichu = takePendingTichu;
            _takePendingBomb = takePendingBomb;
            _bombInterrupt = bombInterrupt;
            var log = new List<GameAction>();
            int grandNext = 0, exchangeNext = 0;
            int steps = 0;

            while (s.Phase != RoundPhase.Scoring)
            {
                if (++steps > MaxSteps)
                    throw new System.InvalidOperationException($"round stuck: Phase={s.Phase} Turn={s.Turn}");

                switch (s.Phase)
                {
                    case RoundPhase.GrandTichuDecision:
                    {
                        int seat = grandNext++;
                        bool call = await _agents[seat].CallGrandTichuAsync(Ctx(s, seat), ct);
                        Apply(s, call ? GameAction.CallGrandTichu(seat) : GameAction.DeclineGrandTichu(seat), log);
                        break;
                    }

                    case RoundPhase.Exchange:
                    {
                        int seat = exchangeNext++;
                        var ex = await _agents[seat].ChooseExchangeAsync(Ctx(s, seat), ct);
                        Apply(s, GameAction.Exchange(seat,
                            new[] { ex.ToLeft }, new[] { ex.ToPartner }, new[] { ex.ToRight }), log);
                        break;
                    }

                    case RoundPhase.Play:
                        await DrivePlayAsync(s, log, ct);
                        break;

                    default:
                        // Scoring 이전에는 도달하지 않아야 한다.
                        break;
                }
            }

            var result = ScoreCalculator.ScoreRound(s);
            return new RoundOutcome(s, result, log);
        }

        /// <summary>
        /// Play 페이즈의 한 번의 반복. 고정 순서:
        /// 1) 용 양도 → 2) 폭탄 창 → 3) 작은 티츄 훅(폴스루) → 4) 인-턴 행동.
        /// 각 분기는 처리 후 return 하여 바깥 루프가 상태를 재평가하게 한다.
        /// 단, 작은 티츄 훅은 턴을 소모하지 않으므로 return 하지 않고 4로 폴스루한다.
        /// </summary>
        private async UniTask DrivePlayAsync(GameState s, List<GameAction> log, CancellationToken ct)
        {
            // 0) 인간 작은 티츄 인터럽트: 버튼으로 예약된 콜을 먼저 적용(차례 무관, 첫 패 전이면 합법).
            if (_takePendingTichu != null)
            {
                int ts = _takePendingTichu(s);
                if (ts >= 0) { Apply(s, GameAction.CallTichu(ts), log); return; }
            }

            // 0b) 인간 차례밖 폭탄 인터럽트: 예약된 폭탄을 적용 시도. 합법성은 엔진이 판정하며,
            //     거부(현재 Top 미격파 등)되면 throw 하지 않고 조용히 버린다(인간 입력이므로).
            if (_takePendingBomb != null)
            {
                var bombAction = _takePendingBomb(s);
                if (bombAction != null)
                {
                    var res = GameEngine.Apply(s, bombAction);
                    if (res.Ok) { log.Add(bombAction); _onApply?.Invoke(s, bombAction); return; }
                }
            }

            // 1) 용 양도 먼저.
            if (FlowQuery.PendingDragonGift(s, out int w))
            {
                int r = await _agents[w].ChooseDragonRecipientAsync(Ctx(s, w), ct);
                Apply(s, GameAction.GiveDragon(w, r), log);
                return;
            }

            // 2) 폭탄 창(시계 순). 첫 non-null 폭탄이 떨어지면 return → 루프 재시작 → 새 Top 에 대해 창이 다시 열린다.
            foreach (int seat in FlowQuery.SeatsWithLegalBomb(s))
            {
                var bomb = await _agents[seat].DecideBombAsync(Ctx(s, seat), ct);
                if (bomb != null)
                {
                    Apply(s, GameAction.Play(seat, bomb.Cards), log);
                    return;
                }
            }

            // 3) 작은 티츄 훅: 첫 패 내기 전(손패 14장)에만, 콜이 없을 때만 한 번 발동.
            //    콜은 턴을 소모하지 않으므로 return 하지 않고 4로 폴스루한다.
            int activeTurn = s.Turn;
            if (s.Seats[activeTurn].Call == TichuCall.None && s.Seats[activeTurn].Hand.Count == FullHand)
            {
                if (await _agents[activeTurn].CallTichuAsync(Ctx(s, activeTurn), ct))
                    Apply(s, GameAction.CallTichu(activeTurn), log);
            }

            // 4) 인-턴 행동: 패스 또는 패 내기(마작 소원 포함).
            //    진행 중 인간 폭탄 예약 신호가 오면 이 결정을 폐기하고 즉시 return → 다음 반복 0b)에서
            //    폭탄을 선점 적용한다(엔진이 폭탄 낸 좌석 다음부터 차례를 돌려 폭탄 응수 기회를 준다).
            TurnDecision d;
            if (_bombInterrupt != null)
            {
                using var turnCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                using var sub = _bombInterrupt.Subscribe(_ => turnCts.Cancel());
                try { d = await _agents[activeTurn].DecideTurnAsync(Ctx(s, activeTurn), turnCts.Token); }
                catch (System.OperationCanceledException) when (turnCts.IsCancellationRequested && !ct.IsCancellationRequested)
                { return; } // 폭탄 인터럽트 — 이 턴 결정을 버리고 루프 재시작
            }
            else
            {
                d = await _agents[activeTurn].DecideTurnAsync(Ctx(s, activeTurn), ct);
            }
            if (d.IsPass)
                Apply(s, GameAction.Pass(activeTurn), log);
            else
                Apply(s, GameAction.Play(activeTurn, d.Move!.Cards, d.Wish), log);
        }

        /// <summary>액션을 적용한다. 거부되면 throw(불법 상태 미발생 증명), 성공하면 로그에 기록.</summary>
        private void Apply(GameState s, GameAction a, List<GameAction> log)
        {
            var res = GameEngine.Apply(s, a);
            if (!res.Ok)
                throw new System.InvalidOperationException($"illegal action {a.Kind} seat {a.Seat}: {res.RejectReason}");
            log.Add(a);
            _onApply?.Invoke(s, a); // 앱: 매 플레이마다 뷰 갱신/로그. 테스트/오라클은 onApply=null.
        }

        private static DecisionContext Ctx(GameState s, int seat) => new DecisionContext(s, seat);
    }
}
