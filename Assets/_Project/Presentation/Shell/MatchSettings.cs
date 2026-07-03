namespace Tichu.Presentation.Shell
{
    /// <summary>
    /// UI가 고르는 매치 진입 파라미터(현재는 AI 난이도)를 담는 앱-수명 홀더.
    /// 순수 <see cref="AppFlowReducer"/>/이벤트는 ScreenState만 다루므로(AppFlowEvent 주석 참조),
    /// 난이도는 여기에 기록해 <see cref="GameSessionPresenter"/>가 GameLaunchArgs로 운반한다.
    /// AppScope 싱글톤 — 앱 실행 중 마지막 선택을 기억(기본 Normal). 재시작 지속(PlayerPrefs)은 후속.
    /// </summary>
    public sealed class MatchSettings
    {
        public Tichu.GameFlow.Agents.Difficulty Difficulty = Tichu.GameFlow.Agents.Difficulty.Normal;
    }
}
