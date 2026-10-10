using System;
using Tichu.Net;

namespace Tichu.Presentation.Net
{
    /// <summary>
    /// 이 기기의 식별자(M5 재접속 키). 처음 한 번 무작위 GUID 를 만들어 저장하고 이후 그대로 쓴다.
    /// 개인정보가 아니라 앱이 만든 무작위 값이다. 저장소는 호출자가 준다(유니티는 PlayerPrefs).
    /// </summary>
    public static class DeviceIdentity
    {
        public static string GetOrCreate(Func<string> load, Action<string> save)
        {
            string stored = load();
            if (ProtocolVersion.IsValidDeviceId(stored)) return stored;
            string id = Guid.NewGuid().ToString("N");
            save(id);
            return id;
        }
    }
}
