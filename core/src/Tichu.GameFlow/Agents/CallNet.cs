using System;

namespace Tichu.GameFlow.Agents
{
    /// <summary>
    /// 콜 헤드 추론기(로지스틱 회귀): p = σ(w·x + b). 가중치는 오프라인 트레이너가
    /// 베이크(GrandTichuWeights). 무의존·결정적·~30줄.
    /// </summary>
    public sealed class CallNet
    {
        private readonly double[] _w;
        private readonly double _b;

        public CallNet(double[] weights, double bias)
        {
            _w = weights;
            _b = bias;
        }

        /// <summary>σ(w·x + b) ∈ (0,1). x.Length 는 w.Length 이상이어야 한다(초과분 무시).</summary>
        public double PredictProb(float[] x)
        {
            double z = _b;
            for (int i = 0; i < _w.Length; i++) z += _w[i] * x[i];
            return 1.0 / (1.0 + Math.Exp(-z));
        }

        /// <summary>Grand Tichu 콜 헤드 싱글턴(베이크된 가중치).</summary>
        public static readonly CallNet Grand = new CallNet(GrandTichuWeights.Weights, GrandTichuWeights.Bias);

        /// <summary>Small Tichu 콜 헤드 싱글턴(베이크된 가중치).</summary>
        public static readonly CallNet Small = new CallNet(SmallTichuWeights.Weights, SmallTichuWeights.Bias);
    }
}
