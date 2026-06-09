using System.Collections.Generic;
using Tichu.Core.Cards;
using Tichu.Core.Game;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    /// <summary>GameFlow 테스트에서 공유하는 상태/컨텍스트 구성 헬퍼.</summary>
    internal static class GameFlowHelpers
    {
        /// <summary>
        /// 지정한 손패로 Play 페이즈 상태를 직접 구성한다(셋업 우회).
        /// PlayPhaseTests / FlowQueryTests 에 있던 로컬 PlayState 패턴을 일반화한 버전.
        /// </summary>
        internal static GameState PlayState(int turn, params IReadOnlyList<Card>[] hands)
        {
            var s = new GameState
            {
                Phase = RoundPhase.Play,
                Turn = turn,
                CurrentTrick = null
                // Setup은 기본값 null — Play 페이즈 불변식
            };
            for (int i = 0; i < 4; i++)
            {
                s.Seats[i] = new PlayerSeat { SeatIndex = i };
                s.Seats[i].Hand.AddRange(hands[i]);
            }
            return s;
        }

        /// <summary>
        /// 지정한 손패로 GrandTichuDecision 페이즈 상태를 구성한다.
        /// GameEngine.NewRound 로 뼈대를 만든 뒤 손패를 교체 — Setup의 나머지 필드는 유지된다.
        /// hands[i]는 각 좌석의 8장 초기패여야 한다.
        /// </summary>
        internal static GameState GrandState(params IReadOnlyList<Card>[] hands)
        {
            // 임의 seed 로 NewRound → Phase=GrandTichuDecision, Setup 유효.
            var s = GameEngine.NewRound(0UL);
            for (int i = 0; i < 4; i++)
            {
                s.Seats[i].Hand.Clear();
                s.Seats[i].Hand.AddRange(hands[i]);
            }
            return s;
        }

        /// <summary>지정한 상태와 좌석으로 DecisionContext 를 생성한다.</summary>
        internal static DecisionContext Context(GameState s, int seat)
            => new DecisionContext(s, seat);
    }
}
