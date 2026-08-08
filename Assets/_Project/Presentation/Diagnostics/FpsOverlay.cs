using UnityEngine;

namespace Tichu.Presentation.Diagnostics
{
    /// <summary>
    /// 디버그 빌드 전용 FPS 오버레이(IMGUI). 60fps 게이트 실기기 측정용.
    /// RoundBootstrap이 Debug.isDebugBuild일 때만 Create — 릴리스 빌드에선 미생성(no-op).
    /// 색: 녹(≥55)·황(30~55)·적(&lt;30). 측정은 평활화 FpsSampler(EditMode 검증).
    /// </summary>
    public sealed class FpsOverlay : MonoBehaviour
    {
        private readonly FpsSampler _sampler = new FpsSampler();
        private GUIStyle _style;

        public static FpsOverlay Create()
        {
            var go = new GameObject("FpsOverlay") { hideFlags = HideFlags.HideAndDontSave };
            return go.AddComponent<FpsOverlay>();
        }

        private void Update() => _sampler.Tick(Time.unscaledDeltaTime);

        /// <summary>둥근 모서리·컷아웃 여유(px). Screen.safeArea 는 둥근 모서리를 인셋해 주지 않는다.</summary>
        private const float Margin = 40f;

        private void OnGUI()
        {
            if (_style == null)
                _style = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold };
            float fps = _sampler.Fps;
            _style.normal.textColor = fps >= 55f ? Color.green : (fps >= 30f ? Color.yellow : Color.red);
            var p = Visuals.SafeAreaMath.GuiTopLeftInside(
                Screen.safeArea, new Vector2(Screen.width, Screen.height), Margin);
            GUI.Label(new Rect(p.x, p.y, 320, 40), Mathf.RoundToInt(fps) + " fps", _style);
        }
    }
}
