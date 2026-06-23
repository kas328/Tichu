using UnityEngine;

namespace Tichu.Presentation.Audio
{
    /// <summary>
    /// IAudioService 실 구현(DoTweenPlayAnimator 본보기 — 트윈 대신 AudioSource만).
    /// 보이스 풀 N=6: 4인 동시 연출(폭탄+차례+결과) 여유. 스틸링/동적확장 미도입(YAGNI) — D6 프로파일러 조정.
    /// AudioListener는 App.unity 카메라의 기존 1개를 재사용(추가 생성 0).
    /// </summary>
    public sealed class UnityAudioService : IAudioService
    {
        private const float Vol = 1f;
        private readonly AudioBank _bank;
        private readonly AudioSource[] _voices;
        private int _next;

        public UnityAudioService(AudioBank bank, int sfxVoices = 6)
        {
            _bank = bank;
            var host = new GameObject("AudioRoot") { hideFlags = HideFlags.HideAndDontSave };
            _voices = new AudioSource[Mathf.Max(1, sfxVoices)];
            for (int i = 0; i < _voices.Length; i++)
            {
                var src = host.AddComponent<AudioSource>();
                src.playOnAwake = false;
                _voices[i] = src;
            }
        }

        public void PlaySfx(SfxId id)
        {
            if (_bank == null) return;                 // 명시 주입 방어(bank null)
            var clip = _bank.Clip(id);
            if (clip == null) return;                  // 미할당 → 무음(PlayOneShot 미호출)
            _voices[_next++ % _voices.Length].PlayOneShot(clip, Vol);
        }
    }
}
