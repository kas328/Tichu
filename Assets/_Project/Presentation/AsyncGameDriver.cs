using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
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

        public AsyncGameDriver(IDecisionAgent[] agents)
        {
            if (agents == null || agents.Length != 4) throw new System.ArgumentException("exactly 4 agents required", nameof(agents));
            _agents = agents;
        }

        /// <summary>주어진 상태에서 한 라운드를 Scoring 까지 구동한 뒤 정산해 결과를 반환한다.</summary>
        public async UniTask<RoundOutcome> RunRoundAsync(GameState s, CancellationToken ct,
            System.Action<GameState, GameAction> onApply = null)
        {
            _onApply = onApply;
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
            var d = await _agents[activeTurn].DecideTurnAsync(Ctx(s, activeTurn), ct);
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
