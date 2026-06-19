namespace Tichu.Presentation.Visuals
{
    /// <summary>연출 duration 상수 + FastForward(스킵) 스냅 규칙. DoTween 비의존(EditMode 테스트 가능).</summary>
    public static class AnimTiming
    {
        public const float PlayPop = 0.18f;   // 트릭 플레이 팝인
        public const float TurnPulse = 0.25f;  // 차례 강조 펄스
        public const float BannerPop = 0.30f;  // 결과 배너 팝

        /// <summary>FastForward면 즉시 스냅(0), 아니면 기본 duration.</summary>
        public static float Scale(float baseDuration, bool fastForward) => fastForward ? 0f : baseDuration;
    }
}
