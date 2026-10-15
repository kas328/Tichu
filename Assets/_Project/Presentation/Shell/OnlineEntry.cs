using System;
using Tichu.Net;

namespace Tichu.Presentation.Shell
{
    public enum OnlineEntryKind { AskAddress, StartDirect, Unavailable }

    public readonly struct OnlineEntryDecision
    {
        public readonly OnlineEntryKind Kind;
        public readonly Uri Uri;   // StartDirect 일 때만

        public OnlineEntryDecision(OnlineEntryKind kind, Uri uri)
        {
            Kind = kind;
            Uri = uri;
        }
    }

    /// <summary>
    /// "온라인 대전" 버튼을 눌렀을 때 어디로 갈지(순수, 유니티 무참조). 개발 빌드는 늘 주소 입력 화면,
    /// 출시 빌드는 빌드에 넣은 출시 주소로 바로 접속하고 주소가 없거나 잘못됐으면 '준비 중'.
    /// </summary>
    public static class OnlineEntry
    {
        public static OnlineEntryDecision Decide(bool isDebugBuild, string releaseAddress)
        {
            if (isDebugBuild) return new OnlineEntryDecision(OnlineEntryKind.AskAddress, null);
            return ServerAddress.TryParse(releaseAddress, out var uri)
                ? new OnlineEntryDecision(OnlineEntryKind.StartDirect, uri)
                : new OnlineEntryDecision(OnlineEntryKind.Unavailable, null);
        }

        /// <summary>개발 빌드 주소 입력란 초기값: 기억한 주소 → 출시 주소 → 빈칸.</summary>
        public static string InitialAddress(string saved, string releaseAddress)
        {
            if (!string.IsNullOrWhiteSpace(saved)) return saved;
            return releaseAddress ?? "";
        }
    }
}
