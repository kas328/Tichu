using VContainer;
using VContainer.Unity;

namespace Tichu.Presentation.Shell
{
    /// <summary>
    /// 앱 수명 DI 스코프(App.unity 루트). 앱-수명 서비스만 등록한다.
    /// 단명 게임 객체(드라이버·에이전트·뷰모델)는 컨테이너에 올리지 않고
    /// <c>RoundBootstrap</c>이 라운드마다 직접 new로 유지한다(설계 §5.3).
    /// </summary>
    public sealed class AppScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            RegisterServices(builder);
            builder.RegisterEntryPoint<MenuShellPresenter>();    // 메뉴 셸 빌드 + 화면 라우팅
            builder.RegisterEntryPoint<GameSessionPresenter>();  // InGame→Table Additive·매치종료→Result
        }

        /// <summary>
        /// 앱-수명 서비스 등록. PlayMode 없이 EditMode에서 검증 가능한 시임으로 분리한다.
        /// </summary>
        public static void RegisterServices(IContainerBuilder builder)
        {
            // 팩토리 등록으로 생성자 선택 모호성을 피하고 AppFlowMachine을 VContainer 무참조 POCO로 유지한다.
            // Singleton + IDisposable → 스코프 종료 시 컨테이너가 Dispose 한다.
            builder.Register<AppFlowMachine>(_ => new AppFlowMachine(), Lifetime.Singleton);

            // 난이도 선택 홀더(앱 실행 중 마지막 선택 기억). 메뉴가 기록·세션 프레젠터가 읽어 GameLaunchArgs로 운반.
            builder.Register<MatchSettings>(Lifetime.Singleton);
        }
    }
}
