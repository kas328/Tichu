using System.Threading;
using Tichu.Core;
using Tichu.Core.Combinations;
using Tichu.Core.Game;

namespace Tichu.GameFlow.Agents
{
    /// <summary>
    /// 다세계 PIMC 탐색 에이전트(P2-B). PolicyConfig 주입으로 티어를 가른다.
    /// DecideTurn: Worlds&gt;0 이면 config.Worlds 세계를 결정화하고 각 루트 합법수를
    /// config.RolloutsPerWorld 회(ε-노이즈) 롤аут해 세계·롤аут 합 EV(관측 팀 부호)가 최대인
    /// 수를 고른다(동점깨기 MoveOrder.Strength 최소). Worlds==0(Easy)이면 탐색 없이 ε-휴리스틱
    /// 직접 결정. 나머지 결정은 휴리스틱 위임. 고정 노드수(토큰 None)에서 결정적.
    /// 좌석은 생성자 _seat 단일 출처를 쓴다(드라이버는 항상 자기 좌석으로 호출).
    /// </summary>
    public sealed class PimcAgent : IAgent
    {
        private readonly ulong _roundSeed;
        private readonly int _seat;
        private readonly PolicyConfig _config;
        private readonly HeuristicRolloutPolicy _policy; // 비-턴 결정 위임 + Easy 직접결정(ε-휴리스틱).
        private Rng _rng;                                 // 결정화 세계 샘플링.

        public PimcAgent(ulong roundSeed, int seat, PolicyConfig config)
        {
            _roundSeed = roundSeed;
            _seat = seat;
            _config = config;
            _policy = new HeuristicRolloutPolicy(roundSeed, seat, config.Epsilon);
            _rng = new Rng(roundSeed ^ 0x91C0_0000_0000_0001UL ^ (ulong)seat);
        }

        /// <summary>고정 노드수(결정적) 탐색. = DecideTurnAnytime(ctx, None, None).</summary>
        public TurnDecision DecideTurn(in DecisionContext ctx)
            => DecideTurnAnytime(ctx, CancellationToken.None, CancellationToken.None);

        /// <summary>
        /// anytime 탐색. budget 만료 시(최소 1샘플 완료 후) 현재까지 best-so-far 반환(throw 안 함);
        /// abort 취소 시 OperationCanceledException(폭탄 인터럽트 — 드라이버가 이 턴 폐기).
        /// 토큰 둘 다 None 이면 고정 노드수 결정적 탐색(= DecideTurn).
        /// </summary>
        public TurnDecision DecideTurnAnytime(in DecisionContext ctx, CancellationToken budget, CancellationToken abort)
        {
            var legal = ctx.LegalMoves;
            if (legal.Count == 0)
                return TurnDecision.Pass;

            // Easy: 탐색 OFF → ε-휴리스틱 직접 결정.
            if (_config.Worlds <= 0)
                return _policy.DecideTurn(ctx);

            ulong policyBase = _roundSeed ^ 0x5043_0000_0000_0001UL ^ (ulong)_seat;
            int rolloutsPerWorld = _config.RolloutsPerWorld < 1 ? 1 : _config.RolloutsPerWorld;

            // 각 합법수의 누적 EV(세계×롤аут 합). 후보 집합은 관측 손패만 의존 → 세계 무관 동일.
            var sumEv = new long[legal.Count];
            var rng = _rng;
            int samples = 0;
            bool budgetHit = false;

            for (int w = 0; w < _config.Worlds && !budgetHit; w++)
            {
                var world = Determinizer.Sample(ctx.State, _seat, ref rng);
                for (int r = 0; r < rolloutsPerWorld; r++)
                {
                    abort.ThrowIfCancellationRequested();                                  // 폭탄 인터럽트 → 폐기
                    if (samples >= 1 && budget.IsCancellationRequested) { budgetHit = true; break; } // anytime
                    // 공통 난수(variance reduction): 같은 (w,r)에서 모든 수가 같은 ε-시퀀스를 본다.
                    ulong rolloutSeed = policyBase + (ulong)(w * rolloutsPerWorld + r);
                    for (int i = 0; i < legal.Count; i++)
                    {
                        var sim = world.Clone();
                        if (!GameEngine.Apply(sim, GameAction.Play(_seat, legal[i].Cards)).Ok) continue; // wish=null(P2-B)
                        sumEv[i] += Pimc.Rollout(sim, _seat, rolloutSeed, _config.Epsilon);
                    }
                    samples++;
                }
            }

            // 세계·롤аут 합 EV 최대 수(동점깨기 MoveOrder.Strength 최소).
            long bestSum = long.MinValue;
            int bestStrength = int.MaxValue;
            Combination? best = null;
            for (int i = 0; i < legal.Count; i++)
            {
                int strength = MoveOrder.Strength(legal[i]);
                if (sumEv[i] > bestSum || (sumEv[i] == bestSum && strength < bestStrength))
                {
                    bestSum = sumEv[i];
                    bestStrength = strength;
                    best = legal[i];
                }
            }

            // 패스(합법일 때)는 1세계로 가볍게 평가해 같은 스케일(×Worlds×rollouts)로 환산 비교.
            // 예산 만료(budgetHit) 시엔 즉시 반환으로 anytime 존중. 동점이면 수를 선호(strict >).
            if (ctx.CanPass && !budgetHit)
            {
                var passWorld = Determinizer.Sample(ctx.State, _seat, ref rng);
                var passSim = passWorld.Clone();
                if (GameEngine.Apply(passSim, GameAction.Pass(_seat)).Ok)
                {
                    long passEv = (long)Pimc.Rollout(passSim, _seat, policyBase, _config.Epsilon)
                                  * _config.Worlds * rolloutsPerWorld;
                    if (passEv > bestSum) { _rng = rng; return TurnDecision.Pass; }
                }
            }

            _rng = rng;
            return best == null ? TurnDecision.Pass : TurnDecision.Play(best);
        }

        public bool CallGrandTichu(in DecisionContext ctx) => _policy.CallGrandTichu(ctx);
        public ExchangeChoice ChooseExchange(in DecisionContext ctx) => _policy.ChooseExchange(ctx);
        public bool CallTichu(in DecisionContext ctx) => _policy.CallTichu(ctx);
        public Combination? DecideBomb(in DecisionContext ctx) => _policy.DecideBomb(ctx);
        public int ChooseDragonRecipient(in DecisionContext ctx) => _policy.ChooseDragonRecipient(ctx);
    }
}
