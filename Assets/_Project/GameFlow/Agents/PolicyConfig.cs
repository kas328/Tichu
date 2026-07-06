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

        /// <summary>true면 (작은/큰)티츄 선언자는 이길 수 있으면 패스하지 않는다(아웃 추진). OFF면 비트불변.</summary>
        public readonly bool UseCallerAggression;

        /// <summary>true면 상대가 Top+아웃/티츄 위협일 때 EV 전에 휴리스틱 블록 가드(D1)를 건다. OFF면 비트불변.</summary>
        public readonly bool UseOpponentThreatBlock;

        /// <summary>true면 후보 선택을 argmax(mean) → argmax(mean−λ·std)로 바꿔 전략 융합을 탈출(B1). OFF면 비트불변.</summary>
        public readonly bool UseRobustBackup;

        /// <summary>강건 백업(B1)의 분산 페널티 계수 λ. UseRobustBackup=false면 무시.</summary>
        public readonly double RobustLambda;

        /// <summary>true면 상대 콤보를 비싼 자원으로 밟는 낭비(팀킬)를 EV 전에 패스로 막는다(Bug4). OFF면 비트불변.</summary>
        public readonly bool UseComboOvertakeGuard;

        /// <summary>true면 끝내기(≤5장) 리드에서 EV 대신 MostShedding(콤보 우선 빠른 아웃)을 강제한다(#3). OFF면 비트불변.</summary>
        public readonly bool UseEndgameSheddingGuard;

        /// <summary>true면 낮은 싱글 팔로우에서 자연 승수가 있으면 봉황 단독을 EV 후보에서 제거한다(#2 봉황 보존). OFF면 비트불변.</summary>
        public readonly bool UsePhoenixConservation;

        /// <summary>true면 결정화 시 관측자가 교환에서 넘긴 미플레이 카드를 수령 좌석에 고정한다(C1 교환 핀). OFF면 비트불변.</summary>
        public readonly bool UseExchangePin;

        public PolicyConfig(int worlds, int rolloutsPerWorld, double epsilon, bool useReachProb = false, bool useCallerAggression = false, bool useOpponentThreatBlock = false, bool useRobustBackup = false, double robustLambda = 0.0, bool useComboOvertakeGuard = false, bool useEndgameSheddingGuard = false, bool usePhoenixConservation = false, bool useExchangePin = false)
        {
            Worlds = worlds;
            RolloutsPerWorld = rolloutsPerWorld;
            Epsilon = epsilon;
            UseReachProb = useReachProb;
            UseCallerAggression = useCallerAggression;
            UseOpponentThreatBlock = useOpponentThreatBlock;
            UseRobustBackup = useRobustBackup;
            RobustLambda = robustLambda;
            UseComboOvertakeGuard = useComboOvertakeGuard;
            UseEndgameSheddingGuard = useEndgameSheddingGuard;
            UsePhoenixConservation = usePhoenixConservation;
            UseExchangePin = useExchangePin;
        }

        /// <summary>Normal 티어 프리셋(다세계).</summary>
        public static PolicyConfig Normal => For(Difficulty.Normal);

        /// <summary>난이도 티어별 시작 프리셋.</summary>
        public static PolicyConfig For(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Easy:   return new PolicyConfig(0, 0, 0.25);   // 탐색 OFF + 블런더
                case Difficulty.Normal: return new PolicyConfig(16, 4, 0.05, useCallerAggression: true, useOpponentThreatBlock: true, usePhoenixConservation: true, useExchangePin: true);  // P2-F 16세계 + caller + D1 + #2 봉황보존 + C1 교환핀
                case Difficulty.Hard:   return new PolicyConfig(20, 4, 0.05, useCallerAggression: true, useOpponentThreatBlock: true, usePhoenixConservation: true, useExchangePin: true);  // P2-G 정합성 + #2 봉황보존 + C1 교환핀
                case Difficulty.Expert: return new PolicyConfig(24, 6, 0.00, useCallerAggression: true, useOpponentThreatBlock: true, usePhoenixConservation: true, useExchangePin: true);  // P2-G 정합성 + #2 봉황보존 + C1 교환핀
                default:                return new PolicyConfig(4, 2, 0.10);
            }
        }
    }
}
