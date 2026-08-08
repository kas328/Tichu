using UnityEngine;

namespace Tichu.Presentation.Visuals
{
    /// <summary>부착된 RectTransform을 Screen.safeArea에 맞춰 앵커 인셋한다(노치 회피).
    /// 해상도/회전 변경 시 자동 재적용. 노치 없는 기기/에디터는 인셋 0(무영향).</summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        /// <summary>기기의 둥근 모서리 여유(dp). Screen.safeArea 는 컷아웃만 인셋하고 모서리 곡률은
        /// 알려주지 않는다(Android 도 반경은 API 31 의 WindowInsets.getRoundedCorner 로 따로 뺐고
        /// Unity 는 미노출). 개별 UI 에 여백을 주면 자동회전 180° 때 반대편이 잘리므로 여기서 네 변을
        /// 함께 깎는다. 반경은 물리 치수라 px 가 아닌 dp — 21dp ≈ 3.3mm ≈ S23 모서리 반경.
        /// 실기 확인: 2026-08-08 S23(425dpi → 약 56px).</summary>
        private const float CornerMarginDp = 21f;

        private RectTransform _rt;
        private Rect _lastSafe;
        private Vector2 _lastScreen;

        private void Awake() => _rt = (RectTransform)transform;
        private void OnEnable() => Apply();

        private void Update()
        {
            if (Screen.safeArea != _lastSafe ||
                _lastScreen.x != Screen.width || _lastScreen.y != Screen.height)
                Apply();
        }

        private void Apply()
        {
            if (_rt == null) _rt = (RectTransform)transform;
            _lastSafe = Screen.safeArea;
            _lastScreen = new Vector2(Screen.width, Screen.height);
            float margin = SafeAreaMath.DpToPixels(CornerMarginDp, Screen.dpi);
            var (min, max) = SafeAreaMath.ComputeAnchors(_lastSafe, _lastScreen, margin);
            _rt.anchorMin = min;
            _rt.anchorMax = max;
            _rt.offsetMin = Vector2.zero;
            _rt.offsetMax = Vector2.zero;
        }
    }
}
