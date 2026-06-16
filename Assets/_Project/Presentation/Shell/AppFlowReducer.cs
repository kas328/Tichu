namespace Tichu.Presentation.Shell
{
    /// <summary>
    /// 순수 함수 화면 전이기. UnityEngine·R3·DI 무참조 → EditMode 헤드리스 단위 테스트 가능.
    /// 부수효과(씬 로드·패널 토글·전환 트윈·BGM)는 AppFlowMachine(비순수)이 담당한다.
    /// 정의되지 않은 (상태, 이벤트) 조합은 현재 상태를 그대로 유지한다 —
    /// 랭킹·친구방 스텁 이벤트(Phase3)와 부적합 이벤트가 모두 여기에 해당해 화면을 바꾸지 않는다.
    /// </summary>
    public static class AppFlowReducer
    {
        public static ScreenState Reduce(ScreenState current, AppFlowEvent evt) => (current, evt) switch
        {
            (ScreenState.Intro,      AppFlowEvent.IntroFinished)  => ScreenState.MainHub,
            (ScreenState.MainHub,    AppFlowEvent.OpenModeSelect) => ScreenState.ModeSelect,
            (ScreenState.MainHub,    AppFlowEvent.OpenHowTo)      => ScreenState.HowTo,
            (ScreenState.MainHub,    AppFlowEvent.OpenSettings)   => ScreenState.Settings,
            (ScreenState.ModeSelect, AppFlowEvent.StartAiMatch)   => ScreenState.InGame,
            (ScreenState.ModeSelect, AppFlowEvent.Back)           => ScreenState.MainHub,
            (ScreenState.HowTo,      AppFlowEvent.Back)           => ScreenState.MainHub,
            (ScreenState.Settings,   AppFlowEvent.Back)           => ScreenState.MainHub,
            (ScreenState.InGame,     AppFlowEvent.MatchEnded)     => ScreenState.Result,
            (ScreenState.InGame,     AppFlowEvent.ReturnToHub)    => ScreenState.MainHub,
            (ScreenState.Result,     AppFlowEvent.ReturnToHub)    => ScreenState.MainHub,
            _ => current,
        };
    }
}
