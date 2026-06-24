using Tichu.Core;
using Tichu.Core.Combinations;
using Tichu.Core.Game;

namespace Tichu.GameFlow.Agents
{
    /// <summary>
    /// 다세계 PIMC 탐색 에이전트(P2-B1). PolicyConfig 주입으로 티어를 가른다.
    /// DecideTurn: Worlds&gt;0 이면 config.Worlds 세계를 결정화하고 각 루트 합법수를
    /// config.RolloutsPerWorld 회(ε-노이즈) 롤아웃해 세계·롤아웃 합 EV(관측 팀 부호)가 최대인
    /// 수를 고른다(동점깨기 MoveOrder.Strength 최소). Worlds==0(Easy)이면 탐색 없이 ε-휴리스틱
    /// 직접 결정. 나머지 결정은 휴리스틱 위임. 고정 노드수에서 결정적(결정화 셔플·ε만 Rng).
    /// </summary>
    public sealed class PimcAgent : IAgent
    {
        private readonly ulong _roundSeed;
        private readonly PolicyConfig _config;
        private readonly AiAgent _heuristic;                 // 비-턴 결정 위임.
        private readonly HeuristicRolloutPolicy _easyDirect; // Worlds==0 직접 결정(ε-휴리스틱).
        private Rng _rng;                                    // 결정화 세계 샘플링.

        public PimcAgent(ulong roundSeed, int seat, PolicyConfig config)
        {
            _roundSeed = roundSeed;
            _config = config;
            _heuristic = new AiAgent(roundSeed, seat);
            _easyDirect = new HeuristicRolloutPolicy(roundSeed, seat, config.Epsilon);
            _rng = new Rng(roundSeed ^ 0x91C0_0000_0000_0001UL ^ (ulong)seat);
        }

        public TurnDecision DecideTurn(in DecisionContext ctx)
        {
            var legal = ctx.LegalMoves;
            if (legal.Count == 0)
                return TurnDecision.Pass;

            // Easy: 탐색 OFF → ε-휴리스틱 직접 결정.
            if (_config.Worlds <= 0)
                return _easyDirect.DecideTurn(ctx);

            ulong policyBase = _roundSeed ^ 0x5043_0000_0000_0001UL ^ (ulong)ctx.Seat;
            int rolloutsPerWorld = _config.RolloutsPerWorld < 1 ? 1 : _config.RolloutsPerWorld;

            // 각 합법수의 누적 EV(세계×롤아웃 합). 후보 집합은 관측 손패만 의존 → 세계 무관 동일.
            var sumEv = new long[legal.Count];
            var rng = _rng;

            for (int w = 0; w < _config.Worlds; w++)
            {
                var world = Determinizer.Sample(ctx.State, ctx.Seat, ref rng);
                for (int r = 0; r < rolloutsPerWorld; r++)
                {
                    // 공통 난수(variance reduction): 같은 (w,r)에서 모든 수가 같은 ε-시퀀스를 본다.
                    ulong rolloutSeed = policyBase + (ulong)(w * rolloutsPerWorld + r);
                    for (int i = 0; i < legal.Count; i++)
                    {
                        var sim = world.Clone();
                        if (!GameEngine.Apply(sim, GameAction.Play(ctx.Seat, legal[i].Cards)).Ok) continue; // wish=null(P2-B1)
                        sumEv[i] += Pimc.Rollout(sim, ctx.Seat, rolloutSeed, _config.Epsilon);
                    }
                }
            }

            // 세계·롤아웃 합 EV 최대 수(동점깨기 MoveOrder.Strength 최소).
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
            // 동점이면 수를 선호(strict >). rng 되쓰기는 모든 Determinizer.Sample 이후 한 번만.
            if (ctx.CanPass)
            {
                var passWorld = Determinizer.Sample(ctx.State, ctx.Seat, ref rng);
                var passSim = passWorld.Clone();
                if (GameEngine.Apply(passSim, GameAction.Pass(ctx.Seat)).Ok)
                {
                    long passEv = (long)Pimc.Rollout(passSim, ctx.Seat, policyBase, _config.Epsilon)
                                  * _config.Worlds * rolloutsPerWorld;
                    if (passEv > bestSum) { _rng = rng; return TurnDecision.Pass; }
                }
            }

            _rng = rng;
            return best == null ? TurnDecision.Pass : TurnDecision.Play(best);
        }

        public bool CallGrandTichu(in DecisionContext ctx) => _heuristic.CallGrandTichu(ctx);
        public ExchangeChoice ChooseExchange(in DecisionContext ctx) => _heuristic.ChooseExchange(ctx);
        public bool CallTichu(in DecisionContext ctx) => _heuristic.CallTichu(ctx);
        public Combination? DecideBomb(in DecisionContext ctx) => _heuristic.DecideBomb(ctx);
        public int ChooseDragonRecipient(in DecisionContext ctx) => _heuristic.ChooseDragonRecipient(ctx);
    }
}
