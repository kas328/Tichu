using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Tichu.Core;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests.Bench
{
    /// <summary>
    /// D4 Fork A 가치망 트레이너. self-play 로그를 리플레이해 중간 결정상태를 캡처하고,
    /// ε=0 롤아웃(AiAgent×4 = Pimc.Rollout(ε=0))으로 관측팀 점수차를 라벨링 → MLP 회귀 학습.
    /// 순수 C#·파이썬0. ε=0 매칭이라 Expert(ε=0) 티어 A/B 와 정합. 무거운 학습은 [Explicit].
    /// </summary>
    public static class ValueNetTrainer
    {
        /// <summary>rounds 라운드 self-play 로그 리플레이 → 라운드당 statesPerRound 개 (Encode, 롤아웃값).</summary>
        public static (float[][] X, double[] y) GenerateValueData(int rounds, ulong baseSeed, int statesPerRound)
        {
            var X = new List<float[]>();
            var y = new List<double>();
            for (int r = 0; r < rounds; r++)
            {
                ulong seed = baseSeed + (ulong)r;
                var playAgents = new IAgent[4];
                for (int i = 0; i < 4; i++) playAgents[i] = new AiAgent(seed, i);
                var outcome = new GameDriver(playAgents).RunRound(GameEngine.NewRound(seed));

                // 로그 리플레이 → Play 페이즈 턴 액션(Play/Pass) 직전 상태 수집.
                var s = GameEngine.NewRound(seed);
                var states = new List<GameState>();
                var seats = new List<int>();
                foreach (var a in outcome.Log)
                {
                    if (s.Phase == RoundPhase.Play && (a.Kind == GameActionKind.Play || a.Kind == GameActionKind.Pass))
                    {
                        states.Add(s.Clone());
                        seats.Add(a.Seat);
                    }
                    var res = GameEngine.Apply(s, a);
                    if (!res.Ok) throw new InvalidOperationException($"replay illegal {a.Kind} seat {a.Seat}: {res.RejectReason}");
                }

                int n = states.Count;
                if (n == 0) continue;
                for (int k = 0; k < statesPerRound; k++)
                {
                    int idx = (int)((long)(k + 1) * n / (statesPerRound + 1));
                    if (idx >= n) idx = n - 1;
                    var st = states[idx];
                    int seat = seats[idx];
                    X.Add(WorldFeatures.Encode(st, seat));
                    y.Add(RolloutValue(st.Clone(), seat, seed ^ (ulong)(0x9E3779B9UL * (ulong)(idx + 1))));
                }
            }
            return (X.ToArray(), y.ToArray());
        }

        /// <summary>ε=0 롤아웃(AiAgent×4 완주)의 관측팀 점수차. = Pimc.Rollout(state, seat, seed, 0).</summary>
        public static double RolloutValue(GameState state, int seat, ulong seed)
        {
            var agents = new IAgent[4];
            for (int i = 0; i < 4; i++) agents[i] = new AiAgent(seed, i);
            var outcome = new GameDriver(agents).RunRound(state);
            int diff = outcome.Result.TeamATotal - outcome.Result.TeamBTotal;
            return Seating.TeamOf(seat) == 0 ? diff : -diff;
        }

        /// <summary>1은닉층 MLP 회귀 SGD(MSE + L2). 타깃 y/scale 로 정규화. 마지막 10% 검증.</summary>
        public static (double[] w1, double[] b1, double[] w2, double b2, double valRmse, double baseRmse) TrainMLP(
            float[][] X, double[] y, int F, int H, int epochs, double lr, double l2, double scale, ulong seed)
        {
            int n = X.Length, valStart = (int)(n * 0.9);
            var rng = new Rng(seed);
            var w1 = new double[H * F]; var b1 = new double[H]; var w2 = new double[H]; double b2 = 0.0;
            double lim1 = Math.Sqrt(6.0 / F), lim2 = Math.Sqrt(6.0 / H);
            for (int i = 0; i < w1.Length; i++) w1[i] = (2.0 * U(ref rng) - 1.0) * lim1;
            for (int h = 0; h < H; h++) w2[h] = (2.0 * U(ref rng) - 1.0) * lim2;

            var idx = new int[valStart];
            for (int i = 0; i < valStart; i++) idx[i] = i;
            var pre = new double[H]; var act = new double[H]; var da = new double[H];

            for (int e = 0; e < epochs; e++)
            {
                for (int i = valStart - 1; i > 0; i--) { int j = rng.NextInt(i + 1); (idx[i], idx[j]) = (idx[j], idx[i]); }
                for (int t = 0; t < valStart; t++)
                {
                    int i = idx[t]; var x = X[i];
                    double outv = b2;
                    for (int h = 0; h < H; h++)
                    {
                        double z = b1[h]; int bo = h * F;
                        for (int f = 0; f < F; f++) z += w1[bo + f] * x[f];
                        pre[h] = z; double a = z > 0 ? z : 0; act[h] = a; outv += w2[h] * a;
                    }
                    double err = outv - y[i] / scale; // dL/dout (MSE)
                    for (int h = 0; h < H; h++) da[h] = pre[h] > 0 ? err * w2[h] : 0.0;
                    b2 -= lr * err;
                    for (int h = 0; h < H; h++)
                    {
                        w2[h] -= lr * (err * act[h] + l2 * w2[h]);
                        int bo = h * F;
                        for (int f = 0; f < F; f++) w1[bo + f] -= lr * (da[h] * x[f] + l2 * w1[bo + f]);
                    }
                }
            }

            // 검증 RMSE(원 스케일) + 기저(평균 예측) RMSE.
            double se = 0, ybar = 0; int m = n - valStart;
            for (int i = valStart; i < n; i++) ybar += y[i];
            ybar /= m;
            double baseSe = 0;
            for (int i = valStart; i < n; i++)
            {
                var x = X[i]; double outv = b2;
                for (int h = 0; h < H; h++)
                {
                    double z = b1[h]; int bo = h * F;
                    for (int f = 0; f < F; f++) z += w1[bo + f] * x[f];
                    if (z > 0) outv += w2[h] * z;
                }
                double pred = outv * scale;
                se += (pred - y[i]) * (pred - y[i]);
                baseSe += (ybar - y[i]) * (ybar - y[i]);
            }
            return (w1, b1, w2, b2, Math.Sqrt(se / m), Math.Sqrt(baseSe / m));
        }

        private static double U(ref Rng rng) => (rng.NextULong() >> 11) * (1.0 / 9007199254740992.0);

        public static string EmitValueWeights(double[] w1, double[] b1, double[] w2, double b2, int f, int h, double scale)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated> D4 Fork A 가치망 가중치(ValueNetWeights). ValueNetTrainer 가 생성. 손으로 편집 금지. </auto-generated>");
            sb.AppendLine("namespace Tichu.GameFlow.Agents");
            sb.AppendLine("{");
            sb.AppendLine("    public static class ValueNetWeights");
            sb.AppendLine("    {");
            sb.AppendLine($"        public const int FeatureCount = {f};");
            sb.AppendLine($"        public const int Hidden = {h};");
            sb.AppendLine($"        public const double Scale = {scale:R};");
            sb.AppendLine($"        public const double B2 = {b2:R};");
            Arr(sb, "W1", w1); Arr(sb, "B1", b1); Arr(sb, "W2", w2);
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static void Arr(StringBuilder sb, string name, double[] a)
        {
            sb.Append($"        public static readonly double[] {name} = new double[] {{ ");
            for (int i = 0; i < a.Length; i++) { sb.Append(a[i].ToString("R")); if (i < a.Length - 1) sb.Append(", "); }
            sb.AppendLine(" };");
        }
    }
}
