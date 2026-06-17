using NUnit.Framework;
using VContainer;
using Tichu.Presentation.Shell;

namespace Tichu.Presentation.Tests
{
    /// <summary>
    /// <see cref="AppScope"/>의 앱-수명 서비스 등록을 PlayMode 없이 헤드리스로 검증한다
    /// (VContainer <see cref="ContainerBuilder"/> 직접 빌드 → resolve). MonoBehaviour·씬 무관.
    /// </summary>
    public sealed class AppScopeTests
    {
        [Test]
        public void Registers_appflowmachine_as_singleton_booting_at_intro()
        {
            var builder = new ContainerBuilder();
            AppScope.RegisterServices(builder);
            using var container = builder.Build();

            var a = container.Resolve<AppFlowMachine>();
            var b = container.Resolve<AppFlowMachine>();

            Assert.That(a, Is.Not.Null);
            Assert.That(a, Is.SameAs(b), "앱-수명 서비스는 싱글톤이어야 한다");
            Assert.That(a.Current, Is.EqualTo(ScreenState.Intro), "부트 화면은 Intro");
        }
    }
}
