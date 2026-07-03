namespace Tichu.Presentation.Shell
{
    /// <summary>
    /// 앱 흐름의 화면 상태(순수). 기획서 5장 사이트맵에 대응.
    /// 일시정지는 별도 상태가 아니라 인게임의 직교 플래그(IsPaused)로 둔다 — 여기 없음.
    /// </summary>
    public enum ScreenState
    {
        Intro,
        MainHub,
        ModeSelect,
        DifficultySelect,
        HowTo,
        Settings,
        InGame,
        Result,
    }
}
