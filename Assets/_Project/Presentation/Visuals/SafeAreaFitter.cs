using UnityEngine;

namespace Tichu.Presentation.Visuals
{
    /// <summary>부착된 RectTransform을 Screen.safeArea에 맞춰 앵커 인셋한다(노치 회피).
    /// 해상도/회전 변경 시 자동 재적용. 노치 없는 기기/에디터는 인셋 0(무영향).</summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        /// <summary>둥근 모서리 여유(dp). 기본 0 = 안전영역만(테이블 배치는 화면을 다 써야 한다).
        /// Screen.safeArea 는 컷아웃만 인셋하고 모서리 곡률은 알려주지 않는다(Android 도 반경은 API 31
        /// 의 WindowInsets.getRoundedCorner 로 따로 뺐고 Unity 는 미노출). 반경은 물리 치수라 px 가
        /// 아닌 dp 로 둔다. 실제로 잘리는 코너 UI 레이어에서만 켠다 — 전체에 주면 화면이 중앙으로
        /// 당겨져 손패가 올라온다(2026-08-08 S23 실기 피드백).</summary>
        private float _cornerMarginDp;

        private RectTransform _rt;
        private Rect _lastSafe;
        private Vector2 _lastScreen;

        private void Awake() => _rt = (RectTransform)transform;
        private void OnEnable() => Apply();

        /// <summary>둥근 모서리 여유를 켜고 즉시 재적용(AddComponent 직후 호출용).</summary>
        public void SetCornerMarginDp(float dp)
        {
            _cornerMarginDp = dp;
            Apply();
        }

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
            float margin = SafeAreaMath.DpToPixels(_cornerMarginDp, Screen.dpi);
            var (min, max) = SafeAreaMath.ComputeAnchors(_lastSafe, _lastScreen, margin);
            _rt.anchorMin = min;
            _rt.anchorMax = max;
            _rt.offsetMin = Vector2.zero;
            _rt.offsetMax = Vector2.zero;
        }
    }
}
