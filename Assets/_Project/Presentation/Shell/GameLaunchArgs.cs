namespace Tichu.Presentation.Shell
{
    /// <summary>
    /// 모드 진입 파라미터. 셸(GameSessionPresenter)이 단일 Table 진입에 운반한다(설계 §5.2).
    /// 랭킹/친구방도 이 DTO로 동일 Table에 수렴(Phase3) — 현재는 AI 대전만 사용.
    /// </summary>
    public sealed class GameLaunchArgs
    {
        /// <summary>매치 종료 목표 점수(기본 1000, 동점 돌파 시 속행).</summary>
        public int TargetScore = 1000;

        /// <summary>AI가 카드를 낼 때의 딜레이(ms).</summary>
        public int AiDelayMs = 900;

        /// <summary>true면 매 실행 무작위 분배, false면 <see cref="Seed"/> 고정(재현).</summary>
        public bool RandomDeal = true;

        /// <summary>고정 분배 시드(RandomDeal=false일 때).</summary>
        public ulong Seed = 42;

        /// <summary>인간 좌석(South=0).</summary>
        public int MySeat = 0;
    }

    /// <summary>매치 결과 요약(Result 화면 표시용). 팀 A=좌석 0·2(나·파트너), 팀 B=좌석 1·3.</summary>
    public readonly struct MatchSummary
    {
        public readonly int WinningTeam;   // 0 = 우리 팀(A), 1 = 상대 팀(B)
        public readonly int TeamA;
        public readonly int TeamB;

        public MatchSummary(int winningTeam, int teamA, int teamB)
        {
            WinningTeam = winningTeam;
            TeamA = teamA;
            TeamB = teamB;
        }
    }
}
