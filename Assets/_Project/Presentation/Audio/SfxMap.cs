using System.Collections.Generic;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;
using Tichu.Net;

namespace Tichu.Presentation.Audio
{
    /// <summary>vm 관측으로 도출 가능한 사운드 이벤트. None=명시적 no-op 분기.</summary>
    public enum SfxId { None = 0, CardPlay, Pass, Bomb, GiveDragon, TichuCall, GrandTichuCall, RoundEnd, ButtonClick }

    /// <summary>
    /// 진실 로그(GameAction)·서버 액션 요약(AppliedAction)·콜(TichuCall) → SfxId 순수 매핑.
    /// UnityEngine 무의존(EditMode 전수 검증).
    /// 폭탄 판정은 RuntimeTableView.FormatAction과 동일 방식(Recognize(..., Lead).IsBomb).
    /// </summary>
    public static class SfxMap
    {
        public static SfxId For(GameAction a) => Classify(a.Kind, a.Cards);

        /// <summary>서버가 적용한 액션 → SfxId. For(GameAction) 과 같은 규칙이다.</summary>
        public static SfxId For(AppliedAction a) => Classify(a.Kind, a.Cards);

        /// <summary>종류와 카드만으로 판정한다 — 두 입구가 같은 규칙을 공유하는 지점.</summary>
        private static SfxId Classify(GameActionKind kind, IReadOnlyList<Card> cards)
        {
            switch (kind)
            {
                case GameActionKind.Pass:       return SfxId.Pass;
                case GameActionKind.GiveDragon: return SfxId.GiveDragon;
                case GameActionKind.Play:
                    // FormatAction과 동일한 null/빈 가드 후에만 Recognize(NRE 방지).
                    if (cards == null || cards.Count == 0) return SfxId.CardPlay;
                    var c = CombinationRecognizer.Recognize(ToArray(cards), TrickContext.Lead);
                    return c.IsBomb ? SfxId.Bomb : SfxId.CardPlay;
                default:                        return SfxId.None;
            }
        }

        private static Card[] ToArray(IReadOnlyList<Card> cards)
        {
            var arr = new Card[cards.Count];
            for (int i = 0; i < cards.Count; i++) arr[i] = cards[i];
            return arr;
        }

        public static SfxId ForCall(TichuCall call)
        {
            switch (call)
            {
                case TichuCall.Tichu:      return SfxId.TichuCall;
                case TichuCall.GrandTichu: return SfxId.GrandTichuCall;
                default:                   return SfxId.None;
            }
        }
    }
}
