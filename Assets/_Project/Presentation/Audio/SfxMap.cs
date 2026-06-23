using System.Linq;
using Tichu.Core.Combinations;
using Tichu.Core.Game;

namespace Tichu.Presentation.Audio
{
    /// <summary>vm 관측으로 도출 가능한 사운드 이벤트. None=명시적 no-op 분기.</summary>
    public enum SfxId { None = 0, CardPlay, Pass, Bomb, GiveDragon, TichuCall, GrandTichuCall, RoundEnd }

    /// <summary>
    /// 진실 로그(GameAction)·콜(TichuCall) → SfxId 순수 매핑. UnityEngine 무의존(EditMode 전수 검증).
    /// 폭탄 판정은 RuntimeTableView.FormatAction과 동일 방식(Recognize(..., Lead).IsBomb).
    /// </summary>
    public static class SfxMap
    {
        public static SfxId For(GameAction a)
        {
            switch (a.Kind)
            {
                case GameActionKind.Pass:       return SfxId.Pass;
                case GameActionKind.GiveDragon: return SfxId.GiveDragon;
                case GameActionKind.Play:
                    // FormatAction과 동일한 null/빈 가드 후에만 Recognize(NRE 방지).
                    if (a.Cards == null || a.Cards.Count == 0) return SfxId.CardPlay;
                    var c = CombinationRecognizer.Recognize(a.Cards.ToArray(), TrickContext.Lead);
                    return c.IsBomb ? SfxId.Bomb : SfxId.CardPlay;
                default:                        return SfxId.None;
            }
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
