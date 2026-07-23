namespace Tichu.GameFlow.Agents
{
    /// <summary>
    /// D4 Fork A 가치망: 1은닉층 MLP 회귀. V(x) = (W2·relu(W1·x + b1) + b2) · Scale → 관측팀 EV(점수차).
    /// 롤아웃 리프의 드롭인 대체(같은 스케일). 무의존·결정적·손코딩 matmul.
    /// </summary>
    public sealed class ValueNet
    {
        private readonly double[] _w1; // H*F, row-major: _w1[h*F + f]
        private readonly double[] _b1; // H
        private readonly double[] _w2; // H
        private readonly double _b2;
        private readonly double _scale;
        private readonly int _f, _h;

        public ValueNet(double[] w1, double[] b1, double[] w2, double b2, int f, int h, double scale)
        {
            _w1 = w1; _b1 = b1; _w2 = w2; _b2 = b2; _f = f; _h = h; _scale = scale;
        }

        public double Evaluate(float[] x)
        {
            double sum = _b2;
            for (int h = 0; h < _h; h++)
            {
                double z = _b1[h];
                int baseIdx = h * _f;
                for (int f = 0; f < _f; f++) z += _w1[baseIdx + f] * x[f];
                if (z > 0.0) sum += _w2[h] * z; // relu
            }
            return sum * _scale;
        }

        /// <summary>베이크된 가중치의 공유 인스턴스.</summary>
        public static readonly ValueNet Shared = new ValueNet(
            ValueNetWeights.W1, ValueNetWeights.B1, ValueNetWeights.W2, ValueNetWeights.B2,
            ValueNetWeights.FeatureCount, ValueNetWeights.Hidden, ValueNetWeights.Scale);
    }
}
