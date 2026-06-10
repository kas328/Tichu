using System.Collections.Generic;
using Tichu.Core;
using Tichu.Core.Cards;
using Tichu.Core.Game;

namespace Tichu.Core.Tests.Sim
{
    /// <summary>
    /// 주입된 Rng로만 결정하는 결정적 봇.
    /// System.Random, DateTime 등 비결정적 소스를 일체 사용하지 않는다.
    /// </summary>
    internal static class RandomBot
    {
        /// <summary>1/20 확률로 큰 티츄 선언 (Simulator에서도 참조).</summary>
        internal const int GrandTichuRateValue = 20;
        private  const int TichuRate           = 30;  // 1/30 확률로 작은 티츄 선언
        private  const int WishRate            = 10;  // 1/10 확률로 소원 거는 마작 리드

        /// <summary>
        /// Exchange 페이즈: seat의 교환 액션을 만든다.
        /// 손패에서 앞 세 장을 deterministic하게 뽑는다.
        /// </summary>
        public static GameAction DecideExchange(GameState s, int seat)
        {
            var hand = s.Seats[seat].Hand;
            // 손패의 첫 세 장 — 딜/교환 순서가 결정적이므로 동일 시드면 항상 동일.
            var toLeft    = new List<Card> { hand[0] };
            var toPartner = new List<Card> { hand[1] };
            var toRight   = new List<Card> { hand[2] };
            return GameAction.Exchange(seat, toLeft, toPartner, toRight);
        }

        /// <summary>
        /// Play 페이즈: s.Turn 좌석의 다음 액션을 결정한다.
        /// - 폭탄 인터럽트(다른 턴 좌석)는 봇이 구동하지 않는다.
        /// - 소원 강제가 있으면 LegalMoves가 이미 필터링한 목록을 따른다.
        /// </summary>
        public static GameAction DecidePlay(GameState s, ref Rng rng)
        {
            int seat = s.Turn;

            // 작은 티츄 선언: 교환 직후 14장인 상태에서 아직 선언하지 않은 경우.
            if (s.Seats[seat].Call == TichuCall.None &&
                s.Seats[seat].Hand.Count == 14 &&
                rng.NextInt(TichuRate) == 0)
            {
                return GameAction.CallTichu(seat);
            }

            var moves   = LegalMoveGenerator.LegalMoves(s, seat);
            bool canPass = LegalMoveGenerator.CanPass(s, seat);
            bool isLead  = s.CurrentTrick == null;

            if (isLead)
            {
                // 리드: 반드시 카드를 내야 한다.
                int idx = moves.Count > 1 ? rng.NextInt(moves.Count) : 0;
                var chosen = moves[idx];

                // 마작 포함 리드 시 낮은 확률로 소원 설정.
                int? wish = null;
                for (int i = 0; i < chosen.Cards.Count; i++)
                {
                    if (chosen.Cards[i].Special == SpecialKind.Mahjong)
                    {
                        if (rng.NextInt(WishRate) == 0)
                            wish = rng.NextInt(13) + 2; // 2..14
                        break;
                    }
                }

                return GameAction.Play(seat, chosen.Cards, wish);
            }
            else
            {
                // 팔로우: 패스 포함하여 균등 선택.
                int optionCount = moves.Count + (canPass ? 1 : 0);

                if (optionCount == 0)
                {
                    // 소원 강제 + 패스 불가 + moves 없음 → 이론상 불발.
                    // 방어적으로 패스를 반환(엔진이 거부하면 Simulator가 감지).
                    return GameAction.Pass(seat);
                }

                int pick = optionCount > 1 ? rng.NextInt(optionCount) : 0;
                if (pick < moves.Count)
                    return GameAction.Play(seat, moves[pick].Cards);
                else
                    return GameAction.Pass(seat);
            }
        }
    }
}
