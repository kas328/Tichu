namespace Tichu.Presentation.Diagnostics
{
    /// <summary>
    /// 프레임 시간 누적 평활화 FPS 샘플러(순수·UnityEngine 무의존 → EditMode 검증).
    /// 윈도(기본 0.5s)가 찰 때마다 Fps = frames / accumTime 로 갱신.
    /// </summary>
    public sealed class FpsSampler
    {
        private readonly float _window;
        private float _accumTime;
        private int _accumFrames;

        /// <summary>최근 윈도 평균 FPS(첫 윈도 채워지기 전 0).</summary>
        public float Fps { get; private set; }

        public FpsSampler(float windowSeconds = 0.5f) { _window = windowSeconds; }

        /// <summary>한 프레임 보고. 윈도가 차면 Fps 갱신 후 누적 리셋.</summary>
        public void Tick(float deltaTime)
        {
            _accumTime += deltaTime;
            _accumFrames++;
            if (_accumTime >= _window)
            {
                Fps = _accumFrames / _accumTime;
                _accumTime = 0f;
                _accumFrames = 0;
            }
        }
    }
}
