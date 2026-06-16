using NUnit.Framework;
using Tichu.Presentation.Shell;

namespace Tichu.Presentation.Tests
{
    /// <summary>
    /// 부트 진입점이 인트로를 지나 메뉴로 전이시키는지 검증(헤드리스 EditMode).
    /// 부수효과(로그)는 검증하지 않는다 — 부트 정책(메뉴로 진입)만 못박는다.
    /// </summary>
    public sealed class AppBootEntryPointTests
    {
        [Test]
        public void Boot_advances_from_intro_to_mainhub()
        {
            using var machine = new AppFlowMachine(ScreenState.Intro);
            using var ep = new AppBootEntryPoint(machine);

            ep.Start();

            Assert.That(machine.Current, Is.EqualTo(ScreenState.MainHub));
        }
    }
}
