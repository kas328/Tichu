using Tichu.Core;
using Tichu.Core.Combinations;
using Tichu.Core.Game;

namespace Tichu.GameFlow.Agents
{
    /// <summary>
    /// 단일-세계 PIMC 탐색 에이전트(P2-A 골격).
    /// DecideTurn 만 탐색: 미관측 손패를 1개 세계로 결정화하고, 각 루트 합법수를 적용한 뒤
    /// 현 휴리스틱(AiAgent)으로 끝까지 롤아웃해 관측 팀 점수차가 최대인 수를 고른다.
    /// 동점깨기는 MoveOrder.Strength 최소(사람다운 보존 편향). 나머지 결정은 휴리스틱 위임.
    /// 무작위는 결정화 셔플에만 쓰이며, 고정 노드수(worlds=1)에서 결정적이다.
    /// </summary>
    public sealed class PimcAgent : IAgent
    {
        private readonly ulong _roundSeed;
        private readonly AiAgent _heuristic;   // 비-턴 결정 위임 + (개념상) 디폴트 정책과 동일 로직.
        private Rng _rng;

        public PimcAgent(ulong roundSeed, int seat)
        {
            _roundSeed = roundSeed;
            _heuristic = new AiAgent(roundSeed, seat);
            _rng = new Rng(roundSeed ^ 0x91C0_0000_0000_0001UL ^ (ulong)seat);
        }

        // ── 탐색하는 결정: 자기 턴 ─────────────────────────────────────────────────
        public TurnDecision DecideTurn(in DecisionContext ctx)
        {
            var legal = ctx.LegalMoves;
            if (legal.Count == 0)
                return TurnDecision.Pass;   // 방어(드라이버상 도달 안 함).

            // 미관측 손패를 1개 세계로 결정화(Rng 로컬 전진 후 되쓰기).
            var rng = _rng;
            var world = Determinizer.Sample(ctx.State, ctx.Seat, ref rng);
            _rng = rng;

            // 롤아웃 정책 시드: 라운드/좌석 파생(결정적, 게임 셔플과 비상관).
            ulong policySeed = _roundSeed ^ 0x5043_0000_0000_0001UL ^ (ulong)ctx.Seat;

            Combination? best = null;
            int bestStrength = int.MaxValue;
            long bestEv = long.MinValue;

            // 각 합법수: 적용 후 롤아웃 보상.
            for (int i = 0; i < legal.Count; i++)
            {
                var move = legal[i];
                var sim = world.Clone();
                var applied = GameEngine.Apply(sim, GameAction.Play(ctx.Seat, move.Cards)); // P2-A: wish=null
                if (!applied.Ok) continue;

                int ev = Pimc.Rollout(sim, ctx.Seat, policySeed);
                int strength = MoveOrder.Strength(move);
                if (ev > bestEv || (ev == bestEv && strength < bestStrength))
                {
                    bestEv = ev;
                    bestStrength = strength;
                    best = move;
                }
            }

            // 패스가 합법이면 후보에 포함(EV 비교). 동점이면 수를 선호(패스는 strict 우위일 때만).
            if (ctx.CanPass)
            {
                var sim = world.Clone();
                var applied = GameEngine.Apply(sim, GameAction.Pass(ctx.Seat));
                if (applied.Ok)
                {
                    int ev = Pimc.Rollout(sim, ctx.Seat, policySeed);
                    if (ev > bestEv)
                        return TurnDecision.Pass;
                }
            }

            return best == null ? TurnDecision.Pass : TurnDecision.Play(best);
        }

        // ── 위임하는 결정(P2-A): 휴리스틱 그대로 ────────────────────────────────────
        public bool CallGrandTichu(in DecisionContext ctx) => _heuristic.CallGrandTichu(ctx);
        public ExchangeChoice ChooseExchange(in DecisionContext ctx) => _heuristic.ChooseExchange(ctx);
        public bool CallTichu(in DecisionContext ctx) => _heuristic.CallTichu(ctx);
        public Combination? DecideBomb(in DecisionContext ctx) => _heuristic.DecideBomb(ctx);
        public int ChooseDragonRecipient(in DecisionContext ctx) => _heuristic.ChooseDragonRecipient(ctx);
    }
}
