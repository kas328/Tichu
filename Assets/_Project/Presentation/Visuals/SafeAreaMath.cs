using UnityEngine;

namespace Tichu.Presentation.Visuals
{
    /// <summary>화면 내 safe area를 정규화 앵커(0..1)로 변환하는 순수 함수.</summary>
    public static class SafeAreaMath
    {
        public static (Vector2 min, Vector2 max) ComputeAnchors(Rect safeArea, Vector2 screen)
        {
            if (screen.x <= 0f || screen.y <= 0f) return (Vector2.zero, Vector2.one);
            var min = new Vector2(safeArea.xMin / screen.x, safeArea.yMin / screen.y);
            var max = new Vector2(safeArea.xMax / screen.x, safeArea.yMax / screen.y);
            return (min, max);
        }
    }
}
