using System;

namespace Tichu.Net
{
    /// <summary>
    /// 사람이 입력한 서버 주소를 WebSocket URI 로 정규화한다. 받는 형식:
    /// "192.168.0.5" · "192.168.0.5:9000" · "ws://host:7777/ws"(앞뒤 공백 허용).
    /// 스킴 생략 → ws, 포트 생략 → 7777, 경로 생략 → /ws. http 등 다른 스킴은 거절.
    /// </summary>
    public static class ServerAddress
    {
        public const int DefaultPort = 7777;

        public static bool TryParse(string? input, out Uri? uri)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(input)) return false;

            string s = input!.Trim();
            if (s.IndexOf("://", StringComparison.Ordinal) < 0) s = "ws://" + s;
            if (!Uri.TryCreate(s, UriKind.Absolute, out var parsed)) return false;
            if (parsed.Scheme != "ws" && parsed.Scheme != "wss") return false;
            if (string.IsNullOrEmpty(parsed.Host)) return false;

            var b = new UriBuilder(parsed);
            if (parsed.IsDefaultPort) b.Port = DefaultPort;
            if (b.Path == "/" || b.Path.Length == 0) b.Path = "/ws";
            uri = b.Uri;
            return true;
        }
    }
}
