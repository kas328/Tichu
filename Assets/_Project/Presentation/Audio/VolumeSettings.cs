using UnityEngine;

namespace Tichu.Presentation.Audio
{
    /// <summary>
    /// BGM/효과음 볼륨의 단일 진실원(정적·앱 전역). MenuBgm·SfxMap과 동일 컨벤션.
    /// PlayerPrefs 영속 + Changed 이벤트(슬라이더 변경 → BGM 소스 실시간 반영).
    /// 볼륨은 스코프를 넘는 앱 전역 상태(PlayerPrefs 자체가 전역)라 정적으로 둔다.
    /// </summary>
    public static class VolumeSettings
    {
        const string KeyBgm = "vol.bgm";
        const string KeySfx = "vol.sfx";
        const float DefaultBgm = 0.5f;
        const float DefaultSfx = 1f;

        public static event System.Action Changed;

        public static float Bgm { get; private set; } = DefaultBgm;
        public static float Sfx { get; private set; } = DefaultSfx;

        public static void Load()
        {
            Bgm = PlayerPrefs.GetFloat(KeyBgm, DefaultBgm);
            Sfx = PlayerPrefs.GetFloat(KeySfx, DefaultSfx);
        }

        public static void SetBgm(float v)
        {
            v = Mathf.Clamp01(v);
            if (v == Bgm) return;
            Bgm = v;
            PlayerPrefs.SetFloat(KeyBgm, v);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static void SetSfx(float v)
        {
            v = Mathf.Clamp01(v);
            if (v == Sfx) return;
            Sfx = v;
            PlayerPrefs.SetFloat(KeySfx, v);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
