using System;
using System.Globalization;

namespace Tichu.Net
{
    /// <summary>
    /// 와이어 형식 버전(M2b 스펙 §5.3). 서버가 인터넷에 올라간 뒤로는 서버와 폰에 깔린 앱이 따로
    /// 업데이트되므로, 메시지 형식이 바뀌는 변경(M3 등)은 Current 를 올린다. 클라이언트는 접속 URI
    /// 쿼리 v 로 싣고, 서버는 좌석을 주기 전에 확인해 다르면 업데이트 안내(SessionFaulted)를 보내고 닫는다.
    /// </summary>
    public static class ProtocolVersion
    {
        public const int Current = 2;
        public const string QueryKey = "v";

        /// <summary>접속 URI 에 v=Current 를 붙인다. 이미 다른 쿼리가 있으면 &amp; 로 잇는다.</summary>
        public static Uri Attach(Uri uri)
        {
            bool hasQuery = uri.Query.Length > 1;   // "" 또는 "?a=b"
            string head = uri.GetLeftPart(hasQuery ? UriPartial.Query : UriPartial.Path);
            return new Uri(head + (hasQuery ? "&" : "?") + QueryKey + "=" + Current.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>서버: 받은 쿼리 값이 지금 버전인가. 없음·부호·공백·소수·중복·다른 값은 전부 false.</summary>
        public static bool Accepts(string? value) =>
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int v) && v == Current;

        public static string UpdateRequiredReason =>
            "앱을 업데이트해 주세요 (서버 프로토콜 v" + Current.ToString(CultureInfo.InvariantCulture) + ")";
    }
}
