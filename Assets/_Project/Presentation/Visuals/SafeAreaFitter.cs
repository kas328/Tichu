using UnityEngine;

namespace Tichu.Presentation.Visuals
{
    /// <summary>부착된 RectTransform을 Screen.safeArea에 맞춰 앵커 인셋한다(노치 회피).
    /// 해상도/회전 변경 시 자동 재적용. 노치 없는 기기/에디터는 인셋 0(무영향).</summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
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
            var (min, max) = SafeAreaMath.ComputeAnchors(_lastSafe, _lastScreen);
            _rt.anchorMin = min;
            _rt.anchorMax = max;
            _rt.offsetMin = Vector2.zero;
            _rt.offsetMax = Vector2.zero;
        }
    }
}
