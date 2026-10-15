using System.Collections.Generic;
using Tichu.Net;

namespace Tichu.Presentation.Net
{
    /// <summary>
    /// 서버가 보낸 거부·중단 사유(대부분 영문 디버그 문자열)를 사용자 문구로 바꾼다(순수, 유니티 무참조).
    /// 알려진 폴트 문구는 서버 리터럴을 복제해 둔 것 — 서버 문구가 바뀌면 일반 문구로 떨어질 뿐 깨지지 않는다.
    /// </summary>
    public static class NetMessages
    {
        public const string LateAnswer = "시간이 지나 AI가 대신 처리했습니다";
        public const string CannotDo = "낼 수 없는 동작입니다";
        public const string ServerError = "서버 오류로 판이 중단되었습니다";

        // 클라이언트(RoundBootstrap)가 직접 내는 폴트 사유.
        public const string CannotConnect = "서버에 연결할 수 없습니다";
        public const string ConnectionLost = "서버와 연결이 끊어졌습니다";
        public const string MatchGone = "진행 중인 판이 끝났습니다";

        // 시간 초과로 대리 AI 가 이미 둔 질문을 향한 늦은 답.
        private const string Late = "stale answer";

        // AI 가 대신 처리한 일이 없는 거부 — 알리지 않는다("AI가 대신" 문구가 틀린 안내가 된다).
        private static readonly HashSet<string> Silent = new HashSet<string>
        {
            "already decided this round",          // 재접속 뒤 다시 뜬 질문 — 내 답이 이미 쓰였다
            "stale action from a previous round",  // 라운드가 끝나 버려진 수(차례 밖 폭탄 등)
        };

        private static readonly HashSet<string> KnownFaults = new HashSet<string>
        {
            "서버 연결 끊김",           // WebSocketClientTransport.LostReason(private)
            "다른 곳에서 접속",         // 서버 Matchmaker/Room
            MatchGone,                  // 서버 Room + 클라 재접속
            CannotConnect,
            ConnectionLost,
            ProtocolVersion.UpdateRequiredReason,
        };

        /// <summary>판 중 거부 → 토스트 문구. null 이면 알리지 않는다.</summary>
        public static string ForRejection(string reason)
        {
            if (reason != null && Silent.Contains(reason)) return null;
            return reason == Late ? LateAnswer : CannotDo;
        }

        /// <summary>판 중단 사유 → 메뉴 알림 문구. null(사용자 취소)은 null 그대로.</summary>
        public static string ForFault(string reason)
        {
            if (reason == null) return null;
            return KnownFaults.Contains(reason) ? reason : ServerError;
        }
    }
}
