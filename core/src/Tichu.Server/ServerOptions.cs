using System;
using System.Globalization;
using Tichu.GameFlow.Agents;

namespace Tichu.Server
{
    /// <summary>
    /// 서버 실행 설정. 프로세스 하나가 랜덤 매칭으로 여러 방을 돌린다(M3 스펙 §2.3).
    /// CLI: --difficulty --target --host --port --fill-after --max-rooms --ai-delay --seed --turn-seconds --empty-room-seconds.
    /// 나머지(라운드 사이 멈춤·레이트리밋)는 테스트가 코드로 바꾸는 값이다.
    /// </summary>
    public sealed class ServerOptions
    {
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

        /// <summary>결정 한 번의 시간 제한(초). 넘으면 그 한 수는 대리 AI(M5).</summary>
        public int TurnSeconds { get; set; } = 20;
        /// <summary>방에 연결된 사람이 0명인 채 이 초가 지나면 방을 닫는다(M5).</summary>
        public int EmptyRoomSeconds { get; set; } = 120;

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
                    case "--difficulty": o.Difficulty = ParseDifficulty(value); break;
                    case "--target":     o.Target = ParseInt(key, value); break;
                    case "--host":       o.Host = value; break;
                    case "--port":       o.Port = ParseInt(key, value); break;
                    case "--ai-delay":   o.AiDelayMs = ParseInt(key, value); break;
                    case "--fill-after": o.FillAfterSeconds = ParseMin(key, value, 0); break;
                    case "--max-rooms":  o.MaxRooms = ParseMin(key, value, 1); break;
                    case "--turn-seconds": o.TurnSeconds = ParseMin(key, value, 1); break;
                    case "--empty-room-seconds": o.EmptyRoomSeconds = ParseMin(key, value, 1); break;
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
    }
}
