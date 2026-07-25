namespace Tichu.Presentation.Shell
{
    /// <summary>앱 포커스/일시정지 → 오디오 정지 여부(순수). MenuBgm.PlaysIn 미러.</summary>
    public static class AudioLifecycle
    {
        public static bool ShouldPause(bool focused) => !focused;
    }
}
