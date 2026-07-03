namespace Tichu.Presentation.Shell
{
    /// <summary>
    /// 앱 흐름 전이 이벤트(순수). 모드 진입 파라미터(AI 난이도·시드 등)는
    /// 이 reducer가 아니라 셸(AppFlowMachine)이 GameLaunchArgs로 별도 운반한다.
    /// </summary>
    public enum AppFlowEvent
    {
        IntroFinished,
        OpenModeSelect,
        OpenHowTo,
        OpenSettings,
        OpenDifficultySelect,
        Back,
        StartAiMatch,
        SelectRankingStub,
        SelectFriendRoomStub,
        MatchEnded,
        ReturnToHub,
    }
}
