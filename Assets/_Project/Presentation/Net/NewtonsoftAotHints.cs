using Newtonsoft.Json.Utilities;
using Tichu.Core.Game;
using UnityEngine.Scripting;

namespace Tichu.Presentation.Net
{
    /// <summary>
    /// IL2CPP 제네릭 공유 보험 — 프로토콜 DTO 의 List&lt;T&gt; 인스턴스를 스트리핑·AOT 가 빠뜨리지 않게
    /// 미리 컴파일시킨다. 호출은 하지 않는다(Preserve 만으로 인스턴스화가 남는다).
    /// </summary>
    public static class NewtonsoftAotHints
    {
        [Preserve]
        public static void EnsureInstances()
        {
            AotHelper.EnsureList<TichuCall>();
            AotHelper.EnsureList<bool>();
            AotHelper.EnsureList<int>();
        }
    }
}
