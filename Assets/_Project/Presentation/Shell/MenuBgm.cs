namespace Tichu.Presentation.Shell
{
    /// <summary>
    /// 메뉴 BGM 재생 정책(순수·UnityEngine 무의존 → EditMode 검증).
    /// 실제 플레이(InGame) 중에만 무음, 그 외 메뉴/결과 화면에선 재생.
    /// </summary>
    public static class MenuBgm
    {
        public static bool PlaysIn(ScreenState s) => s != ScreenState.InGame;
    }
}
