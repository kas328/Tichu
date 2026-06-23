using NUnit.Framework;
using Tichu.Presentation.Diagnostics;

namespace Tichu.Presentation.Tests
{
    /// <summary>
    /// FpsSampler 순수 평활화 로직의 EditMode 검증(UnityEngine 무의존).
    /// 윈도가 찰 때마다 Fps = frames / accumTime 로 갱신 → 일정 dt 입력이면 1/dt 로 수렴.
    /// </summary>
    public class FpsSamplerTests
    {
        [Test]
        public void Fps_is_zero_before_first_window_fills()
        {
            var s = new FpsSampler(0.5f);
            s.Tick(1f / 60f);   // 0.0167s < 0.5s 윈도
            s.Tick(1f / 60f);
            Assert.That(s.Fps, Is.EqualTo(0f));
        }

        [Test]
        public void Reports_about_60_when_ticked_at_60hz()
        {
            var s = new FpsSampler(0.5f);
            for (int i = 0; i < 60; i++) s.Tick(1f / 60f);   // 윈도 충분히 채움
            Assert.That(s.Fps, Is.EqualTo(60f).Within(0.5f));
        }

        [Test]
        public void Reports_about_30_when_ticked_at_30hz()
        {
            var s = new FpsSampler(0.5f);
            for (int i = 0; i < 60; i++) s.Tick(1f / 30f);
            Assert.That(s.Fps, Is.EqualTo(30f).Within(0.5f));
        }
    }
}
