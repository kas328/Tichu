using NUnit.Framework;
using Tichu.Presentation.Shell;

namespace Tichu.Presentation.Tests
{
    /// <summary>앱 포커스 → 오디오 정지 여부(순수)의 EditMode 검증. MenuBgm.PlaysIn 미러.</summary>
    public class AudioLifecycleTests
    {
        [Test] public void Pauses_when_unfocused() => Assert.IsTrue(AudioLifecycle.ShouldPause(false));
        [Test] public void Resumes_when_focused() => Assert.IsFalse(AudioLifecycle.ShouldPause(true));
    }
}
