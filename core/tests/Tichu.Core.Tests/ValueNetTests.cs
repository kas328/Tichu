using NUnit.Framework;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    /// <summary>D4 Fork A ValueNet(MLP 회귀) 검증 — matmul+relu 손계산 대조·Scale·Shared 싱글턴.</summary>
    public class ValueNetTests
    {
        [Test]
        public void Mlp_matmul_relu_scale()
        {
            // F=2, H=1: W1=[1,1](h0), b1=[0], W2=[2], b2=1, scale=1
            // x=[3,4] → z=7 → relu 7 → out=1+2*7=15
            var net = new ValueNet(new double[] { 1, 1 }, new double[] { 0 }, new double[] { 2 }, 1.0, 2, 1, 1.0);
            Assert.That(net.Evaluate(new float[] { 3, 4 }), Is.EqualTo(15.0).Within(1e-9));
        }

        [Test]
        public void Relu_zeroes_negative_preactivation()
        {
            // z = -5 → relu 0 → out = b2 = 1
            var net = new ValueNet(new double[] { 1 }, new double[] { 0 }, new double[] { 2 }, 1.0, 1, 1, 1.0);
            Assert.That(net.Evaluate(new float[] { -5 }), Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void Scale_multiplies_output()
        {
            // 위 예 out=15, scale=100 → 1500
            var net = new ValueNet(new double[] { 1, 1 }, new double[] { 0 }, new double[] { 2 }, 1.0, 2, 1, 100.0);
            Assert.That(net.Evaluate(new float[] { 3, 4 }), Is.EqualTo(1500.0).Within(1e-6));
        }

        [Test]
        public void Shared_singleton_matches_weight_shapes()
        {
            Assert.That(ValueNetWeights.W1.Length, Is.EqualTo(ValueNetWeights.FeatureCount * ValueNetWeights.Hidden));
            Assert.That(ValueNetWeights.B1.Length, Is.EqualTo(ValueNetWeights.Hidden));
            Assert.That(ValueNetWeights.W2.Length, Is.EqualTo(ValueNetWeights.Hidden));
            Assert.That(ValueNetWeights.FeatureCount, Is.EqualTo(WorldFeatures.FeatureCount));
            Assert.That(ValueNet.Shared, Is.Not.Null);
        }
    }
}
