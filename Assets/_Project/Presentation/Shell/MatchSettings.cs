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

        /// <summary>친구방(개발용 NetJoin)에서 고른 서버. AI 대전을 시작하면 null 로 되돌린다.</summary>
        public System.Uri ServerAddress;

        /// <summary>메인 허브로 돌아갔을 때 한 번 띄울 알림(접속 실패·연결 끊김). 띄운 뒤 null.</summary>
        public string PendingNotice;
    }
}
