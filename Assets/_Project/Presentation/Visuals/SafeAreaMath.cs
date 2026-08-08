using UnityEngine;

namespace Tichu.Presentation.Visuals
{
    /// <summary>화면 내 safe area를 정규화 앵커(0..1)로 변환하는 순수 함수.</summary>
    public static class SafeAreaMath
    {
        /// <summary>safeArea 를 정규화 앵커로. cornerMargin(px)은 기기의 둥근 모서리 여유 — Android 의
        /// Screen.safeArea 는 컷아웃만 인셋하고 모서리 곡률은 알려주지 않아 네 변에서 추가로 깎는다.</summary>
        public static (Vector2 min, Vector2 max) ComputeAnchors(Rect safeArea, Vector2 screen, float cornerMargin = 0f)
        {
            if (screen.x <= 0f || screen.y <= 0f) return (Vector2.zero, Vector2.one);
            // 안전영역보다 큰 여백이 앵커를 뒤집지 않도록 축마다 절반으로 클램프(작은 창·에디터 대비).
            float m = Mathf.Max(0f, cornerMargin);
            float mx = Mathf.Min(m, safeArea.width * 0.5f);
            float my = Mathf.Min(m, safeArea.height * 0.5f);
            var min = new Vector2((safeArea.xMin + mx) / screen.x, (safeArea.yMin + my) / screen.y);
            var max = new Vector2((safeArea.xMax - mx) / screen.x, (safeArea.yMax - my) / screen.y);
            return (min, max);
        }

        /// <summary>dp(밀도 독립 단위)를 픽셀로. 둥근 모서리 반경은 물리 치수(S23 ≈3.3mm)라 px 고정값은
        /// 저밀도 기기에서 과하고 고밀도에서 모자란다 → dp 로 두고 기기 dpi 로 환산한다(1dp = dpi/160 px).
        /// dpi 를 못 얻으면(에디터·일부 플랫폼이 0 반환) 기준 밀도 160 으로 폴백 = dp 를 그대로 px 로 쓴다.</summary>
        public static float DpToPixels(float dp, float dpi)
            => dpi > 0f ? dp * (dpi / 160f) : dp;

        /// <summary>IMGUI(좌상단 원점) 기준으로 safe area 안쪽 좌상단 지점. margin 은 둥근 모서리 여유.
        /// Screen.safeArea 는 좌하단 원점이라 y 를 뒤집는다(상단 미안전 높이 = screen.y - safeArea.yMax).</summary>
        public static Vector2 GuiTopLeftInside(Rect safeArea, Vector2 screen, float margin)
            => new Vector2(safeArea.xMin + margin, (screen.y - safeArea.yMax) + margin);
    }
}
