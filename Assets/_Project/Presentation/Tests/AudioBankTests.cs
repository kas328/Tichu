using System.Reflection;
using NUnit.Framework;
using Tichu.Presentation.Audio;
using UnityEngine;

namespace Tichu.Presentation.Tests
{
    /// <summary>
    /// AudioBank(SfxId→AudioClip) 룩업/널가드 EditMode 검증. CardSpriteAtlasTests 골격.
    /// S7: 빈뱅크/등록뱅크를 별도 인스턴스로(지연 _map stale 회피), 등록→참조동일성(SameAs).
    /// </summary>
    public class AudioBankTests
    {
        private static void SetClips(AudioBank bank, AudioBank.Entry[] entries)
        {
            var f = typeof(AudioBank).GetField("clips", BindingFlags.NonPublic | BindingFlags.Instance);
            f.SetValue(bank, entries);
        }

        [Test]
        public void Empty_bank_returns_null_for_any_id()
        {
            var bank = ScriptableObject.CreateInstance<AudioBank>();
            try { Assert.That(bank.Clip(SfxId.CardPlay), Is.Null); }
            finally { Object.DestroyImmediate(bank); }
        }

        [Test]
        public void Registered_id_returns_that_clip()
        {
            var bank = ScriptableObject.CreateInstance<AudioBank>();
            var clip = AudioClip.Create("t", 1, 1, 44100, false);
            try
            {
                SetClips(bank, new[] { new AudioBank.Entry { id = SfxId.CardPlay, clip = clip } });
                Assert.That(bank.Clip(SfxId.CardPlay), Is.SameAs(clip));
            }
            finally { Object.DestroyImmediate(clip); Object.DestroyImmediate(bank); }
        }

        [Test]
        public void Unregistered_id_returns_null()
        {
            var bank = ScriptableObject.CreateInstance<AudioBank>();
            var clip = AudioClip.Create("t", 1, 1, 44100, false);
            try
            {
                SetClips(bank, new[] { new AudioBank.Entry { id = SfxId.CardPlay, clip = clip } });
                Assert.That(bank.Clip(SfxId.Pass), Is.Null);
            }
            finally { Object.DestroyImmediate(clip); Object.DestroyImmediate(bank); }
        }
    }
}
