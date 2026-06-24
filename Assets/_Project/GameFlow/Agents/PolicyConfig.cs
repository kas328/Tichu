namespace Tichu.GameFlow.Agents
{
    /// <summary>
    /// PIMC 탐색/정책 파라미터. 난이도 티어는 이 값 주입만으로 구분된다.
    /// 숫자는 시작값(스펙 §5) — P2-C 벤치로 확정. Hard/Expert 의 reach-prob·EPIMC 등 고급
    /// 기능은 미구현(P2-D/E); 여기서는 세계수/롤아웃/ε 만 정의한다.
    /// </summary>
    public readonly struct PolicyConfig
    {
        /// <summary>결정화 세계 수. 0이면 탐색 OFF(휴리스틱 직접 결정).</summary>
        public readonly int Worlds;

        /// <summary>세계당 롤아웃 수(ε&gt;0일 때 노이즈 평균화에 의미).</summary>
        public readonly int RolloutsPerWorld;

        /// <summary>롤아웃 디폴트 정책의 무작위 확률(ε-greedy). 0이면 순수 휴리스틱.</summary>
        public readonly double Epsilon;

        /// <summary>true면 reach-probability 가중 세계(Hard+). false면 균등 평균.</summary>
        public readonly bool UseReachProb;

        public PolicyConfig(int worlds, int rolloutsPerWorld, double epsilon, bool useReachProb = false)
        {
            Worlds = worlds;
            RolloutsPerWorld = rolloutsPerWorld;
            Epsilon = epsilon;
            UseReachProb = useReachProb;
        }

        /// <summary>Normal 티어 프리셋(다세계).</summary>
        public static PolicyConfig Normal => For(Difficulty.Normal);

        /// <summary>난이도 티어별 시작 프리셋.</summary>
        public static PolicyConfig For(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Easy:   return new PolicyConfig(0, 0, 0.25);   // 탐색 OFF + 블런더
                case Difficulty.Normal: return new PolicyConfig(4, 2, 0.10);
                case Difficulty.Hard:   return new PolicyConfig(16, 4, 0.05, useReachProb: true);  // reach-prob P2-D2
                case Difficulty.Expert: return new PolicyConfig(24, 6, 0.00, useReachProb: true);  // 고급기능 P2-E
                default:                return new PolicyConfig(4, 2, 0.10);
            }
        }
    }
}
