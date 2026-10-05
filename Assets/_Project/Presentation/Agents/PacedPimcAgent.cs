using System;
using System.Diagnostics;
using System.Threading;
using Tichu.Core.Combinations;
using Tichu.GameFlow.Agents;

namespace Tichu.Presentation
{
    /// <summary>
    /// 동기 다세계 <see cref="PimcAgent"/> 를 세션 스레드용 <see cref="IAgent"/> 로 감싸는
    /// 페이싱 어댑터. <see cref="PimcDecisionAgent"/>(비동기 경로)의 동기 쌍둥이이며 지연
    /// 배치를 1:1 로 맞춘다 — 턴 결정과 **실제로 던지는** 폭탄만 지연하고, 셋업 결정은
    /// 즉시 반환한다(폭탄 창은 매 트릭 세 좌석을 폴링하므로 무조건 지연은 3배가 된다).
    ///
    /// 턴 결정(<see cref="DecideTurn"/>)에서는 겹침을 보존한다: per-move ≈ max(지연, 연산).
    /// 비동기 어댑터는 이를 스레드풀 핸드오프로 얻지만, 여기서는 세션이 자기 전용 스레드를
    /// 소유해 메인스레드를 막을 걱정이 없으므로 **연산을 먼저 하고 남은 시간만 잔다.**
    /// 추가 스레드가 없고, 소유 스레드가 직접 계산하므로 방어적 상태 복사도 필요 없다.
    /// 폭탄 경로(<see cref="DecideBomb"/>)는 겹치지 않는다 — Stopwatch 가 휴리스틱 호출
    /// **뒤**에 시작돼 delay + compute 다. 비동기 원본 PimcDecisionAgent 와 지연 배치를
    /// 1:1 로 맞추려는 의도적 설계이지 결함이 아니다 — 고치지 않는다.
    ///
    /// PIMC 가 아직 core 에 없어(유니티 쪽 GameFlow 전용) 이 파일은 미러되지 않는다.
    /// 서버가 빈자리 AI 를 페이싱하는 일은 PIMC 의 core 승격(M5) 뒤의 몫이다.
    /// </summary>
    public sealed class PacedPimcAgent : IAgent
    {
        private readonly PimcAgent _pimc;
        private readonly PolicyConfig _config;
        private readonly int _budgetMs;
        private readonly int _delayMs;
        private readonly Func<bool> _fastForward;

        public PacedPimcAgent(ulong roundSeed, int seat, PolicyConfig config,
            int budgetMs, int delayMs, Func<bool> fastForward = null)
        {
            _pimc = new PimcAgent(roundSeed, seat, config);
            _config = config;
            _budgetMs = budgetMs;
            _delayMs = delayMs;
            _fastForward = fastForward;
        }

        public bool CallGrandTichu(in DecisionContext ctx) => _pimc.CallGrandTichu(ctx);

        public ExchangeChoice ChooseExchange(in DecisionContext ctx) => _pimc.ChooseExchange(ctx);

        public bool CallTichu(in DecisionContext ctx) => _pimc.CallTichu(ctx);

        public int ChooseDragonRecipient(in DecisionContext ctx) => _pimc.ChooseDragonRecipient(ctx);

        public TurnDecision DecideTurn(in DecisionContext ctx)
        {
            var sw = Stopwatch.StartNew();

            TurnDecision d;
            if (_config.Worlds <= 0)
            {
                d = _pimc.DecideTurn(ctx);          // 탐색 OFF(Easy) — 예산이 의미 없다.
            }
            else
            {
                using (var budget = new CancellationTokenSource(_budgetMs))
                    d = _pimc.DecideTurnAnytime(ctx, budget.Token, CancellationToken.None);
            }

            SleepRemaining(sw);                      // 연산과 겹친 만큼 깎아서 잔다.
            return d;
        }

        public Combination? DecideBomb(in DecisionContext ctx)
        {
            var bomb = _pimc.DecideBomb(ctx);
            if (bomb != null)
            {
                var sw = Stopwatch.StartNew();
                SleepRemaining(sw);                  // 실제로 던질 때만 — 거절은 즉시.
            }
            return bomb;
        }

        /// <summary>관전 지연에서 이미 흐른 연산 시간을 빼고 남은 만큼만 잔다.</summary>
        private void SleepRemaining(Stopwatch sw)
        {
            if (_fastForward != null && _fastForward()) return;
            int rest = _delayMs - (int)sw.ElapsedMilliseconds;
            if (rest > 0) Thread.Sleep(rest);
        }
    }
}
