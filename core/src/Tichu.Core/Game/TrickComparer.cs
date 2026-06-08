using Tichu.Core.Combinations;

namespace Tichu.Core.Game
{
    /// <summary>
    /// Trick을 Combinations 레이어의 TrickContext / CombinationComparer로 연결하는 얇은 어댑터.
    /// Game → Combinations 단방향 의존을 유지한다(Combinations는 Game을 모른다).
    /// </summary>
    public static class TrickComparer
    {
        /// <summary>
        /// 현재 trick 위에 카드를 놓으려는 플레이어를 위한 인식 문맥을 반환한다.
        /// trick이 null이거나 Top이 없으면 TrickContext.Lead를 반환한다.
        /// </summary>
        public static TrickContext ContextFor(Trick trick)
        {
            if (trick == null || trick.Top == null)
                return TrickContext.Lead;

            if (trick.Top.Type == CombinationType.Single)
                return new TrickContext(false, true, trick.Top.Rank);

            return new TrickContext(false, false, 0);
        }

        /// <summary>
        /// candidate가 trick의 현재 Top을 이길 수 있는지 반환한다.
        /// - candidate가 null 또는 Invalid이면 false.
        /// - trick이 null 또는 Top이 없으면 false.
        ///   (리드 적법성은 LegalMoves 레이어에서 판단한다.)
        /// - 그 외는 CombinationComparer.Beats에 위임한다.
        ///   봉황, 폭탄 인터럽트 등 모든 규칙이 이미 거기에 구현되어 있다.
        /// </summary>
        public static bool Beats(Combination candidate, Trick trick)
        {
            if (candidate == null || candidate.Type == CombinationType.Invalid)
                return false;

            if (trick == null || trick.Top == null)
                return false;

            return CombinationComparer.Beats(candidate, trick.Top);
        }
    }
}
