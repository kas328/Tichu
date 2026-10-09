using System;
using System.Collections.Generic;
using System.Globalization;
using Tichu.GameFlow.Agents;

namespace Tichu.Server
{
    /// <summary>
    /// 서버 실행 설정. 프로세스 하나 = 매치 하나(M2 스펙 §4.1).
    /// CLI 로 받는 값: --seats --difficulty --target --host --port --ai-delay --seed.
    /// 나머지(라운드 사이 멈춤·레이트리밋)는 테스트가 코드로 바꾸는 값이다.
    /// </summary>
    public sealed class ServerOptions
    {
        /// <summary>인간 좌석 목록. 접속 순서대로 이 순서로 배정한다. 기본 0,2 = 친구와 같은 팀.</summary>
        public int[] Seats { get; set; } = { 0, 2 };
        public Difficulty Difficulty { get; set; } = Difficulty.Hard;
        public int Target { get; set; } = 1000;
        /// <summary>바인딩 주소. 기본 0.0.0.0 = 같은 Wi-Fi 개발 테스트. 클라우드는 127.0.0.1(Caddy 뒤, M2b).</summary>
        public string Host { get; set; } = "0.0.0.0";
        public int Port { get; set; } = 7777;
        /// <summary>대기열에서 가장 오래 기다린 사람이 이 초를 채우면 빈자리를 AI 로 채워 시작한다(M3 스펙 §1).</summary>
        public int FillAfterSeconds { get; set; } = 30;
        /// <summary>동시에 도는 방 상한. 꽉 차면 대기열에서 순번을 안내한다. 확정은 Lightsail 실측 후.</summary>
        public int MaxRooms { get; set; } = 2;
        /// <summary>AI 관전 지연(ms). 유니티 GameLaunchArgs.AiDelayMs 기본값과 같다.</summary>
        public int AiDelayMs { get; set; } = 900;
        /// <summary>라운드 결과를 읽을 시간(ms). 유니티 싱글플레이와 같은 3.5초.</summary>
        public int InterRoundPauseMs { get; set; } = 3500;
        /// <summary>매치 시드. null 이면 시작 시각에서 뽑는다.</summary>
        public ulong? Seed { get; set; }
        /// <summary>연결당 토큰 버킷 — 버스트 20건, 초당 10건(스펙 §6.3).</summary>
        public int RateCapacity { get; set; } = 20;
        public double RatePerSecond { get; set; } = 10;

        public static ServerOptions Parse(string[] args)
        {
            var o = new ServerOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i];
                if (i + 1 >= args.Length) throw new ArgumentException($"{key} 의 값이 없습니다");
                string value = args[++i];
                switch (key)
                {
                    case "--seats":      o.Seats = ParseSeats(value); break;
                    case "--difficulty": o.Difficulty = ParseDifficulty(value); break;
                    case "--target":     o.Target = ParseInt(key, value); break;
                    case "--host":       o.Host = value; break;
                    case "--port":       o.Port = ParseInt(key, value); break;
                    case "--ai-delay":   o.AiDelayMs = ParseInt(key, value); break;
                    case "--fill-after": o.FillAfterSeconds = ParseMin(key, value, 0); break;
                    case "--max-rooms":  o.MaxRooms = ParseMin(key, value, 1); break;
                    case "--seed":
                        if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seed))
                            throw new ArgumentException($"--seed 값이 올바르지 않습니다: {value}");
                        o.Seed = seed;
                        break;
                    default: throw new ArgumentException($"알 수 없는 인자: {key}");
                }
            }
            return o;
        }

        private static int ParseInt(string key, string value) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
                ? v : throw new ArgumentException($"{key} 값이 올바르지 않습니다: {value}");

        private static int ParseMin(string key, string value, int min)
        {
            int v = ParseInt(key, value);
            return v >= min ? v : throw new ArgumentException($"{key} 는 {min} 이상: {value}");
        }

        private static Difficulty ParseDifficulty(string value)
        {
            // 숫자 문자열("7")도 Enum.TryParse 를 통과하므로 이름만 받는다.
            foreach (Difficulty d in Enum.GetValues(typeof(Difficulty)))
                if (string.Equals(d.ToString(), value, StringComparison.OrdinalIgnoreCase)) return d;
            throw new ArgumentException($"--difficulty 는 Easy/Normal/Hard/Expert 중 하나: {value}");
        }

        private static int[] ParseSeats(string value)
        {
            var seats = new List<int>();
            foreach (var part in value.Split(','))
            {
                if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int s) || s < 0 || s > 3)
                    throw new ArgumentException($"--seats 의 좌석은 0~3: {value}");
                if (seats.Contains(s)) throw new ArgumentException($"--seats 에 중복 좌석: {value}");
                seats.Add(s);
            }
            return seats.ToArray();
        }
    }
}
