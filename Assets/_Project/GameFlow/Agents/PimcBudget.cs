namespace Tichu.GameFlow.Agents
{
    /// <summary>
    /// 난이도별 인게임 1수 시간예산(ms). 0 = 탐색 OFF(Easy).
    /// P2-G 정합성: 예산은 티어 오름차순 비감소. 탐색은 관전 딜레이(~900ms)와 겹쳐 돌아
    /// 900 이내는 체감 지연 0 → Normal/Hard/Expert 를 900으로 통일했다(예산 역전 제거).
    /// 유니티(RoundBootstrap)와 서버(Tichu.Server)가 이 표 하나를 공유한다.
    /// </summary>
    public static class PimcBudget
    {
        public static int For(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Easy:   return 0;
                case Difficulty.Normal: return 900;
                case Difficulty.Hard:   return 900;
                case Difficulty.Expert: return 900;
                default:                return 80;
            }
        }
    }
}
