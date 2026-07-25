using NUnit.Framework;
using Tichu.Presentation.Audio;
using UnityEngine;

namespace Tichu.Presentation.Tests
{
    /// <summary>BGM/효과음 볼륨 상태·PlayerPrefs 영속·Changed 이벤트의 EditMode 검증.</summary>
    public class VolumeSettingsTests
    {
        [TearDown]
        public void Cleanup()
        {
            PlayerPrefs.DeleteKey("vol.bgm");
            PlayerPrefs.DeleteKey("vol.sfx");
            VolumeSettings.Load();   // 다음 테스트 격리: 기본값 복원
        }

        [Test]
        public void SetBgm_clamps_to_unit_range()
        {
            VolumeSettings.SetBgm(-0.5f);
            Assert.AreEqual(0f, VolumeSettings.Bgm);
            VolumeSettings.SetBgm(2f);
            Assert.AreEqual(1f, VolumeSettings.Bgm);
        }

        [Test]
        public void SetSfx_persists_across_reload()
        {
            VolumeSettings.SetSfx(0.3f);
            VolumeSettings.Load();
            Assert.AreEqual(0.3f, VolumeSettings.Sfx, 1e-4f);
        }

        [Test]
        public void SetBgm_fires_Changed_only_on_new_value()
        {
            int hits = 0;
            System.Action h = () => hits++;
            VolumeSettings.Changed += h;
            try
            {
                VolumeSettings.SetBgm(0.42f);
                Assert.AreEqual(1, hits);
                VolumeSettings.SetBgm(0.42f);   // 동일 값 → 발화 없음
                Assert.AreEqual(1, hits);
            }
            finally { VolumeSettings.Changed -= h; }
        }
    }
}
