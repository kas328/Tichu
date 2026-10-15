using UnityEngine;

namespace Tichu.Presentation.Shell
{
    /// <summary>
    /// 출시 빌드가 접속할 서버 주소(Resources/ReleaseServer.asset). M2b 에서 운영 주소를 채운다 —
    /// 비어 있으면 출시 빌드의 온라인 대전은 '준비 중'(<see cref="OnlineEntry"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "Tichu/Release Server", fileName = "ReleaseServer")]
    public sealed class ReleaseServerConfig : ScriptableObject
    {
        public string address = "";

        /// <summary>에셋의 주소. 에셋이 없으면 null.</summary>
        public static string LoadAddress()
        {
            var cfg = Resources.Load<ReleaseServerConfig>("ReleaseServer");
            return cfg != null ? cfg.address : null;
        }
    }
}
