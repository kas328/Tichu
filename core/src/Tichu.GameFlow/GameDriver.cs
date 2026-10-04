using System.Collections.Generic;
using Tichu.Core.Game;
using Tichu.GameFlow.Agents;

namespace Tichu.GameFlow
{
    /// <summary>한 라운드 구동 결과: 최종 상태, 정산 결과, 적용된 액션 로그.</summary>
    public sealed class RoundOutcome
    {
        /// <summary>라운드 종료 시점의 게임 상태(Phase=RoundEnd).</summary>
        public GameState State { get; }

        /// <summary>ScoreRound 결과.</summary>
        public RoundResult Result { get; }

        /// <summary>적용된 모든 액션(리플레이 가능한 결정적 로그).</summary>
        public IReadOnlyList<GameAction> Log { get; }

        public RoundOutcome(GameState state, RoundResult result, IReadOnlyList<GameAction> log)
        {
            State = state;
            Result = result;
            Log = log;
        }
    }

    /// <summary>
    /// 한 라운드를 끝까지 구동하는 오케스트레이터.
    /// 각 결정 지점에서 좌석별 에이전트에게 묻고, 모든 액션은 throw 하는 Apply 헬퍼를 통과한다.
    /// 드라이버 자체는 무작위성을 도입하지 않으며 고정 반복 순서를 사용한다 →
    /// "throw 없이 완주" == 불법 상태 미발생 증명, 로그 리플레이 == 결정성 증명.
    /// </summary>
    public sealed class GameDriver
    {
        private const int FullHand = 14;
        private const int MaxSteps = 100_000;

        private readonly IAgent[] _agents; // 좌석 0..3 인덱싱.
        private System.Action<GameState, GameAction>? _onApply;          // 매 Apply 후 호출(세션 브로드캐스트용; null=기존 동작)
        private System.Func<GameState, int>? _takePendingTichu;          // 예약된 작은 티츄 좌석(-1=없음)
        private System.Func<GameState, GameAction?>? _takePendingBomb;   // 예약된 차례밖 폭탄(null=없음)

        public GameDriver(IAgent[] agents)
        {
            if (agents == null || agents.Length != 4) throw new System.ArgumentException("exactly 4 agents required", nameof(agents));
            _agents = agents;
        }

        /// <summary>주어진 상태에서 한 라운드를 Scoring 까지 구동한 뒤 정산해 결과를 반환한다.</summary>
        /// <param name="onApply">매 액션 적용 직후 호출. 권위 세션이 좌석별 뷰를 방송하는 지점.</param>
        /// <param name="takePendingTichu">예약된 작은 티츄 좌석을 꺼낸다(-1=없음).</param>
        /// <param name="takePendingBomb">예약된 차례밖 폭탄을 꺼낸다(null=없음). 엔진이 거부하면 조용히 버린다.</param>
        public RoundOutcome RunRound(
            GameState s,
            System.Action<GameState, GameAction>? onApply = null,
            System.Func<GameState, int>? takePendingTichu = null,
            System.Func<GameState, GameAction?>? takePendingBomb = null)
        {
            _onApply = onApply;
            _takePendingTichu = takePendingTichu;
            _takePendingBomb = takePendingBomb;

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
                        bool call = _agents[seat].CallGrandTichu(Ctx(s, seat));
                        Apply(s, call ? GameAction.CallGrandTichu(seat) : GameAction.DeclineGrandTichu(seat), log);
                        break;
                    }

                    case RoundPhase.Exchange:
                    {
                        int seat = exchangeNext++;
                        var ex = _agents[seat].ChooseExchange(Ctx(s, seat));
                        Apply(s, GameAction.Exchange(seat,
                            new[] { ex.ToLeft }, new[] { ex.ToPartner }, new[] { ex.ToRight }), log);
                        break;
                    }

                    case RoundPhase.Play:
                        DrivePlay(s, log);
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
        /// 0) 작은 티츄 예약 → 0b) 차례밖 폭탄 예약 → 1) 용 양도 → 2) 폭탄 창 → 3) 작은 티츄 훅(폴스루) → 4) 인-턴 행동.
        /// 각 분기는 처리 후 return 하여 바깥 루프가 상태를 재평가하게 한다.
        /// 단, 작은 티츄 훅은 턴을 소모하지 않으므로 return 하지 않고 4로 폴스루한다.
        /// </summary>
        private void DrivePlay(GameState s, List<GameAction> log)
        {
            // 0) 작은 티츄 인터럽트: 예약된 콜을 먼저 적용(차례 무관, 첫 패 전이면 합법).
            //    원격 클라이언트가 먹이는 입력이라 0b 와 동일하게 절대 throw 하지 않는다 —
            //    Play 와 CallTichu 를 연달아 보내면 손패가 13장이 된 뒤 예약이 소비될 수 있고,
            //    그때 엔진이 거부한다. 불법 입력은 상태를 바꾸지 않고 조용히 버린다(기획서 7.7).
            //    AsyncGameDriver 는 예약이 로컬 UI 버튼에서 와 합법성이 미리 게이팅되므로 throw 한다.
            if (_takePendingTichu != null)
            {
                int ts = _takePendingTichu(s);
                if (ts >= 0)
                {
                    var tichuAction = GameAction.CallTichu(ts);
                    var tichuRes = GameEngine.Apply(s, tichuAction);
                    if (tichuRes.Ok) { log.Add(tichuAction); _onApply?.Invoke(s, tichuAction); }
                    return;
                }
            }

            // 0b) 차례밖 폭탄 인터럽트: 예약된 폭탄을 적용 시도. 합법성은 엔진이 판정하며,
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
                int r = _agents[w].ChooseDragonRecipient(Ctx(s, w));
                Apply(s, GameAction.GiveDragon(w, r), log);
                return;
            }

            // 2) 폭탄 창(시계 순). 첫 non-null 폭탄이 떨어지면 return → 루프 재시작 → 새 Top 에 대해 창이 다시 열린다.
            foreach (int seat in FlowQuery.SeatsWithLegalBomb(s))
            {
                var bomb = _agents[seat].DecideBomb(Ctx(s, seat));
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
                if (_agents[activeTurn].CallTichu(Ctx(s, activeTurn)))
                    Apply(s, GameAction.CallTichu(activeTurn), log);
            }

            // 4) 인-턴 행동: 패스 또는 패 내기(마작 소원 포함).
            var d = _agents[activeTurn].DecideTurn(Ctx(s, activeTurn));
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
            _onApply?.Invoke(s, a); // 세션: 매 적용마다 좌석별 뷰 방송. 기존 호출부는 null 이라 무영향.
        }

        private static DecisionContext Ctx(GameState s, int seat) => new DecisionContext(s, seat);
    }
}
