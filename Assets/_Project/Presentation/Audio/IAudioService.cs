namespace Tichu.Presentation.Audio
{
    /// <summary>
    /// 사운드 시임(진실/연출 분리). IPlayAnimator 컨벤션 미러 — 1파일 2형.
    /// D5 코어는 PlaySfx만 발화. BGM/볼륨은 D5.1에서 멤버 추가(비파괴적).
    /// </summary>
    public interface IAudioService
    {
        void PlaySfx(SfxId id);
    }

    /// <summary>사운드 없음(테스트·헤드리스 기본·뱅크 부재 폴백). 모든 호출 무시.</summary>
    public sealed class NoOpAudioService : IAudioService
    {
        public void PlaySfx(SfxId id) { }
    }
}
