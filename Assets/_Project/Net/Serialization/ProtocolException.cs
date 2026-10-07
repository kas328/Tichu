using System;

namespace Tichu.Net
{
    /// <summary>
    /// 프로토콜 메시지를 해석할 수 없다(깨진 JSON · 모르는 Kind · 필드 누락).
    /// 서버는 이걸 받으면 그 연결을 닫는다. Newtonsoft 예외를 감싸 호출자가 라이브러리를 모르게 한다.
    /// </summary>
    public sealed class ProtocolException : Exception
    {
        public ProtocolException(string message) : base(message) { }
        public ProtocolException(string message, Exception inner) : base(message, inner) { }
    }
}
