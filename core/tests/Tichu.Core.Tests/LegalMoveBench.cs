using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests.Bench
{
    /// <summary>
    /// LegalMoveGenerator 핫패스 측정/골든 하니스(A0). LegalMoves/CanPass를 읽기만 한다(엔진 무변형).
    /// - 골든: 실전 분포 상태 코퍼스에 대해 LegalMoves+CanPass 정규화 덤프 → SHA256.
    ///   임시폴더에 베이스라인 해시를 자동 기록하고, 이후 실행은 동일성을 단언(동작 불변 게이트).
    /// - 벤치: LegalMoves(s, s.Turn)을 반복 호출해 µs/call·bytes/call 측정.
    /// 둘 다 [Explicit] — 기본 스위트 제외. `dotnet test --filter` 로 명시 실행.
    /// </summary>
    [Explicit, Category("Bench")]
    public class LegalMoveBench
    {
        private const int CorpusRounds = 120;   // 코퍼스 생성용 라운드 수(시드 1..N)
        private const int BenchIterations = 40; // 벤치 반복(코퍼스 1회 = 상태수 호출)

        private static readonly string GoldenPath =
            Path.Combine(Path.GetTempPath(), "tichu_legalmove_golden.hash");
        private static readonly string ResultPath =
            Path.Combine(Path.GetTempPath(), "tichu_legalmove_bench.txt");

        [Test]
        public void Golden_then_bench()
        {
            var corpus = BuildCorpus(CorpusRounds);

            // ── 골든: 정규화 덤프 → 해시 ──────────────────────────────────────
            var sb = new StringBuilder();
            long totalMoves = 0;
            foreach (var s in corpus)
            {
                for (int seat = 0; seat < 4; seat++)
                {
                    var moves = LegalMoveGenerator.LegalMoves(s, seat);
                    bool canPass = LegalMoveGenerator.CanPass(s, seat);
                    totalMoves += moves.Count;
                    sb.Append(seat).Append(canPass ? '1' : '0').Append('|');
                    AppendCanonical(sb, moves);
                    sb.Append('\n');
                }
            }
            string hash = Sha256Hex(sb.ToString());

            // ── 벤치: LegalMoves(s, s.Turn) µs/call·bytes/call ────────────────
            // 워밍업(JIT + 캐시).
            long warm = 0;
            foreach (var s in corpus) warm += LegalMoveGenerator.LegalMoves(s, s.Turn).Count;

            long calls = 0;
            var sw = Stopwatch.StartNew();
            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int it = 0; it < BenchIterations; it++)
            {
                foreach (var s in corpus)
                {
                    var m = LegalMoveGenerator.LegalMoves(s, s.Turn);
                    calls++;
                    // 결과를 소비해 JIT가 호출을 제거하지 못하게.
                    if (m.Count == int.MinValue) throw new InvalidOperationException();
                }
            }
            long allocAfter = GC.GetAllocatedBytesForCurrentThread();
            sw.Stop();

            double usPerCall = sw.Elapsed.TotalMilliseconds * 1000.0 / calls;
            double bytesPerCall = (allocAfter - allocBefore) / (double)calls;

            // ── 결과 기록 ─────────────────────────────────────────────────────
            var report = new StringBuilder();
            report.AppendLine($"corpusStates={corpus.Count}");
            report.AppendLine($"goldenSeatCalls={corpus.Count * 4}");
            report.AppendLine($"goldenTotalMoves={totalMoves}");
            report.AppendLine($"goldenHash={hash}");
            report.AppendLine($"benchCalls={calls}");
            report.AppendLine($"usPerCall={usPerCall:F3}");
            report.AppendLine($"bytesPerCall={bytesPerCall:F1}");
            report.AppendLine($"warmupSink={warm}");
            File.WriteAllText(ResultPath, report.ToString());

            TestContext.Progress.WriteLine(report.ToString());

            // ── 동작 불변 게이트 ──────────────────────────────────────────────
            if (File.Exists(GoldenPath))
            {
                string baseline = File.ReadAllText(GoldenPath).Trim();
                Assert.That(hash, Is.EqualTo(baseline),
                    $"LegalMoves 출력이 베이스라인과 다름! (동작 변화 발생) baseline={baseline} now={hash}\n" +
                    $"의도된 동작 변화면 {GoldenPath} 삭제 후 재베이스라인.");
            }
            else
            {
                File.WriteAllText(GoldenPath, hash);
                TestContext.Progress.WriteLine($"[baseline written] {GoldenPath} = {hash}");
            }
        }

        /// <summary>
        /// 엔드투엔드: 롤아웃 등가(GameDriver+AiAgent×4 RunRound) 시간·할당. LegalMoves가 롤아웃
        /// 비용의 대부분이므로 이 수치가 실제 속도개선의 벽시계 이득을 보여준다.
        /// 코드 변경 전후로 각각 실행해 비교(같은 시드 범위 → 같은 작업량).
        /// </summary>
        [Test]
        public void RoundPlayout_timing()
        {
            const int Rounds = 400;
            // 워밍업.
            for (ulong r = 1; r <= 20; r++)
                new GameDriver(MkAgents(r)).RunRound(GameEngine.NewRound(r));

            var sw = Stopwatch.StartNew();
            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            long sink = 0;
            for (ulong r = 1; r <= (ulong)Rounds; r++)
                sink += new GameDriver(MkAgents(r)).RunRound(GameEngine.NewRound(r)).Log.Count;
            long allocAfter = GC.GetAllocatedBytesForCurrentThread();
            sw.Stop();

            double msPerRound = sw.Elapsed.TotalMilliseconds / Rounds;
            double kbPerRound = (allocAfter - allocBefore) / 1024.0 / Rounds;

            var report = new StringBuilder();
            report.AppendLine($"rounds={Rounds}");
            report.AppendLine($"msPerRound={msPerRound:F4}");
            report.AppendLine($"kbPerRound={kbPerRound:F1}");
            report.AppendLine($"sink={sink}");
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "tichu_roundplayout.txt"), report.ToString());
            TestContext.Progress.WriteLine(report.ToString());
        }

        private static IAgent[] MkAgents(ulong seed)
        {
            var a = new IAgent[4];
            for (int i = 0; i < 4; i++) a[i] = new AiAgent(seed, i);
            return a;
        }

        // ── 코퍼스 생성: 라운드 플레이 후 로그 리플레이로 Play 상태 수확 ──────────
        private static List<GameState> BuildCorpus(int rounds)
        {
            var states = new List<GameState>(rounds * 64);
            for (int r = 1; r <= rounds; r++)
            {
                ulong seed = (ulong)r;
                var agents = new IAgent[4];
                for (int i = 0; i < 4; i++) agents[i] = new AiAgent(seed, i);

                var outcome = new GameDriver(agents).RunRound(GameEngine.NewRound(seed));

                // 동일 시드 → 동일 딜. 로그를 적용 전마다 Play 상태를 스냅샷.
                var s = GameEngine.NewRound(seed);
                foreach (var a in outcome.Log)
                {
                    if (s.Phase == RoundPhase.Play) states.Add(s.Clone());
                    var res = GameEngine.Apply(s, a);
                    if (!res.Ok) throw new InvalidOperationException($"replay diverged: {res.RejectReason}");
                }
            }
            return states;
        }

        // ── 정규화: 합법수 집합을 순서 독립 정렬해 문자열로 ────────────────────
        private static void AppendCanonical(StringBuilder sb, IReadOnlyList<Combination> moves)
        {
            var tokens = new List<string>(moves.Count);
            foreach (var m in moves) tokens.Add(MoveToken(m));
            tokens.Sort(StringComparer.Ordinal);
            for (int i = 0; i < tokens.Count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(tokens[i]);
            }
        }

        private static string MoveToken(Combination m)
        {
            var ids = new List<uint>(m.Cards.Count);
            for (int i = 0; i < m.Cards.Count; i++) ids.Add(CardId(m.Cards[i]));
            ids.Sort();
            var b = new StringBuilder();
            b.Append((int)m.Type).Append(':').Append(m.Rank).Append(':').Append(m.Length).Append(':');
            for (int i = 0; i < ids.Count; i++)
            {
                if (i > 0) b.Append(',');
                b.Append(ids[i]);
            }
            return b.ToString();
        }

        private static uint CardId(Card c) =>
            ((uint)c.Rank & 0x1F) | ((uint)(int)c.Suit << 5) | ((uint)(int)c.Special << 8);

        private static string Sha256Hex(string text)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte x in hash) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }
    }
}
