using System.Collections.Generic;
using System.Threading;
using Tichu.Core;
using Tichu.Core.Cards;
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
        private (Card card, int seat)[] _passed;          // C1: 교환에서 넘긴 (카드,수령좌석). 결정화 핀용.

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

            // 파트너가 Top 인 팔로우: 휴리스틱과 동일한 파트너 규칙(기본 패스, 조건부 싼-밟기)을
            // 공유한다. EV 탐색이 파트너를 비싼 카드(A·용)로 무의미하게 밟는 낭비를 막는다.
            var trick = ctx.State.CurrentTrick;

            // #3 끝내기 셰딩(라이브 가드): 순수 EV 가 ≤5장 리드에서 콤보 셰딩(빠른 아웃)을 안 골라 싱글
            // 남발 → 휴리스틱 MostShedding 을 EV 전에 강제(리드만). OFF(기본)면 비트불변.
            if (_config.UseEndgameSheddingGuard && trick == null && ctx.MyHand.Count <= 5)
            {
                var nonBombLeads = new List<Combination>(legal.Count);
                for (int i = 0; i < legal.Count; i++)
                    if (!legal[i].IsBomb) nonBombLeads.Add(legal[i]);
                var shed = AiAgent.EndgameSheddingLead(nonBombLeads);
                if (shed != null) return TurnDecision.Play(shed, LiveWish(ctx, shed));
            }

            // ⑦ near-out 싱글 락아웃(라이브 가드): 낮은 싱글 Top + 상대 1장(아웃 임박)이면 Top 소유자 무관
            // 최고 싱글로 봉쇄한다(D1 은 상대-Top 만 → 파트너-Top 케이스 갭 보완). OFF(기본)면 비트불변.
            if (_config.UseNearOutLockout && trick != null)
            {
                var nbWins = new List<Combination>(legal.Count);
                for (int i = 0; i < legal.Count; i++)
                    if (!legal[i].IsBomb) nbWins.Add(legal[i]);
                var lockout = AiAgent.NearOutSingleLockout(ctx, trick, nbWins);
                if (lockout != null) return TurnDecision.Play(lockout);
            }

            if (trick != null && Seating.Partner(_seat) == trick.TopOwnerSeat)
            {
                var nonBomb = new System.Collections.Generic.List<Combination>(legal.Count);
                for (int i = 0; i < legal.Count; i++)
                    if (!legal[i].IsBomb) nonBomb.Add(legal[i]);
                var over = AiAgent.PartnerOvertakeMove(ctx, _seat, trick, nonBomb);
                if (over != null) return TurnDecision.Play(over);
                if (ctx.CanPass) return TurnDecision.Pass;
                // 패스 불가(소원 강제 등) → 아래 EV 탐색으로 폴백.
            }

            // 상대가 Top 인 팔로우 + 아웃/티츄 위협: 순수 EV 는 전략융합·롤아웃 천장 탓에 이 블록을
            // 라이브 수로 못 낸다(롤아웃에만 간접 반영) → 휴리스틱 블록 가드를 EV 전에 직접 건다
            // (파트너-밟기 가드의 쌍둥이). OFF(기본)면 비트불변.
            if (_config.UseOpponentThreatBlock && trick != null
                && Seating.TeamOf(trick.TopOwnerSeat) != Seating.TeamOf(_seat))
            {
                var nonBomb = new List<Combination>(legal.Count);
                for (int i = 0; i < legal.Count; i++)
                    if (!legal[i].IsBomb) nonBomb.Add(legal[i]);
                var block = AiAgent.OpponentThreatBlockMove(ctx, trick, nonBomb);
                if (block != null) return TurnDecision.Play(block);
            }

            // 상대 콤보(≥2장) 밟기 팀킬 가드(Bug4): 순수 EV 는 "상대 저지" 가치로 팀 아웃용 콤보를 헛되이
            // 밟아 팀 아웃을 막을 수 있다 → 티츄/나가기/리치가 아니면 EV 전에 패스로 콤보 보존. OFF면 비트불변.
            if (_config.UseComboOvertakeGuard && trick != null && ctx.CanPass
                && Seating.TeamOf(trick.TopOwnerSeat) != Seating.TeamOf(_seat))
            {
                var nonBomb = new List<Combination>(legal.Count);
                for (int i = 0; i < legal.Count; i++)
                    if (!legal[i].IsBomb) nonBomb.Add(legal[i]);
                if (AiAgent.WastefulComboOvertake(ctx, _seat, trick, nonBomb))
                    return TurnDecision.Pass;
            }

            // Issue A: 손패 크고 near-out 아닌데 낮은 콤보를 고콤보(A풀하우스+봉황 등)로만 이길 수 있으면 낭비 →
            // 밟지 말고 자원 보존(콜러 포함 — 봉황 조기 소진은 티츄 자해). OFF(기본)면 비트불변.
            if (_config.UseHighComboWasteGuard && trick != null && ctx.CanPass
                && Seating.TeamOf(trick.TopOwnerSeat) != Seating.TeamOf(_seat))
            {
                var nonBomb = new List<Combination>(legal.Count);
                for (int i = 0; i < legal.Count; i++)
                    if (!legal[i].IsBomb) nonBomb.Add(legal[i]);
                if (AiAgent.WastefulHighComboOvertake(ctx, _seat, trick, nonBomb))
                    return TurnDecision.Pass;
            }

            // 폭탄은 게이트된 DecideBomb(리치트릭 ≥15점)이 담당 → 인-턴 EV 후보에서 제외(싼 트릭 낭비 방지).
            var candidates = TurnCandidates(legal);

            // #2 봉황 보존: 낮은 싱글 팔로우에서 자연 승수가 있으면 봉황 단독을 EV 후보에서 제거(자연 우선).
            // 강제/과-패스 안 함(EV 가 자연·패스 중 선택) → −3/R 과-패스 회귀 회피. OFF(기본)면 비트불변.
            if (_config.UsePhoenixConservation)
                AiAgent.FilterWastefulPhoenixSingle(candidates, trick, ctx.MyHand.Count);

            ulong policyBase = _roundSeed ^ 0x5043_0000_0000_0001UL ^ (ulong)_seat;
            int rolloutsPerWorld = EffectiveRollouts(_config.Epsilon, _config.RolloutsPerWorld);

            // 각 후보의 가중 누적 EV(Σ weightᵥ·evᵥ). reach-prob off면 weight=1.0 → 균등(P2-C 불변).
            var weightedSum = new double[candidates.Count];
            double totalWeight = 0.0;
            var rng = _rng;
            int samples = 0;
            bool budgetHit = false;

            // B1 강건 백업: 후보별 "세계별 EV(그 세계 롤아웃 평균)"의 합·제곱합 → argmax(mean−λ·std).
            // OFF면 미할당(비트불변). reach-prob 는 전 티어 OFF라 가중 없이 세계별 EV 사용.
            bool robust = _config.UseRobustBackup;
            double[] robSum = robust ? new double[candidates.Count] : null;
            double[] robSumSq = robust ? new double[candidates.Count] : null;
            int worldsCounted = 0;

            var pins = _config.UseExchangePin ? _passed : null;   // C1: OFF면 null → 기존 균등 분배(비트불변).
            for (int w = 0; w < _config.Worlds && !budgetHit; w++)
            {
                var world = Determinizer.Sample(ctx.State, _seat, ref rng, pins, _config.UseTichuCallConstraint);
                double weight = _config.UseReachProb ? ReachWeight.WorldWeight(world, _seat) : 1.0;
                double[] worldSum = robust ? new double[candidates.Count] : null;
                int rolloutsThisWorld = 0;
                for (int r = 0; r < rolloutsPerWorld; r++)
                {
                    abort.ThrowIfCancellationRequested();                                  // 폭탄 인터럽트 → 폐기
                    if (samples >= 1 && budget.IsCancellationRequested) { budgetHit = true; break; } // anytime
                    // 공통 난수(variance reduction): 같은 (w,r)에서 모든 수가 같은 ε-시퀀스를 본다.
                    ulong rolloutSeed = policyBase + (ulong)(w * rolloutsPerWorld + r);
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        var sim = world.Clone();
                        if (!GameEngine.Apply(sim, GameAction.Play(_seat, candidates[i].Cards)).Ok) continue; // wish=null(P2-B)
                        double ev = Pimc.Rollout(sim, _seat, rolloutSeed, _config.Epsilon);
                        weightedSum[i] += weight * ev;
                        if (robust) worldSum[i] += ev;
                    }
                    totalWeight += weight;
                    samples++;
                    rolloutsThisWorld++;
                }
                if (robust && rolloutsThisWorld > 0)
                {
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        double we = worldSum[i] / rolloutsThisWorld;   // 이 세계의 EV(롤아웃 평균)
                        robSum[i] += we;
                        robSumSq[i] += we * we;
                    }
                    worldsCounted++;
                }
            }

            // 선택: robust(B1)면 argmax(mean − λ·std), 아니면 기존 가중 합 argmax(둘 다 Strength 최소 동점깨기).
            // robust 라도 세계<2면 분산 불능 → 가중 합 폴백. 패스 비교는 선택 후보의 weightedSum(스케일 유지) 사용.
            int bestIndex = (robust && worldsCounted >= 2)
                ? RobustArgmax(robSum, robSumSq, worldsCounted, _config.RobustLambda, candidates)
                : MeanArgmax(weightedSum, candidates);
            Combination? best = bestIndex >= 0 ? candidates[bestIndex] : null;
            double bestSum = bestIndex >= 0 ? weightedSum[bestIndex] : double.NegativeInfinity;

            // 패스(합법일 때)는 1세계로 가볍게 평가해 같은 스케일(×Worlds×rollouts)로 환산 비교.
            // 예산 만료(budgetHit) 시엔 즉시 반환으로 anytime 존중. 동점이면 수를 선호(strict >).
            // 콜러 패스억제: (작은/큰)티츄 선언자는 반드시 먼저 나가야 하므로, 이길 수 있으면 패스하지
            // 않고 EV-최선 수를 낸다(아웃 추진). 파트너-Top은 위 가드가 선행 처리. OFF면 비트불변.
            bool callerSuppressPass = _config.UseCallerAggression
                && ctx.State.Seats[_seat].Call != TichuCall.None;
            if (ctx.CanPass && !budgetHit && !callerSuppressPass)
            {
                var passWorld = Determinizer.Sample(ctx.State, _seat, ref rng, pins, _config.UseTichuCallConstraint);
                var passSim = passWorld.Clone();
                if (GameEngine.Apply(passSim, GameAction.Pass(_seat)).Ok)
                {
                    double passEv = (double)Pimc.Rollout(passSim, _seat, policyBase, _config.Epsilon) * totalWeight;
                    if (passEv > bestSum) { _rng = rng; return TurnDecision.Pass; }
                }
            }

            _rng = rng;
            return best == null ? TurnDecision.Pass : TurnDecision.Play(best, LiveWish(ctx, best));
        }

        // #2 라이브 마작 소원: 선택된 리드가 마작을 포함하면 손에 없는 최고 랭크를 소원으로 건다(롤아웃 정책과 동일).
        // UseLiveWish OFF면 null(=P2-B 동작·비트불변). MaybeWish 는 마작 미포함 시 null 이라 팔로우/비마작 리드엔 무영향.
        // 주의: EV 루프(위)는 여전히 wish=null 로 후보를 평가하므로 수 선택은 불변 — 출력 채널만 채운다.
        private int? LiveWish(in DecisionContext ctx, Combination chosen)
            => _config.UseLiveWish ? AiAgent.MaybeWish(ctx, chosen) : null;

        /// <summary>
        /// 인-턴 EV 후보 집합. 폭탄은 게이트된 <see cref="DecideBomb"/>(리치트릭 ≥15점)이 담당하므로
        /// 후보에서 제외해 싼 트릭에 폭탄을 낭비하지 않는다(휴리스틱 DecideLead/DecideFollow 와 동일 규율).
        /// 비폭탄이 하나도 없으면(소원 강제로 폭탄만 합법 등) 폭탄으로 폴백한다.
        /// </summary>
        public static List<Combination> TurnCandidates(IReadOnlyList<Combination> legal)
        {
            var c = new List<Combination>(legal.Count);
            for (int i = 0; i < legal.Count; i++)
                if (!legal[i].IsBomb) c.Add(legal[i]);
            if (c.Count == 0)
                for (int i = 0; i < legal.Count; i++) c.Add(legal[i]);
            return c;
        }

        /// <summary>
        /// 유효 롤아웃 수(D2 ε 정상화). ε≤0이면 롤아웃 디폴트 정책이 결정적이라(HeuristicRolloutPolicy 가
        /// RNG 미사용, AiAgent 도 용양도 좌우타이 코인플립 1곳만 시드 사용) R회 반복이 사실상 중복 →
        /// 1회로 정규화(Expert 24세계×6롤아웃의 ~6× 낭비 제거·굶주린 Expert 완화). ε&gt;0이면 노이즈
        /// 평균화에 R회 의미 있으므로 그대로. r&lt;1이면 1로 보정.
        /// </summary>
        public static int EffectiveRollouts(double epsilon, int rolloutsPerWorld)
        {
            int r = rolloutsPerWorld < 1 ? 1 : rolloutsPerWorld;
            return epsilon <= 0.0 ? 1 : r;
        }

        /// <summary>강건 백업 점수 = mean − λ·std(세계 간). count≤0이면 −∞. B1 순수 함수.</summary>
        public static double RobustScore(double sum, double sumSq, int count, double lambda)
        {
            if (count <= 0) return double.NegativeInfinity;
            double mean = sum / count;
            double variance = sumSq / count - mean * mean;
            if (variance < 0.0) variance = 0.0;   // 부동소수 가드
            return mean - lambda * System.Math.Sqrt(variance);
        }

        /// <summary>robSum/robSumSq(후보별 세계별 EV 합·제곱합)에서 argmax(RobustScore), 동점깨기 Strength 최소. 없으면 −1.</summary>
        public static int RobustArgmax(double[] robSum, double[] robSumSq, int worldsCounted, double lambda, IReadOnlyList<Combination> candidates)
        {
            double bestScore = double.NegativeInfinity;
            int bestStrength = int.MaxValue, best = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                double score = RobustScore(robSum[i], robSumSq[i], worldsCounted, lambda);
                int strength = MoveOrder.Strength(candidates[i]);
                if (score > bestScore || (score == bestScore && strength < bestStrength))
                { bestScore = score; bestStrength = strength; best = i; }
            }
            return best;
        }

        // 가중 합 EV argmax(동점깨기 Strength 최소). robust OFF 경로(비트불변). 없으면 −1.
        private static int MeanArgmax(double[] sum, IReadOnlyList<Combination> candidates)
        {
            double bestSum = double.NegativeInfinity;
            int bestStrength = int.MaxValue, best = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                int strength = MoveOrder.Strength(candidates[i]);
                if (sum[i] > bestSum || (sum[i] == bestSum && strength < bestStrength))
                { bestSum = sum[i]; bestStrength = strength; best = i; }
            }
            return best;
        }

        public bool CallGrandTichu(in DecisionContext ctx) => _policy.CallGrandTichu(ctx);

        public ExchangeChoice ChooseExchange(in DecisionContext ctx)
        {
            var ex = _policy.ChooseExchange(ctx);
            _passed = PassedPins(ex, _seat);   // C1: 넘긴 3장을 수령 좌석에 매핑해 기억(결정화 핀용).
            return ex;
        }

        /// <summary>교환에서 넘긴 3장을 수령 좌석에 매핑한다(C1 결정화 핀).
        /// left=(seat+1)%4, partner=(seat+2)%4, right=(seat+3)%4 (GameEngine.FinalizeExchange 와 동일).</summary>
        public static (Card card, int seat)[] PassedPins(ExchangeChoice ex, int seat) => new[]
        {
            (ex.ToLeft,    (seat + 1) % 4),
            (ex.ToPartner, (seat + 2) % 4),
            (ex.ToRight,   (seat + 3) % 4),
        };

        public bool CallTichu(in DecisionContext ctx) => _policy.CallTichu(ctx);
        public Combination? DecideBomb(in DecisionContext ctx)
        {
            // ⑧ 폭탄 세이브: 파트너가 자연 오버테이크 가능하면 폭탄 지연(창은 파트너 행동 후 재개). OFF면 비트불변.
            if (_config.UseBombSave && ctx.State.CurrentTrick != null
                && AiAgent.ShouldDeferBombForPartner(ctx, _seat, ctx.State.CurrentTrick))
                return null;
            return _policy.DecideBomb(ctx);
        }
        public int ChooseDragonRecipient(in DecisionContext ctx) => _policy.ChooseDragonRecipient(ctx);
    }
}
