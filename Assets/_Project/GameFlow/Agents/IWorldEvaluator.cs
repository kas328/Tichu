using Tichu.Core.Game;

namespace Tichu.GameFlow.Agents
{
    /// <summary>
    /// PIMC 리프(후-상태) 평가 추상화. 결정화 세계 world 를 관측좌석 팀 기준 EV(점수차)로 평가한다.
    /// D4 Fork A: 롤아웃 리프를 학습 가치망으로 교체하기 위한 시임. 기본(RolloutEvaluator)은 비트불변.
    /// </summary>
    public interface IWorldEvaluator
    {
        /// <summary>world(호출자 클론, 내부 변형 가능)의 관측팀 EV. seed 는 롤아웃 ε-노이즈용(가치망은 무시).</summary>
        double Evaluate(GameState world, int observerSeat, ulong seed);
    }

    /// <summary>
    /// 현행 리프: world 를 ε-휴리스틱으로 끝까지 롤아웃한 관측팀 점수차(Pimc.Rollout).
    /// 가치망 도입 후에도 폴백 겸 학습 라벨 생성기로 보존한다.
    /// </summary>
    public sealed class RolloutEvaluator : IWorldEvaluator
    {
        private readonly double _epsilon;

        public RolloutEvaluator(double epsilon) { _epsilon = epsilon; }

        public double Evaluate(GameState world, int observerSeat, ulong seed)
            => Pimc.Rollout(world, observerSeat, seed, _epsilon);
    }

    /// <summary>
    /// 학습 리프: 결정화 세계를 가치망 V(WorldFeatures)로 1회 추론해 관측팀 EV 예측. seed 무시(결정적).
    /// D4 Fork A ①. UseValueNetLeaf 플래그로만 활성(기본 OFF=RolloutEvaluator·비트불변).
    /// </summary>
    public sealed class ValueNetEvaluator : IWorldEvaluator
    {
        public double Evaluate(GameState world, int observerSeat, ulong seed)
            => ValueNet.Shared.Evaluate(WorldFeatures.Encode(world, observerSeat));
    }
}
