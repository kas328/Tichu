using NUnit.Framework;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    /// <summary>B1 CallNet 로지스틱 추론 검증 — matmul 손계산 대조·sigmoid 경계·Grand 싱글턴.</summary>
    public class CallNetTests
    {
        [Test]
        public void Zero_weights_give_half()
        {
            var net = new CallNet(new double[3], 0.0);
            Assert.That(net.PredictProb(new float[] { 1f, 2f, 3f }), Is.EqualTo(0.5).Within(1e-9));
        }

        [Test]
        public void Dot_plus_bias_through_sigmoid()
        {
            // z = 2*0.5 + (-1) = 0 → σ(0) = 0.5
            var net = new CallNet(new double[] { 2.0, 0.0 }, -1.0);
            Assert.That(net.PredictProb(new float[] { 0.5f, 9f }), Is.EqualTo(0.5).Within(1e-9));
            // z = 10*1 = 10 → σ(10) ≈ 0.9999546
            var net2 = new CallNet(new double[] { 10.0 }, 0.0);
            Assert.That(net2.PredictProb(new float[] { 1f }), Is.EqualTo(0.9999546).Within(1e-5));
        }

        [Test]
        public void Prob_is_bounded()
        {
            var net = new CallNet(new double[] { 1000.0 }, 0.0);
            Assert.That(net.PredictProb(new float[] { 1f }), Is.LessThanOrEqualTo(1.0));
            Assert.That(net.PredictProb(new float[] { -1f }), Is.GreaterThanOrEqualTo(0.0));
        }

        [Test]
        public void Grand_singleton_matches_weights_length()
        {
            Assert.That(GrandTichuWeights.Weights.Length, Is.EqualTo(GrandTichuFeatures.FeatureCount));
            Assert.That(CallNet.Grand, Is.Not.Null);
        }

        [Test]
        public void Small_singleton_matches_weights_length()
        {
            Assert.That(SmallTichuWeights.Weights.Length, Is.EqualTo(SmallTichuFeatures.FeatureCount));
            Assert.That(CallNet.Small, Is.Not.Null);
        }
    }
}
