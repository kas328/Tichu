using System;
using System.IO;
using NUnit.Framework;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests.Bench
{
    /// <summary>D4 Fork A 트레이너 검증 — 데이터생성 캡처 정확성 + [Explicit] 학습.</summary>
    public class ValueNetTrainerTests
    {
        [Test]
        public void GenerateValueData_captures_states_with_correct_shape()
        {
            var (X, y) = ValueNetTrainer.GenerateValueData(rounds: 20, baseSeed: 1, statesPerRound: 3);
            Assert.That(X.Length, Is.EqualTo(60));   // 20 × 3 (모든 라운드가 Play 턴액션 보유)
            Assert.That(y.Length, Is.EqualTo(60));
            for (int i = 0; i < X.Length; i++)
            {
                Assert.That(X[i].Length, Is.EqualTo(WorldFeatures.FeatureCount));
                Assert.That(double.IsFinite(y[i]), Is.True);
                Assert.That(Math.Abs(y[i]), Is.LessThanOrEqualTo(600), "점수차 합리 범위");
            }
        }

        [Test]
        public void RolloutValue_is_deterministic_for_fixed_seed()
        {
            var s = Tichu.Core.Game.GameEngine.NewRound(42);
            double a = ValueNetTrainer.RolloutValue(s.Clone(), 0, 7);
            double b = ValueNetTrainer.RolloutValue(s.Clone(), 0, 7);
            Assert.That(a, Is.EqualTo(b));
        }

        [Explicit, Category("Bench")]
        [Test]
        public void Train_and_emit_value_weights()
        {
            const int Rounds = 40_000, StatesPerRound = 3;
            var (X, y) = ValueNetTrainer.GenerateValueData(Rounds, baseSeed: 1, StatesPerRound);
            int F = WorldFeatures.FeatureCount, H = 32;
            var (w1, b1, w2, b2, valRmse, baseRmse) = ValueNetTrainer.TrainMLP(X, y, F, H, epochs: 30, lr: 0.02, l2: 1e-5, scale: 100.0, seed: 12345);
            string src = ValueNetTrainer.EmitValueWeights(w1, b1, w2, b2, F, H, 100.0);
            string outPath = Path.Combine(Path.GetTempPath(), "ValueNetWeights.g.cs");
            File.WriteAllText(outPath, src);
            var report = $"VALUE rows={X.Length} valRmse={valRmse:F2} baseRmse={baseRmse:F2} (개선율 {(1 - valRmse / baseRmse):P1})\nweights -> {outPath}";
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "tichu_valuenet_train.txt"), report);
            TestContext.Progress.WriteLine(report);
        }
    }
}
