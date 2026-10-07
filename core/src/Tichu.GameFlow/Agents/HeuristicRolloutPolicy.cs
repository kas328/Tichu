using Tichu.Core;
using Tichu.Core.Combinations;

namespace Tichu.GameFlow.Agents
{
    /// <summary>
    /// 롤아웃 디폴트 정책: ε 확률로 무작위 합법수(블런더), 아니면 현 휴리스틱(AiAgent).
    /// 비-턴 결정(그랜드/교환/티츄/폭탄/용양도)은 AiAgent 에 그대로 위임한다.
    /// ε=0 이면 RNG 를 전혀 건드리지 않아 AiAgent 와 비트동일하다.
    /// Easy 티어의 직접 결정과 PIMC 롤아웃의 디폴트 정책, 두 곳에서 재사용된다.
    /// </summary>
    public sealed class HeuristicRolloutPolicy : IAgent
    {
        private readonly AiAgent _heuristic;
        private readonly double _epsilon;
        private Rng _rng;   // 비-readonly: 직접 메서드 호출로 전진(AiAgent/RandomAgent 패턴).

        public HeuristicRolloutPolicy(ulong seed, int seat, double epsilon, bool useGrandCallNet = false, double grandThreshold = GrandTichuWeights.Threshold, bool useSmallTichuNet = false, double smallThreshold = SmallTichuWeights.Threshold)
        {
            _heuristic = new AiAgent(seed, seat, useGrandCallNet, grandThreshold, useSmallTichuNet, smallThreshold);
            _epsilon = epsilon;
            _rng = new Rng(seed ^ 0xB0E1_0000_0000_0001UL ^ (ulong)seat);
        }

        public TurnDecision DecideTurn(in DecisionContext ctx)
        {
            if (_epsilon > 0.0)
            {
                // [0,1) 난수: NextULong 상위 53비트로 double. ε 미만이면 무작위 합법수.
                double u = (_rng.NextULong() >> 11) * (1.0 / 9007199254740992.0); // 2^53
                if (u < _epsilon)
                {
                    var moves = ctx.LegalMoves;
                    bool canPass = ctx.CanPass;
                    int n = moves.Count + (canPass ? 1 : 0);
                    if (n > 0)
                    {
                        int pick = _rng.NextInt(n);
                        return pick < moves.Count ? TurnDecision.Play(moves[pick]) : TurnDecision.Pass;
                    }
                }
            }
            return _heuristic.DecideTurn(ctx);
        }

        public bool CallGrandTichu(in DecisionContext ctx) => _heuristic.CallGrandTichu(ctx);
        public ExchangeChoice ChooseExchange(in DecisionContext ctx) => _heuristic.ChooseExchange(ctx);
        public bool CallTichu(in DecisionContext ctx) => _heuristic.CallTichu(ctx);
        public Combination? DecideBomb(in DecisionContext ctx) => _heuristic.DecideBomb(ctx);
        public int ChooseDragonRecipient(in DecisionContext ctx) => _heuristic.ChooseDragonRecipient(ctx);
    }
}
