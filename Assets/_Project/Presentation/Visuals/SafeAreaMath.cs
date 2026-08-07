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

        /// <summary>IMGUI(좌상단 원점) 기준으로 safe area 안쪽 좌상단 지점. margin 은 둥근 모서리 여유.
        /// Screen.safeArea 는 좌하단 원점이라 y 를 뒤집는다(상단 미안전 높이 = screen.y - safeArea.yMax).</summary>
        public static Vector2 GuiTopLeftInside(Rect safeArea, Vector2 screen, float margin)
            => new Vector2(safeArea.xMin + margin, (screen.y - safeArea.yMax) + margin);
    }
}
