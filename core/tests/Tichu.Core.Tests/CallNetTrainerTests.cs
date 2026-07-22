using System.IO;
using NUnit.Framework;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests.Bench
{
    /// <summary>B1 트레이너 데이터생성 캡처 정확성 — 8장 피처·라운드당 정확히 한 승자.</summary>
    public class CallNetTrainerTests
    {
        [Test]
        public void GenerateData_captures_8card_hands_and_one_winner_per_round()
        {
            var (X, y) = CallNetTrainer.GenerateData(rounds: 20, baseSeed: 1);
            Assert.That(X.Length, Is.EqualTo(80));  // 20라운드 × 4좌석
            Assert.That(y.Length, Is.EqualTo(80));
            for (int i = 0; i < X.Length; i++)
                Assert.That(X[i].Length, Is.EqualTo(GrandTichuFeatures.FeatureCount));
            // 라운드마다 정확히 한 좌석이 먼저 나감(label=1).
            for (int r = 0; r < 20; r++)
            {
                int wins = 0;
                for (int seat = 0; seat < 4; seat++) wins += y[r * 4 + seat];
                Assert.That(wins, Is.EqualTo(1), $"round {r}");
            }
        }

        [Explicit, Category("Bench")]
        [Test]
        public void Train_and_emit_weights()
        {
            const int Rounds = 50_000;      // ×4 = 20만 행
            var (X, y) = CallNetTrainer.GenerateData(Rounds, baseSeed: 1);
            int pos = 0; for (int i = 0; i < y.Length; i++) pos += y[i];
            var (w, b, ll, acc) = CallNetTrainer.TrainLogistic(X, y, epochs: 40, lr: 0.1, l2: 1e-4, seed: 12345);
            string src = CallNetTrainer.EmitWeightsSource(w, b, 0.5);
            string outPath = Path.Combine(Path.GetTempPath(), "GrandTichuWeights.g.cs");
            File.WriteAllText(outPath, src);
            var report = $"rows={X.Length} baseRate={pos / (double)y.Length:F3} valLogloss={ll:F4} valAcc={acc:F3}\nweights -> {outPath}";
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "tichu_callnet_train.txt"), report);
            TestContext.Progress.WriteLine(report);
            TestContext.Progress.WriteLine(src);
        }
    }
}
